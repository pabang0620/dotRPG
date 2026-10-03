-- 0006_chat_social: 5단계(채팅·친구) 테이블
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0005와 같다. 선행: 0005_party.
-- 설계: Docs/server/phase5_api.md, Docs/server/phase5_mapping.md
--
-- 이 마이그레이션이 하는 일
--   1. accounts.last_character_id: 친구 목록에 보일 "그 계정의 대표 캐릭터 이름"(WebSocket 접속 때 갱신).
--   2. chat_messages: 일반·파티·귓속말 채팅 기록(전달 보장·놓친 메시지 따라잡기·신고 증거의 원본). 보관 짧게(기본 7일).
--   3. friendships: 친구 요청과 친구 관계(계정 단위, 상호 수락).
--   4. blocks: 차단(계정 단위. 캐릭터를 바꿔 우회하지 못하게).
--   5. reports, report_lines: 신고와 서버가 찍어 둔 증거 스냅샷(원본 채팅이 지워져도 남는다).
--   6. account_sanctions: 채팅 금지·경고·이용 정지 기록(자동 제재와 운영자 제재가 같은 표).
--   7. party_invites: 친구 초대(PLAN_ONLINE 2.3 "친구 목록(접속 상태, 초대)", 4단계에서 5단계로 넘긴 항목).
--
-- 이 단계에는 재화(골드·아이템·경험치)가 움직이는 경로가 없다. 그래서 원장·잔액 CHECK는 없고,
-- 멱등성은 REST는 request_log(account_id, request_id), 채팅 전송은 chat_messages UNIQUE(sender_account_id, client_msg_id)가 맡는다.
-- 날짜 경계(06:00 일일, 목요일 06:00 주간)는 쓰지 않는다. 이 단계의 기간은 전부 "지금부터 N일/분"이라 resetBoundaries와 무관하다.
--
-- 락 순서(5단계에서 추가): 4단계 규칙(캐릭터 id 오름차순 -> parties -> party_runs) 뒤에 friendships, blocks를 잠그는 요청은 없다.
-- 친구·차단·신고는 한 문장짜리 조건부 INSERT/UPDATE + 유니크 인덱스 충돌 처리로 끝나 별도 잠금을 두지 않는다.

-- ============ UP ============

-- ---------- 1. 계정의 대표 캐릭터 ----------

ALTER TABLE accounts ADD COLUMN last_character_id BIGINT REFERENCES characters(id);
COMMENT ON COLUMN accounts.last_character_id IS '마지막으로 WebSocket에 접속한 캐릭터. 친구 목록의 이름·직업·레벨 표시용(친구는 계정 단위라 접속 중이 아니면 이 캐릭터를 보여 준다). 삭제된 캐릭터면 서버가 가장 오래된 살아 있는 캐릭터로 대신한다';

-- ---------- 2. 채팅 기록 ----------

CREATE TABLE chat_messages (
  id                     BIGSERIAL PRIMARY KEY,
  channel                TEXT NOT NULL CHECK (channel IN ('general', 'party', 'whisper')),
  shard                  INT CHECK (shard >= 1),
  party_id               BIGINT REFERENCES parties(id),
  sender_account_id      BIGINT NOT NULL REFERENCES accounts(id),
  sender_character_id    BIGINT NOT NULL REFERENCES characters(id),
  sender_name            TEXT NOT NULL,
  recipient_account_id   BIGINT REFERENCES accounts(id),
  recipient_character_id BIGINT REFERENCES characters(id),
  recipient_name         TEXT,
  text                   TEXT NOT NULL CHECK (char_length(text) BETWEEN 1 AND 200),
  filtered               BOOLEAN NOT NULL DEFAULT false,
  client_msg_id          UUID NOT NULL,
  created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT chat_messages_shape_chk CHECK (
    (channel = 'general' AND shard IS NOT NULL AND party_id IS NULL
       AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
    OR (channel = 'party' AND party_id IS NOT NULL AND shard IS NULL
       AND recipient_account_id IS NULL AND recipient_character_id IS NULL AND recipient_name IS NULL)
    OR (channel = 'whisper' AND shard IS NULL AND party_id IS NULL
       AND recipient_account_id IS NOT NULL AND recipient_character_id IS NOT NULL AND recipient_name IS NOT NULL)
  ),
  CONSTRAINT chat_messages_client_uq UNIQUE (sender_account_id, client_msg_id)
);
COMMENT ON TABLE  chat_messages IS '채팅 기록. 서버가 저장한 뒤에 전달한다(저장 성공 = 전달 대상). id 순서가 곧 전달 순서이고, 프로세스 안 단일 쓰기 큐가 id 순서와 커밋 순서를 같게 만든다. 시스템 채널은 저장하지 않는다. 차단당한 귓속말은 저장하지 않는다';
COMMENT ON COLUMN chat_messages.id IS '순번(seq). 클라이언트에는 커서로만 나가고, 이 값으로 메시지를 조회하는 API는 없다(IDOR 대상 아님). 프로젝트 규칙 "내부 id 비노출"의 의도된 예외이며 uuid 열은 두지 않는다';
COMMENT ON COLUMN chat_messages.shard IS '일반 채널 방 번호(1부터). 시작은 1개뿐이고 정원(CHAT_SHARD_SIZE)을 넘으면 늘어난다. 파티·귓속말은 NULL';
COMMENT ON COLUMN chat_messages.party_id IS '파티 채널: 보낼 때의 파티. 놓친 메시지 따라잡기는 "내 현재 파티 + 내 가입 시각 이후"만 준다';
COMMENT ON COLUMN chat_messages.sender_name IS '보낸 캐릭터 이름 스냅샷(캐릭터가 지워져도 기록·신고에서 읽힌다)';
COMMENT ON COLUMN chat_messages.text IS '금칙어를 가린 뒤의 최종 문장(전달된 그대로). 원문은 저장하지 않는다. 실제 길이 한도는 server/data/chat.json의 maxLength(C# ChatRules.MaxLength)이고 DB는 이상값만 막는다';
COMMENT ON COLUMN chat_messages.filtered IS '금칙어를 가린 적이 있는가(자동 제재의 반복 횟수 근거)';
COMMENT ON COLUMN chat_messages.client_msg_id IS '클라이언트가 보낸 메시지 id(uuid). 같은 id 재전송은 새 행을 만들지 않고 처음 결과(ack)를 돌려준다. 채팅의 request_id';
-- 일반 채널 따라잡기: WHERE channel='general' AND shard=$1 AND id > $since ORDER BY id (최근 N건만)
CREATE INDEX chat_messages_general ON chat_messages (shard, id) WHERE channel = 'general';
-- 파티 채널 따라잡기: WHERE channel='party' AND party_id=$1 AND id > $since AND created_at >= 내 가입 시각
CREATE INDEX chat_messages_party ON chat_messages (party_id, id) WHERE channel = 'party';
-- 귓속말 따라잡기와 신고 증거(두 사람 사이의 양방향은 받는 사람 기준 조회 2번으로 모은다)
CREATE INDEX chat_messages_whisper ON chat_messages (recipient_account_id, id) WHERE channel = 'whisper';
-- 신고 증거: "신고 대상이 최근에 한 말" 조회
CREATE INDEX chat_messages_sender ON chat_messages (sender_account_id, id DESC);
-- 보관 기간 정리(created_at < 기준)
CREATE INDEX chat_messages_created ON chat_messages (created_at);

-- ---------- 3. 친구 ----------

CREATE TABLE friendships (
  id                     BIGSERIAL PRIMARY KEY,
  uuid                   UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  requester_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  target_account_id      BIGINT NOT NULL REFERENCES accounts(id),
  requester_character_id BIGINT NOT NULL REFERENCES characters(id),
  target_character_id    BIGINT NOT NULL REFERENCES characters(id),
  state                  TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'accepted', 'declined', 'cancelled', 'removed')),
  created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
  responded_at           TIMESTAMPTZ,
  ended_at               TIMESTAMPTZ,
  ended_by_account_id    BIGINT REFERENCES accounts(id),
  silent                 BOOLEAN NOT NULL DEFAULT false,
  CONSTRAINT friendships_self_chk  CHECK (requester_account_id <> target_account_id),
  CONSTRAINT friendships_ended_chk CHECK ((state IN ('pending', 'accepted')) = (ended_at IS NULL)),
  CONSTRAINT friendships_responded_chk CHECK (state = 'pending' OR state = 'cancelled' OR responded_at IS NOT NULL)
);
COMMENT ON TABLE  friendships IS '친구 요청과 관계(계정 단위, 상호 수락). 한 쌍(A,B)은 방향과 무관하게 pending 또는 accepted 행이 최대 하나. 끝난 행(declined, cancelled, removed)은 남겨 재요청 쿨다운·괴롭힘 확인에 쓴다';
COMMENT ON COLUMN friendships.silent IS '조용한 무시: 상대가 요청자를 차단했거나 상대의 한도가 가득 찼을 때 요청자에게만 보이는 요청으로 남긴다(응답 모양이 정상 요청과 같아 차단·상태가 드러나지 않는다). 상대 화면·알림·수락 대상에서 빠진다';
COMMENT ON COLUMN friendships.requester_character_id IS '요청을 보낸 캐릭터(받는 사람 화면의 "누가 보냈나" 이름)';
COMMENT ON COLUMN friendships.target_character_id IS '요청자가 지목한 캐릭터. 친구 관계 자체는 계정 단위이고, 목록에는 accounts.last_character_id가 나온다';
COMMENT ON COLUMN friendships.state IS 'pending 요청 중 / accepted 친구 / declined 받은 쪽이 거절 / cancelled 보낸 쪽이 취소 / removed 친구였다가 삭제(차단 포함)';
COMMENT ON COLUMN friendships.ended_by_account_id IS '끝낸 쪽(삭제·거절·취소·차단). 차단으로 끝난 행은 ended_by = 차단한 계정';
-- 같은 두 계정 사이의 살아 있는 행은 하나(A->B 요청과 B->A 요청이 동시에 와도 하나만 남는다)
CREATE UNIQUE INDEX friendships_pair_live ON friendships
  (LEAST(requester_account_id, target_account_id), GREATEST(requester_account_id, target_account_id))
  WHERE state IN ('pending', 'accepted');
-- 내 친구·보낸 요청(요청자 쪽)과 받은 요청·친구(대상 쪽): 두 인덱스를 UNION으로 읽는다
CREATE INDEX friendships_requester ON friendships (requester_account_id) WHERE state IN ('pending', 'accepted');
CREATE INDEX friendships_target    ON friendships (target_account_id)    WHERE state IN ('pending', 'accepted');
-- 거절·취소 뒤 재요청 쿨다운(FRIEND_REREQUEST_HOURS)
CREATE INDEX friendships_pair_hist ON friendships (requester_account_id, target_account_id, created_at DESC);
-- 보관 기간 정리
CREATE INDEX friendships_ended ON friendships (ended_at) WHERE ended_at IS NOT NULL;

-- ---------- 4. 차단 ----------

CREATE TABLE blocks (
  id                   BIGSERIAL PRIMARY KEY,
  uuid                 UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  blocker_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  blocked_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  blocked_character_id BIGINT NOT NULL REFERENCES characters(id),
  blocked_name         TEXT NOT NULL,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  deleted_at           TIMESTAMPTZ,
  CONSTRAINT blocks_self_chk CHECK (blocker_account_id <> blocked_account_id)
);
COMMENT ON TABLE  blocks IS '차단(계정 단위: 상대가 다른 캐릭터로 접속해도 막힌다). 차단하면 상대의 일반·파티·귓속말·친구 요청·파티 초대가 나에게 오지 않는다. 해제는 deleted_at(소프트 삭제)';
COMMENT ON COLUMN blocks.blocked_character_id IS '차단 당시 지목한 캐릭터';
COMMENT ON COLUMN blocks.blocked_name IS '차단 목록에 보일 이름 스냅샷(캐릭터가 지워져도 목록이 읽힌다)';
-- 내 차단 목록 로드(접속 때 세션 메모리로), 차단 여부 확인(친구 요청·초대·귓속말), 중복 차단 방지
CREATE UNIQUE INDEX blocks_pair_live ON blocks (blocker_account_id, blocked_account_id) WHERE deleted_at IS NULL;

-- ---------- 5. 신고 ----------

CREATE TABLE reports (
  id                    BIGSERIAL PRIMARY KEY,
  uuid                  UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  reporter_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  reporter_character_id BIGINT NOT NULL REFERENCES characters(id),
  target_account_id     BIGINT NOT NULL REFERENCES accounts(id),
  target_character_id   BIGINT NOT NULL REFERENCES characters(id),
  target_name           TEXT NOT NULL,
  reason                TEXT NOT NULL CHECK (reason IN ('abuse', 'spam', 'scam_ad', 'cheat', 'other')),
  state                 TEXT NOT NULL DEFAULT 'open' CHECK (state IN ('open', 'reviewing', 'actioned', 'dismissed')),
  line_count            SMALLINT NOT NULL DEFAULT 0 CHECK (line_count >= 0),
  created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
  handled_at            TIMESTAMPTZ,
  handled_by            TEXT,
  note                  TEXT CHECK (char_length(note) <= 500),
  CONSTRAINT reports_self_chk CHECK (reporter_account_id <> target_account_id),
  CONSTRAINT reports_handled_chk CHECK ((state IN ('actioned', 'dismissed')) = (handled_at IS NOT NULL))
);
COMMENT ON TABLE  reports IS '신고 접수. 증거는 클라이언트가 보낸 값이 아니라 서버가 접수 순간 chat_messages에서 찍어 report_lines에 복사한다';
COMMENT ON COLUMN reports.reason IS 'abuse 욕설·비하 / spam 도배 / scam_ad 광고·사기 / cheat 불법 프로그램 의심 / other 기타 (C# ChatRules.ReportReasons 5개와 1:1)';
COMMENT ON COLUMN reports.state IS 'open 접수 / reviewing 운영자 검토 중 / actioned 제재함(account_sanctions.report_id로 연결) / dismissed 위반 없음·중복';
COMMENT ON COLUMN reports.line_count IS '붙은 증거 줄 수(report_lines 행 수)';
COMMENT ON COLUMN reports.handled_by IS '처리한 운영자 표시(7단계 관리자 도구 전에는 SQL을 실행한 사람 이름)';
-- 같은 사람이 같은 대상을 같은 사유로 처리 전에 다시 신고하는 것을 막는다(두 번째는 기존 신고를 돌려준다)
CREATE UNIQUE INDEX reports_open_uq ON reports (reporter_account_id, target_account_id, reason) WHERE state IN ('open', 'reviewing');
-- 운영자 대기열(오래된 것부터)
CREATE INDEX reports_queue ON reports (created_at) WHERE state IN ('open', 'reviewing');
-- 한 사람에 대한 신고 모음(서로 다른 신고자 수 세기, 운영자 조회)
CREATE INDEX reports_target ON reports (target_account_id, created_at DESC);
-- 신고자별 시간당·일일 한도 검사
CREATE INDEX reports_reporter ON reports (reporter_account_id, created_at DESC);

CREATE TABLE report_lines (
  id                  BIGSERIAL PRIMARY KEY,
  report_id           BIGINT NOT NULL REFERENCES reports(id),
  seq                 BIGINT NOT NULL,
  channel             TEXT NOT NULL CHECK (channel IN ('general', 'party', 'whisper')),
  sender_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  sender_character_id BIGINT NOT NULL REFERENCES characters(id),
  sender_name         TEXT NOT NULL,
  recipient_name      TEXT,
  text                TEXT NOT NULL,
  sent_at             TIMESTAMPTZ NOT NULL,
  is_target           BOOLEAN NOT NULL,
  UNIQUE (report_id, seq)
);
COMMENT ON TABLE  report_lines IS '신고 증거 스냅샷(접수 때 chat_messages에서 복사). 원본이 보관 기간으로 지워져도 남는다. 수정 불가(UPDATE 트리거). 정리는 신고가 닫힌 뒤 REPORT_RETENTION_DAYS';
COMMENT ON COLUMN report_lines.seq IS '원본 chat_messages.id(원본은 지워졌을 수 있어 FK를 두지 않는다). UNIQUE(report_id, seq)가 증거 줄의 순서와 중복 방지를 맡는다';
COMMENT ON COLUMN report_lines.is_target IS '신고 대상이 한 말인가(운영자 화면 강조용)';
CREATE TRIGGER report_lines_no_update BEFORE UPDATE ON report_lines
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 6. 제재 ----------

CREATE TABLE account_sanctions (
  id          BIGSERIAL PRIMARY KEY,
  uuid        UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  kind        TEXT NOT NULL CHECK (kind IN ('warning', 'chat_mute', 'ban')),
  source      TEXT NOT NULL CHECK (source IN ('auto_filter', 'auto_spam', 'auto_report', 'admin')),
  reason_code TEXT NOT NULL CHECK (reason_code IN ('abuse', 'spam', 'scam_ad', 'cheat', 'other', 'filter_strikes', 'repeat_spam')),
  report_id   BIGINT REFERENCES reports(id),
  starts_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  ends_at     TIMESTAMPTZ,
  created_by  TEXT NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  notified_at TIMESTAMPTZ,
  revoked_at  TIMESTAMPTZ,
  revoked_by  TEXT,
  note        TEXT CHECK (char_length(note) <= 500),
  CONSTRAINT account_sanctions_mute_chk   CHECK (kind <> 'chat_mute' OR ends_at IS NOT NULL),
  CONSTRAINT account_sanctions_warn_chk   CHECK (kind <> 'warning' OR ends_at IS NULL),
  CONSTRAINT account_sanctions_range_chk  CHECK (ends_at IS NULL OR ends_at > starts_at),
  CONSTRAINT account_sanctions_revoke_chk CHECK ((revoked_at IS NULL) = (revoked_by IS NULL))
);
COMMENT ON TABLE  account_sanctions IS '제재 기록. 자동(system)과 운영자가 같은 표를 쓴다. chat_mute는 이 표만 본다. ban은 같은 트랜잭션에서 accounts.banned_until도 갱신하고(정지 판정은 기존 인증 미들웨어가 한다), 풀 때도 같이 되돌린다. 기록은 지우지 않는다';
COMMENT ON COLUMN account_sanctions.kind IS 'warning 경고(접속 때 한 번 알림) / chat_mute 채팅 금지(ends_at까지) / ban 이용 정지(ends_at NULL = 무기한)';
COMMENT ON COLUMN account_sanctions.source IS 'auto_filter 금칙어 반복 / auto_spam 도배 반복 / auto_report 서로 다른 신고자 다수(기본 꺼짐) / admin 운영자';
COMMENT ON COLUMN account_sanctions.created_by IS '자동이면 system, 운영자면 표시 이름';
COMMENT ON COLUMN account_sanctions.notified_at IS '플레이어에게 알린 시각. NULL이면 다음 WebSocket 접속(또는 즉시 푸시)에서 알리고 채운다';
-- 지금 채팅 금지인가: WHERE account_id=$1 AND kind='chat_mute' AND revoked_at IS NULL AND ends_at > now()
CREATE INDEX account_sanctions_active ON account_sanctions (account_id, kind, ends_at) WHERE revoked_at IS NULL;
-- 접속 때 아직 알리지 않은 제재 찾기
CREATE INDEX account_sanctions_unnotified ON account_sanctions (account_id) WHERE notified_at IS NULL AND revoked_at IS NULL;
-- 자동 제재 단계 올리기(최근 24시간 자동 금지 횟수), 운영자 이력 조회
CREATE INDEX account_sanctions_history ON account_sanctions (account_id, created_at DESC);
-- 제재가 생기거나 바뀌면(자동이든 운영자 SQL이든) 서버 프로세스가 LISTEN으로 즉시 알아 접속 중인 소켓의 금지 상태를 갱신하고 ban이면 끊는다.
-- 7단계 관리자 도구 전에도 운영자가 SQL만 실행하면 반영된다. 서버가 여러 대가 되어도 같은 방식이 그대로 동작한다.
CREATE FUNCTION notify_account_sanction() RETURNS trigger AS $$
BEGIN
  PERFORM pg_notify('dotrpg_sanction', NEW.account_id::text);
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER account_sanctions_notify AFTER INSERT OR UPDATE ON account_sanctions
  FOR EACH ROW EXECUTE FUNCTION notify_account_sanction();

-- ---------- 7. 파티 초대 ----------

CREATE TABLE party_invites (
  id                   BIGSERIAL PRIMARY KEY,
  uuid                 UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id             BIGINT NOT NULL REFERENCES parties(id),
  inviter_character_id BIGINT NOT NULL REFERENCES characters(id),
  invitee_character_id BIGINT NOT NULL REFERENCES characters(id),
  invitee_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  state                TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'accepted', 'declined', 'expired', 'cancelled')),
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at           TIMESTAMPTZ NOT NULL,
  responded_at         TIMESTAMPTZ,
  silent               BOOLEAN NOT NULL DEFAULT false,
  CONSTRAINT party_invites_responded_chk CHECK ((state IN ('accepted', 'declined')) = (responded_at IS NOT NULL) OR state IN ('expired', 'cancelled'))
);
COMMENT ON TABLE  party_invites IS '파티 초대(만든 뒤 PARTY_INVITE_SECONDS 동안 유효, 지연 만료). 받는 사람은 WebSocket으로 즉시 받고, 접속 중이 아니면 못 보낸다. 수락은 4단계 신청 수락과 같은 검사(자격·전투력·정원)를 지난다';
COMMENT ON COLUMN party_invites.silent IS '조용한 무시: 받는 사람이 방장을 차단했으면 방장에게만 정상 초대처럼 보이는 행으로 남긴다(멱등·쿨다운·취소가 같게 동작). 받는 사람에게는 푸시도 목록도 없다';
COMMENT ON COLUMN party_invites.invitee_account_id IS '받는 사람의 계정(차단 확인, 푸시 대상)';
-- 같은 파티가 같은 사람에게 동시에 둘 이상 보내지 못한다
CREATE UNIQUE INDEX party_invites_pending_uq ON party_invites (party_id, invitee_character_id) WHERE state = 'pending';
-- 받는 사람이 접속할 때 대기 중 초대 조회, 수락·거절 경로
CREATE INDEX party_invites_invitee ON party_invites (invitee_character_id, created_at DESC) WHERE state = 'pending';
-- 파티가 가진 대기 초대 수 제한(정원 초과 방지)과 보관 기간 정리
CREATE INDEX party_invites_party ON party_invites (party_id) WHERE state = 'pending';
CREATE INDEX party_invites_created ON party_invites (created_at);

-- ============ DOWN ============
-- 개발 DB 전용. 채팅·친구·차단·신고·제재 기록이 모두 사라진다. 운영에서는 실행하지 않는다.
-- ban 제재로 올려 둔 accounts.banned_until은 되돌리지 않는다(필요하면 직접 푼다).

DROP TABLE IF EXISTS party_invites;
DROP TABLE IF EXISTS account_sanctions;
DROP FUNCTION IF EXISTS notify_account_sanction();
DROP TABLE IF EXISTS report_lines;
DROP TABLE IF EXISTS reports;
DROP TABLE IF EXISTS blocks;
DROP TABLE IF EXISTS friendships;
DROP TABLE IF EXISTS chat_messages;
ALTER TABLE accounts DROP COLUMN IF EXISTS last_character_id;
