-- 0005_party: 4단계(파티 협동) 테이블
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0004와 같다. 선행: 0003_dungeon_solo, 0004_gather_rate.
-- 설계: Docs/server/phase4_api.md, Docs/server/phase4_mapping.md
--
-- 이 마이그레이션이 하는 일
--   1. Steam 로그인: auth_identities를 "계정당 공급자 하나"로 제한한다(provider 'steam'은 0001에 이미 있다).
--   2. 파티 로비·모집 게시판: parties, party_members, party_applications.
--   3. 파티 던전: party_runs(같은 던전을 함께 도는 한 판), party_run_members, party_run_host_reports(방장 보고).
--   4. dungeon_runs 확장: 멤버별 한 줄(party_run_id, slot), AI 인원, 레이드(입장 횟수 제외, 보상 잠금), 대조 대기('reported') 상태.
--   5. 레이드 일일·주간 보상 기록: raid_claims.
--   6. item_ledger reason(raid_key, raid_key_cost), anomaly_log kind(party_result, party_host, raid_enter) 확장.
--
-- 만들지 않는 것: 매칭 대기열 표. 대기열은 서버 메모리(QueueStore 인터페이스 뒤, PLAN_SERVER §7)이고 서버가 재시작하면
-- 대기가 사라진다(클라이언트는 다음 폴링에서 queue=null을 보고 다시 건다). 맞춰진 결과만 parties 행으로 남는다.
--
-- 락 순서(4단계에서 추가): ① 필요한 캐릭터 행을 id 오름차순으로 한 번에 FOR UPDATE ② parties ③ party_runs.
-- 한 요청이 여러 캐릭터를 잠그는 곳은 파티 출발(start)과 판 시작(begin)뿐이다. 나머지는 3단계와 같이 자기 캐릭터 하나만 잠근다.

-- ============ UP ============

-- ---------- 1. Steam 로그인 ----------

CREATE UNIQUE INDEX auth_identities_account_provider_uq ON auth_identities (account_id, provider);

-- ---------- 2. 원장 reason, 이상 기록 kind 확장 ----------

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter'));

-- ---------- 3. 파티 로비·모집 게시판 ----------

CREATE TABLE parties (
  id                  BIGSERIAL PRIMARY KEY,
  uuid                UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  leader_character_id BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id          TEXT NOT NULL,
  difficulty          SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  max_members         SMALLINT NOT NULL CHECK (max_members BETWEEN 2 AND 4),
  min_power           INT NOT NULL DEFAULT 0 CHECK (min_power >= 0),
  message             TEXT NOT NULL DEFAULT '' CHECK (char_length(message) <= 30),
  source              TEXT NOT NULL CHECK (source IN ('board', 'match')),
  listed              BOOLEAN NOT NULL,
  listed_until        TIMESTAMPTZ,
  state               TEXT NOT NULL DEFAULT 'forming' CHECK (state IN ('forming', 'starting', 'in_run', 'closed')),
  version             INT NOT NULL DEFAULT 0 CHECK (version >= 0),
  start_by            TIMESTAMPTZ,
  last_active_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  closed_at           TIMESTAMPTZ,
  close_reason        TEXT CHECK (close_reason IN ('disbanded', 'idle_timeout', 'start_timeout')),
  CONSTRAINT parties_listed_chk CHECK (NOT listed OR listed_until IS NOT NULL),
  CONSTRAINT parties_closed_chk CHECK ((state = 'closed') = (closed_at IS NOT NULL) AND (state = 'closed') = (close_reason IS NOT NULL))
);
COMMENT ON TABLE  parties IS '파티 로비. 모집 글(listed)과 자동 매칭 결과(source=match)를 같은 표로 둔다. 판이 끝나도 파티는 남아 다시 도전할 수 있다';
COMMENT ON COLUMN parties.uuid IS '외부 노출 id(모집 글 id, 참가 신청 경로). 내부 키 id는 API에 나가지 않는다';
COMMENT ON COLUMN parties.leader_character_id IS '방장(로비 방장 = 판의 첫 호스트). 활성 멤버여야 한다. 방장이 나가면 가장 오래된 멤버로 서버가 즉시 넘긴다';
COMMENT ON COLUMN parties.dungeon_id IS 'dungeons.json 던전 id. 요일 던전과 레이드 모두 가능';
COMMENT ON COLUMN parties.difficulty IS 'DungeonDifficulty 0~3. 레이드는 0';
COMMENT ON COLUMN parties.max_members IS '사람 정원 2~4. 빈자리 AI는 정원에 세지 않고 출발할 때 정한다';
COMMENT ON COLUMN parties.min_power IS '참가 최소 전투력. 서버가 계산한 power_estimate와 비교하고 방장 자신의 값을 넘을 수 없다';
COMMENT ON COLUMN parties.message IS '모집 메시지 30자 이하. 금칙어는 서버가 최종 판정한다';
COMMENT ON COLUMN parties.source IS 'board 방장이 만든 모집 글 / match 자동 매칭이 만든 파티(멤버 준비 완료 상태로 시작)';
COMMENT ON COLUMN parties.listed IS '모집 게시판에 보이는가. false면 신청을 받지 않는다(비공개 파티는 5단계 초대용)';
COMMENT ON COLUMN parties.listed_until IS '게시 만료(만든 뒤 10분). 만료돼도 파티는 남고 목록에서만 빠진다';
COMMENT ON COLUMN parties.state IS 'forming 모집·로비 / starting 판 모으는 중(party_runs.gathering) / in_run 판 진행 중 / closed 해산';
COMMENT ON COLUMN parties.version IS '변경될 때마다 +1. 클라이언트 폴링이 after_version으로 변화를 확인한다';
COMMENT ON COLUMN parties.start_by IS '자동 매칭 파티의 출발 기한(만든 뒤 60초). 넘기면 해산한다. board 파티는 NULL';
COMMENT ON COLUMN parties.last_active_at IS '마지막 활동 시각. 30분 지나면 지연 정리가 해산한다';
-- 모집 게시판 목록: WHERE listed AND state='forming' AND listed_until > now() [AND dungeon_id=$1 AND difficulty=$2] ORDER BY created_at DESC
CREATE INDEX parties_board ON parties (dungeon_id, difficulty, created_at DESC) WHERE listed AND state = 'forming';
-- 방장 한 명이 만든 열린 파티 찾기, 방장 권한 검사
CREATE INDEX parties_leader ON parties (leader_character_id) WHERE state <> 'closed';
-- 방치 파티 지연 정리
CREATE INDEX parties_idle ON parties (last_active_at) WHERE state <> 'closed';

CREATE TABLE party_members (
  id           BIGSERIAL PRIMARY KEY,
  party_id     BIGINT NOT NULL REFERENCES parties(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  ready        BOOLEAN NOT NULL DEFAULT false,
  joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  left_at      TIMESTAMPTZ,
  left_reason  TEXT CHECK (left_reason IN ('left', 'kicked', 'disbanded', 'idle_timeout', 'start_timeout', 'matched_other')),
  CONSTRAINT party_members_left_chk CHECK ((left_at IS NULL) = (left_reason IS NULL))
);
COMMENT ON TABLE  party_members IS '파티 멤버(한 번의 소속 = 한 행). 나가면 left_at을 채우고 행은 남긴다(알림·분쟁 확인용)';
COMMENT ON COLUMN party_members.ready IS '준비 완료. 방장은 항상 준비로 본다. 구성이 바뀌면(입장·방장 설정 변경) 멤버의 준비는 해제된다';
COMMENT ON COLUMN party_members.joined_at IS '입장 시각. 방장이 나갈 때 가장 오래된 멤버가 다음 방장';
COMMENT ON COLUMN party_members.left_reason IS 'left 스스로 / kicked 강퇴 / disbanded 파티 해산 / idle_timeout·start_timeout 지연 정리 / matched_other 다른 매칭 파티로 이동';
-- 한 캐릭터는 동시에 한 파티에만 있다(DB 수준 이중 입장 방지)
CREATE UNIQUE INDEX party_members_one_active ON party_members (character_id) WHERE left_at IS NULL;
-- 파티 멤버 목록
CREATE INDEX party_members_party ON party_members (party_id) WHERE left_at IS NULL;
-- "파티가 해산됐다" 알림: 최근에 나간 기록 조회
CREATE INDEX party_members_notice ON party_members (character_id, left_at DESC) WHERE left_at IS NOT NULL;

CREATE TABLE party_applications (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id       BIGINT NOT NULL REFERENCES parties(id),
  character_id   BIGINT NOT NULL REFERENCES characters(id),
  account_id     BIGINT NOT NULL REFERENCES accounts(id),
  state          TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'accepted', 'rejected', 'expired', 'cancelled')),
  power_estimate INT NOT NULL CHECK (power_estimate >= 0),
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at     TIMESTAMPTZ NOT NULL,
  responded_at   TIMESTAMPTZ
);
COMMENT ON TABLE  party_applications IS '참가 신청. 방장이 수락·거절하고, 30초 무응답이면 만료(지연 처리)';
COMMENT ON COLUMN party_applications.power_estimate IS '신청 시점에 서버가 계산한 전투력(방장 화면 표시용 스냅샷). 클라이언트 값이 아니다';
COMMENT ON COLUMN party_applications.expires_at IS '신청 시각 + 30초';
-- 같은 파티에 신청 중복 방지
CREATE UNIQUE INDEX party_applications_pending_uq ON party_applications (party_id, character_id) WHERE state = 'pending';
-- 방장 폴링: 우리 파티의 대기 신청
CREATE INDEX party_applications_party ON party_applications (party_id) WHERE state = 'pending';
-- 신청자 폴링: 내 신청 상태(최대 3건 동시 신청 검사 포함)
CREATE INDEX party_applications_char ON party_applications (character_id, created_at DESC);

-- ---------- 4. 파티 던전 판 ----------

CREATE TABLE party_runs (
  id                 BIGSERIAL PRIMARY KEY,
  uuid               UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  party_id           BIGINT NOT NULL REFERENCES parties(id),
  dungeon_id         TEXT NOT NULL,
  difficulty         SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  state              TEXT NOT NULL DEFAULT 'gathering' CHECK (state IN ('gathering', 'playing', 'ended', 'cancelled')),
  host_character_id  BIGINT NOT NULL REFERENCES characters(id),
  host_epoch         INT NOT NULL DEFAULT 1 CHECK (host_epoch >= 1),
  humans             SMALLINT NOT NULL CHECK (humans BETWEEN 1 AND 4),
  ai_count           SMALLINT NOT NULL DEFAULT 0 CHECK (ai_count BETWEEN 0 AND 3),
  run_key            BYTEA CHECK (octet_length(run_key) = 32),
  power_cap          NUMERIC(14, 3) CHECK (power_cap > 0),
  gather_deadline_at TIMESTAMPTZ NOT NULL,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
  begun_at           TIMESTAMPTZ,
  first_report_at    TIMESTAMPTZ,
  ended_at           TIMESTAMPTZ,
  cancel_reason      TEXT CHECK (cancel_reason IN ('timeout', 'host_cancel', 'nobody_joined', 'ineligible')),
  CONSTRAINT party_runs_begun_chk  CHECK ((state IN ('playing', 'ended')) = (begun_at IS NOT NULL)),
  CONSTRAINT party_runs_end_chk    CHECK ((state IN ('ended', 'cancelled')) = (ended_at IS NOT NULL)),
  CONSTRAINT party_runs_cancel_chk CHECK ((state = 'cancelled') = (cancel_reason IS NOT NULL)),
  CONSTRAINT party_runs_size_chk   CHECK (humans + ai_count <= 4)
);
COMMENT ON TABLE  party_runs IS '같은 던전을 함께 도는 한 판(PLAN_ONLINE의 run_instances). 멤버별 보상 행은 dungeon_runs, 이 표는 방장·인원·시작과 호스트 세대만 가진다';
COMMENT ON COLUMN party_runs.uuid IS '외부 노출 id. 입장 토큰의 기준, 호스트 보고·하트비트 경로';
COMMENT ON COLUMN party_runs.host_character_id IS '지금 몬스터를 계산하는 방장 PC의 캐릭터. 호스트 인계로 바뀐다';
COMMENT ON COLUMN party_runs.host_epoch IS '호스트 세대. 인계마다 +1. 옛 세대의 보고는 거절한다(스플릿 브레인 방지)';
COMMENT ON COLUMN party_runs.humans IS 'gathering 동안은 출발 때 사람 수, begin 때 실제로 들어온 사람 수로 고정';
COMMENT ON COLUMN party_runs.ai_count IS '빈자리 AI 수. begin에서 고정(방장이 정한 수 + 불참 인원, 합이 4 이하). 던전 안에서 바꾸지 않는다';
COMMENT ON COLUMN party_runs.run_key IS '이 판의 입장 토큰 서명 키(난수 32바이트). 방장 PC만 받는다(출발 응답·호스트 인계 응답). 판이 끝나면 NULL';
COMMENT ON COLUMN party_runs.power_cap IS 'begin 때 계산한 파티 화력 상한(사람 멤버 attackCap 합 + AI 몫). 처치 보고의 화력 검사가 쓴다';
COMMENT ON COLUMN party_runs.gather_deadline_at IS '입장 마감(출발 + 90초). 넘으면 들어온 사람으로 시작하거나 취소';
COMMENT ON COLUMN party_runs.begun_at IS '판 시작 시각. 모든 멤버 dungeon_runs.started_at이 같은 값';
COMMENT ON COLUMN party_runs.first_report_at IS '첫 결과 보고 시각. 결과 대조 대기 시간의 기준';
-- 한 파티는 동시에 한 판만
CREATE UNIQUE INDEX party_runs_one_active ON party_runs (party_id) WHERE state IN ('gathering', 'playing');
-- 방치된 판 정리
CREATE INDEX party_runs_active_idx ON party_runs (created_at) WHERE state IN ('gathering', 'playing');

-- dungeon_runs 확장 (party_runs 이후에 만든다)
ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_state_check;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_state_check
  CHECK (state IN ('playing', 'reported', 'cleared', 'failed', 'abandoned', 'held'));
ALTER TABLE dungeon_runs ADD COLUMN party_run_id BIGINT REFERENCES party_runs(id);
ALTER TABLE dungeon_runs ADD COLUMN slot SMALLINT CHECK (slot BETWEEN 0 AND 3);
ALTER TABLE dungeon_runs ADD COLUMN humans SMALLINT NOT NULL DEFAULT 1 CHECK (humans BETWEEN 1 AND 4);
ALTER TABLE dungeon_runs ADD COLUMN ai_count SMALLINT NOT NULL DEFAULT 0 CHECK (ai_count BETWEEN 0 AND 3);
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_size_chk CHECK (party_size = humans + ai_count);
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_party_chk CHECK ((party_run_id IS NULL) = (slot IS NULL));
ALTER TABLE dungeon_runs ADD COLUMN counts_entry BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE dungeon_runs ADD COLUMN reward_locked BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE dungeon_runs ADD COLUMN lock_reason TEXT CHECK (lock_reason IN ('ALREADY_CLAIMED', 'TOO_FEW_HUMANS', 'KEYS_MISSING'));
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_lock_chk CHECK (reward_locked = (lock_reason IS NOT NULL));
ALTER TABLE dungeon_runs ADD COLUMN power_cap NUMERIC(14, 3) CHECK (power_cap > 0);
ALTER TABLE dungeon_runs ADD COLUMN reported_outcome TEXT CHECK (reported_outcome IN ('cleared', 'failed'));
ALTER TABLE dungeon_runs ADD COLUMN reported_at TIMESTAMPTZ;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_reported_chk
  CHECK (state <> 'reported' OR (reported_outcome IS NOT NULL AND reported_at IS NOT NULL));
-- 연습 입장(reward_locked)의 클리어는 카드가 없다: 0003의 cleared 제약을 "잠기지 않았으면 카드 필수"로 완화
ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_cleared_chk;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_cleared_chk
  CHECK (state <> 'cleared' OR (rank IS NOT NULL AND ended_at IS NOT NULL AND (reward_locked OR cards IS NOT NULL)));
COMMENT ON COLUMN dungeon_runs.state IS 'playing 진행 / reported 결과 보고함(파티 판: 다른 사람의 보고와 대조 대기) / cleared 클리어(카드 있음) / failed 실패 보고 / abandoned 방치·이탈 / held 불가능해 보여 보상 보류';
COMMENT ON COLUMN dungeon_runs.party_run_id IS '파티 판이면 party_runs.id, 솔로(AI 동반 포함)는 NULL';
COMMENT ON COLUMN dungeon_runs.slot IS '파티 판에서의 자리 0~3(사람은 앞 번호, AI는 뒤). 솔로는 NULL';
COMMENT ON COLUMN dungeon_runs.humans IS '이 판의 사람 수. 파티 크기 = humans + ai_count';
COMMENT ON COLUMN dungeon_runs.ai_count IS '빈자리 AI 수. 몬스터 HP 배율(partyHpScale)과 화력 상한이 이 값을 쓴다';
COMMENT ON COLUMN dungeon_runs.counts_entry IS '일일 입장 횟수에 세는가. 레이드는 false(레이드는 횟수 제한 대신 일일·주간 보상 제한)';
COMMENT ON COLUMN dungeon_runs.reward_locked IS '연습 입장. 클리어 경험치·카드·열쇠·보상 청구가 없다(레이드 보상을 이미 받은 기간, 사람 수 부족, 열쇠 부족)';
COMMENT ON COLUMN dungeon_runs.lock_reason IS 'ALREADY_CLAIMED 이번 기간 보상 수령 / TOO_FEW_HUMANS 보상 최소 인원 미달 / KEYS_MISSING 정산 때 봉인 열쇠 부족';
COMMENT ON COLUMN dungeon_runs.power_cap IS '이 판의 화력 상한 스냅샷(솔로+AI: 본인 attackCap x (1 + AI 몫), 파티: party_runs.power_cap과 같은 값). NULL이면 3단계 방식(본인만)';
COMMENT ON COLUMN dungeon_runs.reported_outcome IS '멤버가 보고한 결과(cleared | failed). 대조 전에는 확정이 아니다';
COMMENT ON COLUMN dungeon_runs.reported_at IS '멤버가 결과를 보고한 시각';
DROP INDEX dungeon_runs_char_day;
-- 오늘 입장 수 집계(레이드 제외): WHERE character_id = $1 AND reset_day = $2 AND counts_entry
CREATE INDEX dungeon_runs_char_day ON dungeon_runs (character_id, reset_day) WHERE counts_entry;
-- 한 판의 모든 멤버 행(결과 대조, 정산)
CREATE INDEX dungeon_runs_party_idx ON dungeon_runs (party_run_id) WHERE party_run_id IS NOT NULL;

CREATE TABLE party_run_members (
  party_run_id    BIGINT NOT NULL REFERENCES party_runs(id),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  account_id      BIGINT NOT NULL REFERENCES accounts(id),
  slot            SMALLINT NOT NULL CHECK (slot BETWEEN 0 AND 3),
  state           TEXT NOT NULL DEFAULT 'invited'
                  CHECK (state IN ('invited', 'joined', 'playing', 'disconnected', 'done', 'left', 'no_show', 'dropped')),
  left_reason     TEXT CHECK (left_reason IN ('left', 'rejoin_timeout')),
  dungeon_run_id  BIGINT REFERENCES dungeon_runs(id),
  joined_at       TIMESTAMPTZ,
  last_seen_at    TIMESTAMPTZ,
  disconnected_at TIMESTAMPTZ,
  finished_at     TIMESTAMPTZ,
  PRIMARY KEY (party_run_id, character_id),
  UNIQUE (party_run_id, slot),
  CONSTRAINT party_run_members_left_chk CHECK ((state = 'left') = (left_reason IS NOT NULL))
);
COMMENT ON TABLE  party_run_members IS '판의 멤버(사람만). 입장 토큰 발급·입장 확인·하트비트·호스트 인계 자격의 근거';
COMMENT ON COLUMN party_run_members.slot IS '자리. 방장이 0, 나머지는 파티 입장 순. 호스트 인계는 살아 있는 가장 작은 자리가 이어받는다';
COMMENT ON COLUMN party_run_members.state IS 'invited 출발 알림 받음 / joined 방장에게 연결됨(입장 확인) / playing 판 진행 / disconnected 하트비트 끊김(AI가 대신 싸움, 60초 안에 돌아오면 playing) / done 결과를 보고했거나 판이 끝남 / left 이탈(결과 보상 없음) / no_show 입장 마감까지 안 들어옴 / dropped begin 때 자격 미달';
COMMENT ON COLUMN party_run_members.left_reason IS 'left 스스로 이탈 / rejoin_timeout 끊긴 뒤 60초 안에 못 돌아옴. 판 안에서 강퇴하는 API는 없다(방장이 P2P 연결을 끊어도 서버 기록은 그대로이고, 내보내진 사람은 이탈 API를 부른다)';
COMMENT ON COLUMN party_run_members.dungeon_run_id IS 'begin이 만든 이 멤버의 dungeon_runs 행';
COMMENT ON COLUMN party_run_members.last_seen_at IS '마지막 하트비트(서버 시계). 호스트 생존 판단과 연결 끊김 판단의 근거';
-- 한 캐릭터는 동시에 한 판에만 참여(솔로 입장·다른 파티 출발도 막는다)
CREATE UNIQUE INDEX party_run_members_one_active ON party_run_members (character_id)
  WHERE state IN ('invited', 'joined', 'playing', 'disconnected');

CREATE TABLE party_run_host_reports (
  id                BIGSERIAL PRIMARY KEY,
  party_run_id      BIGINT NOT NULL REFERENCES party_runs(id),
  host_epoch        INT NOT NULL CHECK (host_epoch >= 1),
  host_character_id BIGINT NOT NULL REFERENCES characters(id),
  request_id        UUID NOT NULL,
  outcome           TEXT NOT NULL CHECK (outcome IN ('cleared', 'failed')),
  elapsed_ms        INT NOT NULL CHECK (elapsed_ms >= 0),
  rooms             JSONB NOT NULL CHECK (jsonb_typeof(rooms) = 'array'),
  members           JSONB NOT NULL CHECK (jsonb_typeof(members) = 'array'),
  ai                JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(ai) = 'array'),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (party_run_id, host_epoch)
);
COMMENT ON TABLE  party_run_host_reports IS '방장이 보낸 판 전체 관찰(결과 대조의 기준). 추가만 한다(트리거가 UPDATE/DELETE 차단). 호스트 세대마다 한 번';
COMMENT ON COLUMN party_run_host_reports.rooms IS '[{room_index, kills:[{monster_id,count}]}] 방별 처치 수(방장이 본 값)';
COMMENT ON COLUMN party_run_host_reports.members IS '[{character_id(uuid), hits_taken, max_combo, revives_used, damage_dealt}] 사람 멤버별 관찰. 지급 값이 아니라 대조·화력 검사용 사실';
COMMENT ON COLUMN party_run_host_reports.ai IS '[{slot, damage_dealt}] AI별 딜. 화력 검사에만 쓴다';
CREATE TRIGGER party_run_host_reports_append_only BEFORE UPDATE OR DELETE ON party_run_host_reports
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER party_run_host_reports_no_truncate BEFORE TRUNCATE ON party_run_host_reports
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 5. 레이드 일일·주간 보상 ----------

CREATE TABLE raid_claims (
  character_id   BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id     TEXT NOT NULL,
  period_kind    TEXT NOT NULL CHECK (period_kind IN ('daily', 'weekly')),
  period_start   TIMESTAMPTZ NOT NULL,
  dungeon_run_id BIGINT NOT NULL REFERENCES dungeon_runs(id),
  claimed_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, dungeon_id, period_start)
);
COMMENT ON TABLE  raid_claims IS '레이드 보상 수령 기록. PK가 "기간당 한 번"을 DB 수준에서 보장한다. 중간 레이드는 일일(개방 요일마다), 최종 레이드는 주간(목요일 06:00 KST)';
COMMENT ON COLUMN raid_claims.period_kind IS 'daily: raidTier Mid / weekly: raidTier Final (dungeons.json)';
COMMENT ON COLUMN raid_claims.period_start IS 'resetBoundaries의 dailyStartAt 또는 weeklyStartAt(서버 KST 함수 하나)';
COMMENT ON COLUMN raid_claims.dungeon_run_id IS '보상을 받은 판(dungeon_runs). 이번 주 n/3 표시는 같은 주간 구간의 행 수';

-- ============ DOWN ============
-- 개발 DB 전용. 새 사유의 원장 행을 지우므로 잔액과 원장이 어긋난다. 운영에서는 실행하지 않는다.

DROP TABLE IF EXISTS raid_claims;

DELETE FROM kill_log WHERE run_id IN (SELECT id FROM dungeon_runs WHERE party_run_id IS NOT NULL OR counts_entry = false OR state = 'reported');
DROP TABLE IF EXISTS party_run_host_reports;
DROP TABLE IF EXISTS party_run_members;

DROP INDEX IF EXISTS dungeon_runs_party_idx;
DROP INDEX IF EXISTS dungeon_runs_char_day;
DELETE FROM dungeon_runs WHERE party_run_id IS NOT NULL OR counts_entry = false OR state = 'reported';
CREATE INDEX dungeon_runs_char_day ON dungeon_runs (character_id, reset_day);
DELETE FROM dungeon_runs WHERE state = 'cleared' AND cards IS NULL;
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_cleared_chk;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_cleared_chk
  CHECK (state <> 'cleared' OR (rank IS NOT NULL AND cards IS NOT NULL AND ended_at IS NOT NULL));
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_reported_chk;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS reported_at;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS reported_outcome;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS power_cap;
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_lock_chk;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS lock_reason;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS reward_locked;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS counts_entry;
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_party_chk;
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_size_chk;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS ai_count;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS humans;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS slot;
ALTER TABLE dungeon_runs DROP COLUMN IF EXISTS party_run_id;
ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_state_check;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_state_check
  CHECK (state IN ('playing', 'cleared', 'failed', 'abandoned', 'held'));

DROP TABLE IF EXISTS party_runs;
DROP TABLE IF EXISTS party_applications;
DROP TABLE IF EXISTS party_members;
DROP TABLE IF EXISTS parties;

DELETE FROM anomaly_log WHERE kind IN ('party_result', 'party_host', 'raid_enter');
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result'));

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason IN ('raid_key', 'raid_key_cost');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card'));

DROP INDEX IF EXISTS auth_identities_account_provider_uq;
