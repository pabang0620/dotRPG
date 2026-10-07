-- 0020_anti_abuse: 부정 행위 방지 1단계 (Docs/server/phase9_anti_abuse.md)
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0019와 같다. 선행: 0019_gear_renewal.
--
-- 이 마이그레이션이 하는 일
--   1. 계정 세션(accounts 4열), 로그인 기록(login_events), 기기·IP 집계(account_devices, account_ips),
--      refresh_tokens 폐기 사유·기기 바인딩, auth_identities.steam_owner_id.
--   2. 프레즌스(online_sessions)와 활동 시간(play_time_hourly).
--   3. 경제 속도 감시: income_hourly(원장 파생 집계), economy_holds(정지 기록).
--   4. 파티 판 사람 수 스냅샷(party_run_members 2열), 기여 판정(dungeon_runs.contribution, lock_reason에 LOW_CONTRIBUTION).
--   5. 전직·각성 서버 기록(character_career, character_career_trials)과 기존 저장 값의 백필.
--   6. 경매 의심 거래(auction_trade_flags, 추가 전용).
--   7. 원장 사유(admin_clawback), 이상 기록 종류, 관리자 감사 대상(hold).
-- 설계 문서 13절 초안에서 달라진 점: admin_audit_log.target_type에 'hold' 추가, auth_identities.steam_owner_id 인덱스 추가,
-- 백필 조건에 jsonb_typeof 확인, 막는 상태에 clawed_back 포함(회수 뒤에도 해제 전까지 막는다, 부분 인덱스 조건), DOWN 본문 작성.

-- ============ UP ============

-- ---------- 1. 계정 세션, 로그인 기록 ----------

ALTER TABLE accounts
  ADD COLUMN active_family_id    UUID,
  ADD COLUMN active_install_id   UUID,
  ADD COLUMN active_device_hash  TEXT CHECK (active_device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN active_session_at   TIMESTAMPTZ;
COMMENT ON COLUMN accounts.active_family_id IS '현재 유효한 세션(리프레시 가족). 액세스 토큰 sid가 이것과 다르면 SESSION_REPLACED. 같은 계정 재로그인이 갱신';
COMMENT ON COLUMN accounts.active_device_hash IS '현재 세션의 기기(클라이언트 지문의 HMAC-SHA256 hex). 프레즌스의 기기 한도·레이드 사람 수 스냅샷의 원본. 기기를 모르면 NULL';

ALTER TABLE auth_identities
  ADD COLUMN steam_owner_id TEXT CHECK (steam_owner_id ~ '^[0-9]{17}$');
COMMENT ON COLUMN auth_identities.steam_owner_id IS 'Steam 패밀리 공유일 때 앱 소유자의 SteamID(AuthenticateUserTicket ownersteamid). 사람 키 = COALESCE(steam_owner_id, subject)';
CREATE INDEX auth_identities_steam_owner ON auth_identities (steam_owner_id) WHERE steam_owner_id IS NOT NULL;

ALTER TABLE refresh_tokens
  ADD COLUMN install_id    UUID,
  ADD COLUMN device_hash   TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN revoke_reason TEXT CHECK (revoke_reason IN ('logout', 'reuse', 'replaced', 'device_mismatch', 'admin', 'legacy')),
  ADD CONSTRAINT refresh_tokens_revoke_chk CHECK (revoke_reason IS NULL OR revoked_at IS NOT NULL);
UPDATE refresh_tokens SET revoke_reason = 'legacy' WHERE revoked_at IS NOT NULL;
COMMENT ON COLUMN refresh_tokens.device_hash IS '이 가족을 만든 로그인의 기기. 리프레시 기기 불일치 감지용';
COMMENT ON COLUMN refresh_tokens.revoke_reason IS 'replaced는 같은 계정 재로그인으로 정상 교체(REFRESH_REUSED 경보를 내지 않는다)';

CREATE TABLE login_events (
  id             BIGSERIAL PRIMARY KEY,
  account_id     BIGINT NOT NULL REFERENCES accounts(id),
  character_id   BIGINT REFERENCES characters(id),
  kind           TEXT NOT NULL CHECK (kind IN ('register', 'login', 'steam_login', 'refresh', 'enter')),
  install_id     UUID,
  device_hash    TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ip             INET,
  steam_id       TEXT CHECK (steam_id ~ '^[0-9]{17}$'),
  steam_owner_id TEXT CHECK (steam_owner_id ~ '^[0-9]{17}$'),
  client_version TEXT,
  flags          TEXT[] NOT NULL DEFAULT '{}',
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  login_events IS '로그인·세션 진입 기록(추가 위주, 90일 보관). 다계정 추적과 사후 조사의 원본. 외부 노출 id 없음(관리자 응답은 필드만)';
COMMENT ON COLUMN login_events.character_id IS 'kind=enter(프레즌스 첫 신호)일 때만';
COMMENT ON COLUMN login_events.flags IS 'device_missing / device_mismatch / replaced_other';
COMMENT ON COLUMN login_events.ip IS '개인정보 성격. 보관 기간 후 삭제';
CREATE INDEX login_events_account_time ON login_events (account_id, created_at DESC);
CREATE INDEX login_events_created ON login_events (created_at);
CREATE INDEX login_events_device ON login_events (device_hash, created_at DESC) WHERE device_hash IS NOT NULL;
CREATE INDEX login_events_ip ON login_events (ip, created_at DESC) WHERE ip IS NOT NULL;

CREATE TABLE account_devices (
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  device_hash   TEXT NOT NULL CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  seen_count    INT NOT NULL DEFAULT 1 CHECK (seen_count >= 1),
  PRIMARY KEY (account_id, device_hash)
);
COMMENT ON TABLE account_devices IS '계정이 쓴 기기 집계. "같은 기기를 쓴 계정" 조회(레이드·경매 플래그·정지 전파)의 원본. 마지막 관측 180일 뒤 삭제';
CREATE INDEX account_devices_device ON account_devices (device_hash, last_seen_at DESC);
CREATE INDEX account_devices_seen ON account_devices (last_seen_at);

CREATE TABLE account_ips (
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  ip            INET NOT NULL,
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  seen_count    INT NOT NULL DEFAULT 1 CHECK (seen_count >= 1),
  PRIMARY KEY (account_id, ip)
);
COMMENT ON TABLE account_ips IS '계정이 쓴 IP 집계. 거절 근거가 아니라 감시 점수와 경매 SAME_IP 플래그용';
CREATE INDEX account_ips_ip ON account_ips (ip, last_seen_at DESC);
CREATE INDEX account_ips_seen ON account_ips (last_seen_at);

-- ---------- 2. 프레즌스, 플레이 시간 ----------

CREATE TABLE online_sessions (
  account_id       BIGINT PRIMARY KEY REFERENCES accounts(id),
  character_id     BIGINT NOT NULL REFERENCES characters(id),
  family_id        UUID NOT NULL,
  install_id       UUID,
  device_hash      TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ip               INET,
  map_id           TEXT NOT NULL,
  prev_map_id      TEXT,
  map_since        TIMESTAMPTZ NOT NULL,
  map_changed_at   TIMESTAMPTZ NOT NULL,
  auto_play        BOOLEAN NOT NULL DEFAULT false,
  input_recent     BOOLEAN NOT NULL DEFAULT true,
  unattended_since TIMESTAMPTZ,
  started_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at     TIMESTAMPTZ NOT NULL,
  ended_at         TIMESTAMPTZ,
  end_reason       TEXT CHECK (end_reason IN ('leave', 'replaced', 'timeout', 'banned')),
  CONSTRAINT online_sessions_end_chk CHECK ((ended_at IS NULL) = (end_reason IS NULL))
);
COMMENT ON TABLE  online_sessions IS '계정당 한 행: 지금(또는 마지막) 온라인 캐릭터. 온라인 = ended_at IS NULL AND last_seen_at > now() - PRESENCE_ONLINE_SECONDS(90). 기기 동시 접속·처치 맵 확인·보스 체류의 근거';
COMMENT ON COLUMN online_sessions.map_since IS '현재 맵에 끊김 없이 있기 시작한 시각(맵 이동 또는 신호 공백 때 갱신). 필드 보스 체류';
COMMENT ON COLUMN online_sessions.prev_map_id IS '직전 맵. 맵 이동 직후 PRESENCE_MAP_GRACE_SECONDS 동안 옛 맵 처치 보고를 받아 준다';
CREATE INDEX online_sessions_device ON online_sessions (device_hash, last_seen_at) WHERE ended_at IS NULL AND device_hash IS NOT NULL;
CREATE INDEX online_sessions_ip ON online_sessions (ip, last_seen_at) WHERE ended_at IS NULL AND ip IS NOT NULL;
CREATE INDEX online_sessions_char ON online_sessions (character_id);

CREATE TABLE play_time_hourly (
  character_id       BIGINT NOT NULL REFERENCES characters(id),
  hour_start         TIMESTAMPTZ NOT NULL,
  active_seconds     SMALLINT NOT NULL DEFAULT 0 CHECK (active_seconds BETWEEN 0 AND 3600),
  auto_seconds       SMALLINT NOT NULL DEFAULT 0 CHECK (auto_seconds BETWEEN 0 AND 3600),
  unattended_seconds SMALLINT NOT NULL DEFAULT 0 CHECK (unattended_seconds BETWEEN 0 AND 3600),
  beats              SMALLINT NOT NULL DEFAULT 0 CHECK (beats >= 0),
  PRIMARY KEY (character_id, hour_start)
);
COMMENT ON TABLE  play_time_hourly IS '프레즌스가 쌓은 활동 시간(서버 시계, 신호 간격 60초 이하만). 경제 속도 정지의 분모. 35일 보관';
COMMENT ON COLUMN play_time_hourly.hour_start IS 'date_trunc(hour, 신호를 받은 시각) UTC';
CREATE INDEX play_time_hourly_hour ON play_time_hourly (hour_start);

-- ---------- 3. 경제 속도 감시 ----------

CREATE TABLE income_hourly (
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  hour_start    TIMESTAMPTZ NOT NULL,
  level_max     SMALLINT NOT NULL CHECK (level_max >= 1),
  xp            BIGINT NOT NULL DEFAULT 0 CHECK (xp >= 0),
  gold_acq      BIGINT NOT NULL DEFAULT 0 CHECK (gold_acq >= 0),
  item_value    BIGINT NOT NULL DEFAULT 0 CHECK (item_value >= 0),
  ore           INT NOT NULL DEFAULT 0 CHECK (ore >= 0),
  essence       INT NOT NULL DEFAULT 0 CHECK (essence >= 0),
  core          INT NOT NULL DEFAULT 0 CHECK (core >= 0),
  epic_plus     INT NOT NULL DEFAULT 0 CHECK (epic_plus >= 0),
  unique_plus   INT NOT NULL DEFAULT 0 CHECK (unique_plus >= 0),
  auction_in    BIGINT NOT NULL DEFAULT 0 CHECK (auction_in >= 0),
  auction_in_w  BIGINT NOT NULL DEFAULT 0 CHECK (auction_in_w >= 0),
  auction_out   BIGINT NOT NULL DEFAULT 0 CHECK (auction_out >= 0),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, hour_start)
);
COMMENT ON TABLE  income_hourly IS '원장에서 파생한 시간별 획득 집계(재화 정본은 원장). 경제 요청 트랜잭션이 같은 트랜잭션에서 증분하고 일 1회 원장과 대조·재계산한다. 35일 보관';
COMMENT ON COLUMN income_hourly.item_value IS '획득한 아이템을 상점 판매가로 환산한 값. 모아 두고 팔지 않아도 감시된다';
COMMENT ON COLUMN income_hourly.auction_in_w IS '경매 판매 수입 x 상대 위험 가중(플래그 최댓값/100). 다른 계정에서 들어온 순유입 감시';
CREATE INDEX income_hourly_updated ON income_hourly (updated_at);
CREATE INDEX income_hourly_hour ON income_hourly (hour_start);

CREATE TABLE economy_holds (
  id             BIGSERIAL PRIMARY KEY,
  uuid           UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id     BIGINT NOT NULL REFERENCES accounts(id),
  character_id   BIGINT REFERENCES characters(id),
  scope_char     BIGINT GENERATED ALWAYS AS (COALESCE(character_id, 0)) STORED,
  kind           TEXT NOT NULL CHECK (kind IN ('velocity', 'auction', 'linked', 'manual')),
  state          TEXT NOT NULL CHECK (state IN ('shadow', 'active', 'released', 'clawed_back')),
  origin_hold_id BIGINT REFERENCES economy_holds(id),
  window_kind    TEXT CHECK (window_kind IN ('1h', '24h', '7d', 'auction_24h')),
  window_start   TIMESTAMPTZ,
  window_end     TIMESTAMPTZ,
  evidence       JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(evidence) = 'object'),
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  reviewed_by    TEXT,
  reviewed_at    TIMESTAMPTZ,
  released_at    TIMESTAMPTZ,
  note           TEXT CHECK (char_length(note) <= 500),
  clawback       JSONB CHECK (clawback IS NULL OR jsonb_typeof(clawback) = 'object'),
  CONSTRAINT economy_holds_linked_chk CHECK ((kind = 'linked') = (origin_hold_id IS NOT NULL)),
  CONSTRAINT economy_holds_review_chk CHECK (state IN ('shadow', 'active') OR reviewed_at IS NOT NULL)
);
COMMENT ON TABLE  economy_holds IS '경제 정지(검토 중). shadow = log_only 모드 기록(막지 않음), active = 막음. 자동 해제 없음. 기록은 지우지 않는다';
COMMENT ON COLUMN economy_holds.character_id IS 'NULL이면 계정 전체(경매 위반·연결 전파·수동 계정 정지)';
COMMENT ON COLUMN economy_holds.evidence IS '근거: { metric, value, cap, mult, active_seconds, band, windows:{...} }. 플레이어에게 보이지 않는다';
COMMENT ON COLUMN economy_holds.released_at IS '해제 시각. 이후 평가에서 이 시각 이전 버킷은 제외(운영자가 승인한 수입)';
-- 막는 상태는 active와 clawed_back(회수 뒤에도 운영자가 해제할 때까지 막는다). 같은 (계정, 범위)에 막는 정지는 하나
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char) WHERE state IN ('active', 'clawed_back');
CREATE INDEX economy_holds_account_active ON economy_holds (account_id) WHERE state IN ('active', 'clawed_back');
CREATE INDEX economy_holds_state_time ON economy_holds (state, created_at DESC);
CREATE INDEX economy_holds_shadow ON economy_holds (account_id, scope_char, created_at DESC) WHERE state = 'shadow';
CREATE INDEX economy_holds_origin ON economy_holds (origin_hold_id) WHERE origin_hold_id IS NOT NULL;

-- ---------- 4. 파티 사람 수, 기여 ----------

ALTER TABLE party_run_members
  ADD COLUMN device_hash TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  ADD COLUMN steam_key   TEXT CHECK (steam_key ~ '^[0-9]{17}$'),
  ADD COLUMN install_id  UUID;
COMMENT ON COLUMN party_run_members.install_id IS '판 시작 때 세션의 설치 id 스냅샷(device_hash를 생략·위조해도 같은 설치 = 한 사람)';
COMMENT ON COLUMN party_run_members.device_hash IS '판 시작 때 이 멤버 세션의 기기 스냅샷(같은 기기 = 한 사람)';
COMMENT ON COLUMN party_run_members.steam_key IS '판 시작 때 COALESCE(steam_owner_id, steam subject) 스냅샷(같은 Steam 소유자 = 한 사람)';

ALTER TABLE dungeon_runs ADD COLUMN contribution JSONB CHECK (contribution IS NULL OR jsonb_typeof(contribution) = 'object');
COMMENT ON COLUMN dungeon_runs.contribution IS '정산 때의 기여 판정 { share, hits, source: host|none, met }. 관리자 검토용';

ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_lock_reason_check;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_lock_reason_check
  CHECK (lock_reason IN ('ALREADY_CLAIMED', 'TOO_FEW_HUMANS', 'KEYS_MISSING', 'LOW_CONTRIBUTION'));
COMMENT ON COLUMN dungeon_runs.lock_reason IS 'ALREADY_CLAIMED 이번 기간 보상 수령 / TOO_FEW_HUMANS 보상 최소 인원(사람 수, 같은 기기·Steam은 1명) 미달 / KEYS_MISSING 열쇠 부족 / LOW_CONTRIBUTION 피해 지분·적중 수 기여 부족';

-- ---------- 5. 전직·각성 서버 기록 ----------

CREATE TABLE character_career (
  character_id     BIGINT PRIMARY KEY REFERENCES characters(id),
  career           SMALLINT NOT NULL CHECK (career BETWEEN 1 AND 4),
  stage            SMALLINT NOT NULL DEFAULT 0 CHECK (stage BETWEEN 0 AND 5),
  promoted_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  stage_changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  source           TEXT NOT NULL CHECK (source IN ('promote', 'legacy_backfill', 'admin'))
);
COMMENT ON TABLE  character_career IS '전직·각성의 서버 진실. 행 없음 = 미전직. PUT state는 이 값과 일치하는 career·questStage·awakened만 받는다';
COMMENT ON COLUMN character_career.stage IS 'Progression.AwakeningStage와 같다: 0~1 대화, 2 시련 대기, 3~4 대화, 5 각성 완료';
COMMENT ON COLUMN character_career.source IS 'legacy_backfill = 이 마이그레이션 전에 클라이언트 값으로 저장된 상태를 이전(감사용)';

CREATE TABLE character_career_trials (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  career       SMALLINT NOT NULL CHECK (career BETWEEN 1 AND 4),
  started_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  ended_at     TIMESTAMPTZ,
  outcome      TEXT CHECK (outcome IN ('success', 'fail', 'expired')),
  CONSTRAINT career_trials_end_chk CHECK ((ended_at IS NULL) = (outcome IS NULL))
);
COMMENT ON TABLE character_career_trials IS '각성 시련 시도(시작과 보고). 성공 보고는 최소 시간·마을 체류 검증을 통과해야 stage 3으로 넘어간다';
CREATE UNIQUE INDEX career_trials_one_open ON character_career_trials (character_id) WHERE ended_at IS NULL;
CREATE INDEX career_trials_char ON character_career_trials (character_id, started_at DESC);

INSERT INTO character_career (character_id, career, stage, promoted_at, stage_changed_at, source)
SELECT cs.character_id,
       (cs.career->>'career')::int,
       LEAST(5, GREATEST(0, COALESCE((cs.career->>'questStage')::int, 0))),
       now(), now(), 'legacy_backfill'
  FROM character_state cs
 WHERE cs.career IS NOT NULL AND jsonb_typeof(cs.career) = 'object'
   AND COALESCE((cs.career->>'career')::int, 0) BETWEEN 1 AND 4;

-- ---------- 6. 경매 의심 거래 ----------

CREATE TABLE auction_trade_flags (
  id         BIGSERIAL PRIMARY KEY,
  trade_id   BIGINT NOT NULL REFERENCES auction_trades(id),
  flag       TEXT NOT NULL CHECK (flag IN ('CEILING_PRICE', 'NEW_BUYER', 'SAME_DEVICE', 'SAME_STEAM', 'SAME_IP', 'PAIR_REPEAT')),
  weight_pct SMALLINT NOT NULL CHECK (weight_pct BETWEEN 100 AND 1000),
  detail     JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (trade_id, flag)
);
COMMENT ON TABLE auction_trade_flags IS '성공한 체결의 의심 표시(추가만). 거절하지 않고 기록해 관리자 목록·경매 위반 가중·정지 전파의 근거가 된다';
CREATE INDEX auction_trade_flags_time ON auction_trade_flags (created_at DESC);
CREATE INDEX auction_trade_flags_flag ON auction_trade_flags (flag, created_at DESC);
CREATE TRIGGER auction_trade_flags_append_only BEFORE UPDATE OR DELETE ON auction_trade_flags
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER auction_trade_flags_no_truncate BEFORE TRUNCATE ON auction_trade_flags
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 7. 원장 사유, 이상 기록 종류, 관리자 감사 대상 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback'));

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution'));
COMMENT ON COLUMN anomaly_log.kind IS '기존 값 + kill_presence 처치 보고의 맵·보스 체류 불일치 / device_limit 기기 동시 접속 초과 / ip_cluster 같은 IP 다수 동시 접속(감시 점수) / member_card 호스트가 보고한 멤버 카드 불일치 / career_state 전직·각성 비정상 시도 / contribution 기여 판단 불가·분쟁';

ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold'));

-- ============ DOWN ============
-- 개발 DB 전용. 새 표·열을 지우고 CHECK를 0019 상태로 되돌린다(원장 행 삭제는 추가 전용 트리거를 잠시 끈다).

DELETE FROM admin_audit_log WHERE target_type = 'hold';
ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server'));

DELETE FROM anomaly_log WHERE kind IN ('kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution');
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse'));
COMMENT ON COLUMN anomaly_log.kind IS NULL;

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason = 'admin_clawback';
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal'));

ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason = 'admin_clawback';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost'));

DROP TABLE auction_trade_flags;
DROP TABLE character_career_trials;
DROP TABLE character_career;
DROP TABLE economy_holds;
DROP TABLE income_hourly;
DROP TABLE play_time_hourly;
DROP TABLE online_sessions;
DROP TABLE account_ips;
DROP TABLE account_devices;
DROP TABLE login_events;

UPDATE dungeon_runs SET lock_reason = 'TOO_FEW_HUMANS' WHERE lock_reason = 'LOW_CONTRIBUTION';
ALTER TABLE dungeon_runs DROP CONSTRAINT dungeon_runs_lock_reason_check;
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_lock_reason_check
  CHECK (lock_reason IN ('ALREADY_CLAIMED', 'TOO_FEW_HUMANS', 'KEYS_MISSING'));
COMMENT ON COLUMN dungeon_runs.lock_reason IS 'ALREADY_CLAIMED 이번 기간 보상 수령 / TOO_FEW_HUMANS 보상 최소 인원 미달 / KEYS_MISSING 정산 때 봉인 열쇠 부족';
ALTER TABLE dungeon_runs DROP COLUMN contribution;
ALTER TABLE party_run_members DROP COLUMN install_id, DROP COLUMN steam_key, DROP COLUMN device_hash;

ALTER TABLE refresh_tokens
  DROP CONSTRAINT refresh_tokens_revoke_chk,
  DROP COLUMN revoke_reason,
  DROP COLUMN device_hash,
  DROP COLUMN install_id;
DROP INDEX auth_identities_steam_owner;
ALTER TABLE auth_identities DROP COLUMN steam_owner_id;
ALTER TABLE accounts
  DROP COLUMN active_session_at,
  DROP COLUMN active_device_hash,
  DROP COLUMN active_install_id,
  DROP COLUMN active_family_id;
