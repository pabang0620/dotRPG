-- 0008_ops: 7단계(운영) 테이블 - 관리자 계정·감사·운영 지급·점검 창·정리 작업 기록
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0007과 같다. 선행: 0007_auction.   (초안: 구현 전에 Docs/server/phase7_ops.md 12절 결정 대기를 확인한다)
-- 설계: Docs/server/phase7_ops.md
--
-- 이 마이그레이션이 하는 일
--   1. 관리자 계정(admin_users), 세션(admin_sessions): 게임 계정(accounts)과 완전히 분리한다.
--   2. 감사 로그(admin_audit_log), 계정 메모(admin_account_notes), 운영 지급 기록(admin_grants), 보류 던전 검토 기록(held_run_reviews): 추가만 한다(트리거가 UPDATE/DELETE 차단).
--   3. 점검 창(maintenance_windows), 정리·점검 작업 실행 기록(job_runs).
--   4. mails.system_code + item_ledger reason 'admin_grant': 운영 지급은 system 우편으로만 나간다(캐릭터 잔액을 직접 고치는 경로를 만들지 않는다).
--   5. 정리 작업·관리자 조회를 위한 인덱스(원장 created_at BRIN, drops.kill_id, anomaly_log 계정별 등), 변동이 잦은 표의 autovacuum 설정.
--
-- 만들지 않는 것
--   - 캐릭터 골드·아이템을 직접 깎거나 더하는 관리자 API. 지급은 system 우편(수령 때 mail_claim 원장)이고 회수 기능은 만들지 않는다(정지 후 개별 대응).
--   - 원장·체결·소각·경매 악용 기록의 삭제. 이 표들은 append-only 트리거 + FK 연결 때문에 7단계에서도 지우지 않는다(phase7_ops.md 6절).
--
-- 출시 후에 쓰는 마이그레이션 규칙(phase7_ops.md 3.5절): 원장 reason CHECK를 바꿀 때는 NOT VALID로 추가하고 VALIDATE는 다음 마이그레이션에서 한다.
--   이 파일은 출시 전이라 0007까지와 같이 DROP/ADD로 쓴다. 러너가 파일마다 한 트랜잭션이라 CREATE INDEX CONCURRENTLY는 쓸 수 없다.

-- ============ UP ============

-- ---------- 1. 관리자 계정·세션 ----------

CREATE TABLE admin_users (
  id                  BIGSERIAL PRIMARY KEY,
  uuid                UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  login_id            TEXT NOT NULL UNIQUE,
  display_name        TEXT NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 30),
  role                TEXT NOT NULL CHECK (role IN ('viewer', 'operator', 'owner')),
  password_hash       TEXT NOT NULL,
  password_changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  must_change_password BOOLEAN NOT NULL DEFAULT true,
  totp_secret_enc     BYTEA,
  totp_confirmed_at   TIMESTAMPTZ,
  totp_last_step      BIGINT,
  failed_count        SMALLINT NOT NULL DEFAULT 0 CHECK (failed_count >= 0),
  locked_until        TIMESTAMPTZ,
  last_login_at       TIMESTAMPTZ,
  created_by          BIGINT REFERENCES admin_users(id),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  disabled_at         TIMESTAMPTZ,
  CONSTRAINT admin_users_login_chk CHECK (login_id ~ '^[a-z0-9_.-]{3,32}$'),
  CONSTRAINT admin_users_totp_chk  CHECK (totp_confirmed_at IS NULL OR totp_secret_enc IS NOT NULL)
);
COMMENT ON TABLE  admin_users IS '관리자 계정. 게임 계정(accounts)과 분리되어 서로의 토큰·비밀번호가 통하지 않는다. 행은 지우지 않고 disabled_at으로 비활성화한다(감사 로그가 참조)';
COMMENT ON COLUMN admin_users.uuid IS '외부 노출 id(감사 로그 조회, 관리자 목록)';
COMMENT ON COLUMN admin_users.login_id IS '소문자 아이디(3~32자 영문 소문자·숫자·_.-)';
COMMENT ON COLUMN admin_users.role IS 'viewer 조회만 / operator 신고·보류·제재·점검 처리 / owner 관리자 관리·운영 지급·영구 정지·작업 수동 실행';
COMMENT ON COLUMN admin_users.password_hash IS 'argon2id 문자열(게임 dev 로그인과 같은 라이브러리)';
COMMENT ON COLUMN admin_users.must_change_password IS 'true면 첫 로그인(임시 비밀번호)이므로 비밀번호 변경 전에는 setup 범위 세션만 받는다';
COMMENT ON COLUMN admin_users.totp_secret_enc IS 'TOTP 비밀키. ADMIN_SECRET_KEY(환경변수, 32바이트)로 AES-256-GCM 암호화한 값(nonce || 암호문 || 태그). 원문은 저장하지 않는다';
COMMENT ON COLUMN admin_users.totp_confirmed_at IS '첫 코드 확인으로 등록이 끝난 시각. NULL이면 2FA 미등록이라 setup 범위 세션만 받는다';
COMMENT ON COLUMN admin_users.totp_last_step IS '마지막으로 받아들인 TOTP 30초 구간 번호. 같은 코드 재사용(재전송)을 막는다';
COMMENT ON COLUMN admin_users.failed_count IS '연속 로그인 실패 횟수. 성공하면 0';
COMMENT ON COLUMN admin_users.locked_until IS '연속 실패로 잠긴 시각까지. 지나면 다시 시도할 수 있다';

CREATE TABLE admin_sessions (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  admin_id     BIGINT NOT NULL REFERENCES admin_users(id),
  token_hash   TEXT NOT NULL UNIQUE,
  scope        TEXT NOT NULL DEFAULT 'full' CHECK (scope IN ('full', 'setup')),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at   TIMESTAMPTZ NOT NULL,
  revoked_at   TIMESTAMPTZ,
  client_note  TEXT CHECK (char_length(client_note) <= 100),
  CONSTRAINT admin_sessions_range_chk CHECK (expires_at > created_at)
);
COMMENT ON TABLE  admin_sessions IS '관리자 세션. 불투명 토큰(32바이트 난수)의 SHA-256만 저장한다. 유휴 ADMIN_SESSION_IDLE_MINUTES, 절대 수명 ADMIN_SESSION_MAX_HOURS';
COMMENT ON COLUMN admin_sessions.scope IS 'full 전체 기능 / setup 비밀번호 변경·2FA 등록만 가능(첫 로그인)';
COMMENT ON COLUMN admin_sessions.client_note IS 'CLI 이름·버전 같은 표시용 문자열';
-- 매 요청 토큰 조회는 token_hash UNIQUE가 맡는다. 관리자별 살아 있는 세션(비활성화 때 일괄 폐기)
CREATE INDEX admin_sessions_admin ON admin_sessions (admin_id, created_at DESC) WHERE revoked_at IS NULL;
-- 만료 세션 정리 작업
CREATE INDEX admin_sessions_expires ON admin_sessions (expires_at);

-- ---------- 2. 감사 로그·메모·운영 지급·보류 검토 ----------

CREATE TABLE admin_audit_log (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  admin_id       BIGINT REFERENCES admin_users(id),
  login_id_tried TEXT CHECK (char_length(login_id_tried) <= 64),
  action         TEXT NOT NULL,
  target_type    TEXT CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server')),
  target_uuid    UUID,
  request_id     UUID,
  result         TEXT NOT NULL CHECK (result IN ('ok', 'denied', 'invalid', 'error')),
  error_code     TEXT,
  params         JSONB NOT NULL DEFAULT '{}'::jsonb,
  response       JSONB,
  ip             TEXT,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT admin_audit_action_chk CHECK (action ~ '^[a-z_]+(\.[a-z_]+)+$'),
  CONSTRAINT admin_audit_actor_chk  CHECK (admin_id IS NOT NULL OR login_id_tried IS NOT NULL OR action LIKE 'bootstrap.%')
);
COMMENT ON TABLE  admin_audit_log IS '관리자 행동 기록(추가만, 트리거가 UPDATE/DELETE 차단, 지우지 않는다). 변경 작업은 작업과 같은 트랜잭션에서 result=ok로 쓰고, 거절·실패·로그인 실패는 롤백되지 않도록 별도 트랜잭션으로 쓴다. 민감 조회(신고 증거·계정 상세)도 남긴다';
COMMENT ON COLUMN admin_audit_log.admin_id IS '행동한 관리자. 아이디를 모르는 로그인 실패와 bootstrap 명령은 NULL';
COMMENT ON COLUMN admin_audit_log.login_id_tried IS '로그인 실패 때 입력된 아이디(없는 아이디 공격 추적). 비밀번호·코드는 어디에도 저장하지 않는다';
COMMENT ON COLUMN admin_audit_log.action IS '점으로 구분한 소문자 이름. 예: auth.login, sanction.create, report.resolve, held_run.release, maintenance.schedule, grant.create, account.view, bootstrap.create_owner';
COMMENT ON COLUMN admin_audit_log.target_uuid IS '대상의 외부 id(계정·캐릭터·신고·던전 판·제재·점검 창·관리자). 내부 키는 넣지 않는다';
COMMENT ON COLUMN admin_audit_log.request_id IS '변경 요청의 request_id. (admin_id, request_id)가 ok 행에서 유일해 멱등성 기록을 겸한다';
COMMENT ON COLUMN admin_audit_log.params IS '행동의 허용 목록 필드만 정리한 요청 값(사유 코드, 기간 프리셋, 메모 길이 등). 비밀번호·코드·토큰·채팅 원문은 넣지 않는다';
COMMENT ON COLUMN admin_audit_log.response IS 'ok 변경 작업의 응답 data(멱등 재전송 때 그대로 돌려준다). 조회·실패 행은 NULL';
COMMENT ON COLUMN admin_audit_log.ip IS '접속 출처(관리자 listener는 loopback이라 보통 127.0.0.1. 프록시를 거치면 그 값)';
-- 같은 관리자의 같은 request_id는 성공 행이 하나(동시 재전송을 DB가 거절)
CREATE UNIQUE INDEX admin_audit_request_uq ON admin_audit_log (admin_id, request_id) WHERE request_id IS NOT NULL AND result = 'ok';
-- 최근 기록 보기(최신순), 한 관리자의 행동, 한 대상(계정·신고 등)을 누가 건드렸나
CREATE INDEX admin_audit_time   ON admin_audit_log (created_at DESC);
CREATE INDEX admin_audit_admin  ON admin_audit_log (admin_id, created_at DESC) WHERE admin_id IS NOT NULL;
CREATE INDEX admin_audit_target ON admin_audit_log (target_type, target_uuid, created_at DESC) WHERE target_uuid IS NOT NULL;
CREATE TRIGGER admin_audit_log_append_only BEFORE UPDATE OR DELETE ON admin_audit_log
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER admin_audit_log_no_truncate BEFORE TRUNCATE ON admin_audit_log
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

CREATE TABLE admin_account_notes (
  id         BIGSERIAL PRIMARY KEY,
  uuid       UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  admin_id   BIGINT NOT NULL REFERENCES admin_users(id),
  kind       TEXT NOT NULL DEFAULT 'note' CHECK (kind IN ('note', 'review_ack')),
  note       TEXT NOT NULL CHECK (char_length(note) BETWEEN 1 AND 500),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  admin_account_notes IS '계정에 대한 운영 메모(추가만). 수정이 필요하면 새 메모를 쓴다';
COMMENT ON COLUMN admin_account_notes.kind IS 'note 일반 메모 / review_ack "이상 기록을 검토했고 조치 불필요". 감시 목록은 계정의 마지막 review_ack 이후 기록만 점수에 넣는다';
-- 계정 상세의 최근 메모, 감시 목록의 마지막 review_ack 조회
CREATE INDEX admin_account_notes_account ON admin_account_notes (account_id, created_at DESC);
CREATE TRIGGER admin_account_notes_append_only BEFORE UPDATE OR DELETE ON admin_account_notes
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER admin_account_notes_no_truncate BEFORE TRUNCATE ON admin_account_notes
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 운영 지급: system 우편 한 통 + 이 기록 한 줄. 골드는 우편이 들고 있다가 수령할 때 gold_ledger(mail_claim)로 캐릭터에 들어간다
ALTER TABLE mails ADD COLUMN system_code TEXT CHECK (system_code IN ('compensation', 'event', 'refund', 'notice'));
ALTER TABLE mails ADD CONSTRAINT mails_system_chk CHECK ((kind = 'system') = (system_code IS NOT NULL));
COMMENT ON COLUMN mails.system_code IS 'kind=system일 때만 값이 있다. compensation 보상 / event 이벤트 / refund 환불 / notice 안내. 문구는 클라이언트가 이 코드로 조립한다(서버는 문장을 만들지 않는다)';

CREATE TABLE admin_grants (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  admin_id     BIGINT NOT NULL REFERENCES admin_users(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  mail_id      BIGINT NOT NULL UNIQUE REFERENCES mails(id),
  request_id   UUID NOT NULL,
  system_code  TEXT NOT NULL CHECK (system_code IN ('compensation', 'event', 'refund', 'notice')),
  gold         BIGINT NOT NULL DEFAULT 0 CHECK (gold >= 0),
  item_key     TEXT CHECK (item_key <> 'gold'),
  count        INT CHECK (count > 0),
  memo         TEXT NOT NULL CHECK (char_length(memo) BETWEEN 1 AND 200),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (admin_id, request_id),
  CONSTRAINT admin_grants_item_chk    CHECK ((item_key IS NULL) = (count IS NULL)),
  CONSTRAINT admin_grants_content_chk CHECK (gold > 0 OR item_key IS NOT NULL)
);
COMMENT ON TABLE  admin_grants IS '운영 지급 기록(추가만). 우편(mails.kind=system) 한 통과 1:1. 한도(1회·관리자별 일일)는 서버 환경변수 ADMIN_GRANT_*가 검사한다. 골드 보존식의 SUM(mails.gold WHERE kind=system)이 이 표와 같아야 한다';
COMMENT ON COLUMN admin_grants.memo IS '지급 사유(고객 문의 번호, 사고 설명 등). 필수';
-- 한 캐릭터가 받은 지급 이력(계정 상세), 관리자별 일일 지급 합계(한도 검사: admin_id + created_at 범위)
CREATE INDEX admin_grants_character ON admin_grants (character_id, created_at DESC);
CREATE INDEX admin_grants_admin_time ON admin_grants (admin_id, created_at);
CREATE TRIGGER admin_grants_append_only BEFORE UPDATE OR DELETE ON admin_grants
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER admin_grants_no_truncate BEFORE TRUNCATE ON admin_grants
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 운영 지급 아이템은 item_ledger에 mail 위치 +n으로 남는다(수령하면 mail_claim으로 mail -n, bag +n)
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant'));
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). 경매: auction_list 등록(가방 -n, auction +n) / auction_return 취소·만료(auction -n, 판매자 mail +n) / auction_sold 판매 체결(판매자 auction -n) / auction_buy 구매(구매자 mail +n) / mail_claim 우편 수령(mail -n, bag +n) / mail_expire 30일 경과 폐기(mail -n) / admin_grant 운영 지급(system 우편 첨부, mail +n)';
-- 같은 지급 우편의 원장은 한 번만
CREATE UNIQUE INDEX item_ledger_admin_grant_uq ON item_ledger (ref, location) WHERE reason = 'admin_grant';

CREATE TABLE held_run_reviews (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  dungeon_run_id BIGINT NOT NULL UNIQUE REFERENCES dungeon_runs(id),
  admin_id       BIGINT NOT NULL REFERENCES admin_users(id),
  decision       TEXT NOT NULL CHECK (decision IN ('released', 'rejected')),
  note           TEXT NOT NULL CHECK (char_length(note) BETWEEN 1 AND 500),
  prev_ended_at  TIMESTAMPTZ NOT NULL,
  request_id     UUID NOT NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (admin_id, request_id)
);
COMMENT ON TABLE  held_run_reviews IS '보류(held)된 던전 판의 검토 결과(추가만). 한 판에 한 번만 결정한다(UNIQUE). 검토 대기 목록 = state=held이고 이 표에 행이 없는 판. released는 판 상태를 cleared로 바꾸고(finalizeCleared) rejected는 held로 남긴다';
COMMENT ON COLUMN held_run_reviews.prev_ended_at IS '해제 전 dungeon_runs.ended_at(보류 시각). 해제하면 ended_at을 해제 시각으로 옮겨 카드 선택 시간(DUNGEON_CARD_TTL_HOURS)이 해제부터 다시 시작한다';
CREATE TRIGGER held_run_reviews_append_only BEFORE UPDATE OR DELETE ON held_run_reviews
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER held_run_reviews_no_truncate BEFORE TRUNCATE ON held_run_reviews
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();
-- 검토 대기 목록(오래된 순). 보류 판은 드물어 부분 인덱스가 아주 작다
CREATE INDEX dungeon_runs_held ON dungeon_runs (ended_at) WHERE state = 'held';

-- ---------- 3. 점검 창, 작업 실행 기록 ----------

CREATE TABLE maintenance_windows (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  state          TEXT NOT NULL DEFAULT 'scheduled' CHECK (state IN ('scheduled', 'cancelled', 'ended')),
  notice         TEXT NOT NULL DEFAULT '' CHECK (char_length(notice) <= 100),
  block_login_at TIMESTAMPTZ NOT NULL,
  starts_at      TIMESTAMPTZ NOT NULL,
  ends_at        TIMESTAMPTZ NOT NULL,
  created_by     BIGINT NOT NULL REFERENCES admin_users(id),
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  closed_at      TIMESTAMPTZ,
  closed_by      BIGINT REFERENCES admin_users(id),
  CONSTRAINT maintenance_range_chk  CHECK (block_login_at <= starts_at AND starts_at < ends_at),
  CONSTRAINT maintenance_closed_chk CHECK ((state = 'scheduled') = (closed_at IS NULL))
);
COMMENT ON TABLE  maintenance_windows IS '점검 창. 단계(공지 -> 새 로그인·새 판 차단 -> 점검 중)는 이 행과 서버 시각으로 계산한다(상태를 시각 경계마다 쓰지 않는다). 서버는 MAINT_POLL_SECONDS마다 읽어 메모리에 둔다';
COMMENT ON COLUMN maintenance_windows.state IS 'scheduled 예약됨(시각이 지나 점검 중이어도 종료 전까지 scheduled) / cancelled 시작 전 취소 / ended 종료(운영자가 끝냈거나 ends_at이 지나 작업이 닫음)';
COMMENT ON COLUMN maintenance_windows.notice IS '운영자가 덧붙이는 안내 문구(100자 이하). 서버가 공지 문장 틀에 끼워 chat.sys와 /meta로 내보낸다';
COMMENT ON COLUMN maintenance_windows.block_login_at IS '새 로그인·새 던전 판(입장·파티 출발·판 시작·매칭)을 막기 시작하는 시각 = starts_at - MAINT_PRE_BLOCK_MINUTES(진행 중인 판이 끝날 시간을 준다)';
COMMENT ON COLUMN maintenance_windows.starts_at IS '점검 시작: 접속 중인 모두에게 bye(MAINTENANCE)를 보내고 /health·/meta·/auth/logout 외 요청은 503 MAINTENANCE';
COMMENT ON COLUMN maintenance_windows.ends_at IS '예정 종료. 이 시각이 지나면 점검이 자동으로 풀린다(연장하려면 extend). 운영자가 더 일찍 end 할 수 있다';
COMMENT ON COLUMN maintenance_windows.closed_by IS '운영자가 닫았으면 그 관리자, ends_at 경과로 자동 종료면 NULL';
-- 열려 있는(scheduled) 점검 창은 한 번에 하나
CREATE UNIQUE INDEX maintenance_one_open ON maintenance_windows ((true)) WHERE state = 'scheduled';
CREATE INDEX maintenance_time ON maintenance_windows (starts_at DESC);

CREATE TABLE job_runs (
  id            BIGSERIAL PRIMARY KEY,
  job           TEXT NOT NULL CHECK (job ~ '^[a-z0-9-]+$'),
  started_by    TEXT NOT NULL DEFAULT 'schedule' CHECK (started_by IN ('schedule', 'manual', 'startup')),
  triggered_by  BIGINT REFERENCES admin_users(id),
  status        TEXT NOT NULL DEFAULT 'running' CHECK (status IN ('running', 'ok', 'failed')),
  started_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  finished_at   TIMESTAMPTZ,
  rows_affected INT NOT NULL DEFAULT 0 CHECK (rows_affected >= 0),
  detail        JSONB NOT NULL DEFAULT '{}'::jsonb,
  error         TEXT CHECK (char_length(error) <= 1000),
  CONSTRAINT job_runs_finished_chk CHECK ((status = 'running') = (finished_at IS NULL))
);
COMMENT ON TABLE  job_runs IS '정리·점검 작업(JobRunner)의 실행 기록. 작업 하나는 pg_try_advisory_lock으로 한 곳에서만 돌고, 못 잡으면 기록 없이 건너뛴다. "마지막 성공이 오래됐다" 알림과 정합성 점검 결과의 보관소. 서버가 죽어 running으로 남은 행은 다음 기동에서 failed(interrupted)로 닫는다. 90일 보관';
COMMENT ON COLUMN job_runs.job IS '작업 이름(소문자·숫자·하이픈). 예: purge-hourly, purge-daily, stale-runs, integrity-nightly, maintenance-close';
COMMENT ON COLUMN job_runs.rows_affected IS '지운·바꾼 행 수 합계';
COMMENT ON COLUMN job_runs.detail IS '표별 행 수, 배치 수, 걸린 시간(ms), 정합성 점검이면 항목별 불일치 건수와 표본(최대 20건)';
-- 작업별 마지막 실행·마지막 성공 조회, 보관 기간 정리
CREATE INDEX job_runs_job_time ON job_runs (job, started_at DESC);
CREATE INDEX job_runs_started ON job_runs (started_at);

-- ---------- 4. 정리·관리자 조회용 인덱스, 문서 보정 ----------

-- 원장 일일 집계·"오늘 변동이 있던 캐릭터" 찾기(정합성 점검): 시간순으로 쌓이는 추가 전용 표라 BRIN(수 KB)이면 충분하다
CREATE INDEX gold_ledger_created_brin ON gold_ledger USING brin (created_at);
CREATE INDEX item_ledger_created_brin ON item_ledger USING brin (created_at);
CREATE INDEX xp_ledger_created_brin   ON xp_ledger   USING brin (created_at);

-- kill_log 정리의 ON DELETE CASCADE가 처치 한 건마다 drops를 찾는다. 인덱스가 없으면 행마다 전체 스캔이다
CREATE INDEX drops_kill_idx ON drops (kill_id);
-- 우편 정리(수령된 행)와 소각 기록 FK 확인
CREATE INDEX mails_claimed_idx ON mails (claimed_at) WHERE claimed_at IS NOT NULL;
CREATE INDEX auction_sinks_mail_idx ON auction_sinks (mail_id) WHERE mail_id IS NOT NULL;

-- 계정별 이상 기록(계정 상세, 감시 목록), 심각도 2 이상 최신순(검토 대기)
CREATE INDEX anomaly_log_account_time ON anomaly_log (account_id, created_at DESC);
CREATE INDEX anomaly_log_severe ON anomaly_log (created_at DESC) WHERE severity >= 2;

-- 쓰기·삭제가 잦은 표: 기본값(20%)이면 정리 뒤 죽은 행이 오래 남는다
ALTER TABLE chat_messages SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE kill_log      SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE drops         SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE request_log   SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE anomaly_log   SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.05);

COMMENT ON COLUMN accounts.banned_until IS '이 시각 전까지 로그인·refresh 거부. NULL = 정지 아님. 영구 정지는 9999-12-31T00:00:00Z를 쓴다(PostgreSQL infinity는 node-pg가 Date가 아닌 값으로 돌려줘 인증 미들웨어가 깨진다)';

-- ============ DOWN ============
-- 개발 DB 전용. 관리자 기록·운영 지급 우편이 사라진다. 운영에서는 실행하지 않는다(운영 롤백은 phase7_ops.md 3.6절: 이전 이미지 또는 배포 전 덤프 복구).

COMMENT ON COLUMN accounts.banned_until IS '이 시각 전까지 로그인·refresh 거부. NULL = 정지 아님';

ALTER TABLE anomaly_log   RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);
ALTER TABLE request_log   RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);
ALTER TABLE drops         RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);
ALTER TABLE kill_log      RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);
ALTER TABLE chat_messages RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);

DROP INDEX IF EXISTS anomaly_log_severe;
DROP INDEX IF EXISTS anomaly_log_account_time;
DROP INDEX IF EXISTS auction_sinks_mail_idx;
DROP INDEX IF EXISTS mails_claimed_idx;
DROP INDEX IF EXISTS drops_kill_idx;
DROP INDEX IF EXISTS xp_ledger_created_brin;
DROP INDEX IF EXISTS item_ledger_created_brin;
DROP INDEX IF EXISTS gold_ledger_created_brin;

DROP TABLE IF EXISTS job_runs;
DROP TABLE IF EXISTS maintenance_windows;

DROP INDEX IF EXISTS dungeon_runs_held;
DROP TABLE IF EXISTS held_run_reviews;

DROP INDEX IF EXISTS item_ledger_admin_grant_uq;
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason = 'admin_grant';
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire'));
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). 경매: auction_list 등록(가방 -n, auction +n) / auction_return 취소·만료(auction -n, 판매자 mail +n) / auction_sold 판매 체결(판매자 auction -n) / auction_buy 구매(구매자 mail +n) / mail_claim 우편 수령(mail -n, bag +n) / mail_expire 30일 경과 폐기(mail -n)';

DROP TABLE IF EXISTS admin_grants;
DELETE FROM mails WHERE kind = 'system';
ALTER TABLE mails DROP CONSTRAINT IF EXISTS mails_system_chk;
ALTER TABLE mails DROP COLUMN IF EXISTS system_code;

DROP TABLE IF EXISTS admin_account_notes;
DROP TABLE IF EXISTS admin_audit_log;
DROP TABLE IF EXISTS admin_sessions;
DROP TABLE IF EXISTS admin_users;
