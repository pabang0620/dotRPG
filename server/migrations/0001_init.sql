-- 0001_init: 1단계(기반) + 2단계(온라인 캐릭터) 테이블
-- 대상: PostgreSQL 14 이상 (gen_random_uuid 내장). 이 파일은 UP/DOWN 마커 줄로 나뉜다.
-- 러너는 마커 줄을 기준으로 잘라 UP 또는 DOWN 부분만 한 트랜잭션으로 실행한다.
-- (node-pg-migrate의 "-- Up Migration" 형식과 다르므로 쓰려면 마커를 바꾸거나 얇은 러너를 둔다.)

-- ============ UP ============

-- 추가만 하는 원장의 수정·삭제를 DB가 막는다 (사후 추적의 근거)
CREATE FUNCTION ledger_block_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION '% is append-only (% blocked)', TG_TABLE_NAME, TG_OP;
END;
$$ LANGUAGE plpgsql;

-- ---------- 1단계: 계정·인증 ----------

CREATE TABLE accounts (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_login_at TIMESTAMPTZ,
  banned_until  TIMESTAMPTZ,
  deleted_at    TIMESTAMPTZ
);
COMMENT ON TABLE  accounts IS '게임 계정. 로그인 수단은 auth_identities에 따로 둔다';
COMMENT ON COLUMN accounts.id IS '내부 키. API에 노출하지 않는다';
COMMENT ON COLUMN accounts.uuid IS '외부 노출용 id. JWT sub, 응답의 account id';
COMMENT ON COLUMN accounts.last_login_at IS '마지막 로그인 성공 시각 (refresh는 갱신하지 않는다)';
COMMENT ON COLUMN accounts.banned_until IS '이 시각 전까지 로그인·refresh 거부. NULL = 정지 아님';
COMMENT ON COLUMN accounts.deleted_at IS '소프트 삭제. NULL이 아니면 로그인 불가';

CREATE TABLE auth_identities (
  id          BIGSERIAL PRIMARY KEY,
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  provider    TEXT NOT NULL CHECK (provider IN ('dev', 'steam')),
  subject     TEXT NOT NULL,
  secret_hash TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (provider, subject),
  CONSTRAINT auth_identities_dev_secret CHECK (provider <> 'dev' OR secret_hash IS NOT NULL),
  CONSTRAINT auth_identities_dev_lower CHECK (provider <> 'dev' OR subject = lower(subject))
);
COMMENT ON TABLE  auth_identities IS '로그인 수단. 지금은 dev(아이디·비밀번호), 나중에 steam';
COMMENT ON COLUMN auth_identities.subject IS 'dev: 소문자 아이디, steam: steam_id 문자열';
COMMENT ON COLUMN auth_identities.secret_hash IS 'dev 비밀번호 해시(argon2id 문자열). steam은 NULL';
CREATE INDEX auth_identities_account_idx ON auth_identities (account_id);

CREATE TABLE refresh_tokens (
  id         BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  family_id  UUID NOT NULL,
  token_hash TEXT NOT NULL UNIQUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at TIMESTAMPTZ NOT NULL,
  used_at    TIMESTAMPTZ,
  revoked_at TIMESTAMPTZ
);
COMMENT ON TABLE  refresh_tokens IS '교체형 갱신 토큰. 원문은 저장하지 않고 해시만 둔다';
COMMENT ON COLUMN refresh_tokens.family_id IS '로그인 1회에서 이어진 토큰 묶음. 재사용 감지 시 묶음 전체를 폐기';
COMMENT ON COLUMN refresh_tokens.token_hash IS 'SHA-256(hex) of 토큰 원문(32바이트 난수)';
COMMENT ON COLUMN refresh_tokens.used_at IS '교체되어 쓰인 시각. 이미 쓴 토큰이 또 오면 탈취로 본다';
COMMENT ON COLUMN refresh_tokens.revoked_at IS '로그아웃 또는 재사용 감지로 폐기된 시각';
CREATE INDEX refresh_tokens_family_idx  ON refresh_tokens (family_id);
CREATE INDEX refresh_tokens_expires_idx ON refresh_tokens (expires_at);

-- ---------- 2단계: 캐릭터 ----------

CREATE TABLE characters (
  id         BIGSERIAL PRIMARY KEY,
  uuid       UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  name       TEXT NOT NULL CHECK (name ~ '^[가-힣A-Za-z0-9]{2,8}$'),
  class      TEXT NOT NULL CHECK (class IN ('warrior', 'mage')),
  level      INT NOT NULL DEFAULT 1 CHECK (level >= 1),
  xp         INT NOT NULL DEFAULT 0 CHECK (xp >= 0),
  gold       BIGINT NOT NULL DEFAULT 0 CHECK (gold >= 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  deleted_at TIMESTAMPTZ
);
COMMENT ON TABLE  characters IS '온라인 캐릭터. level·xp·gold는 서버 판정(3단계)만 바꾼다';
COMMENT ON COLUMN characters.name IS '2~8자, 한글 완성형·영문·숫자. 서버가 NFC 정규화 후 저장';
COMMENT ON COLUMN characters.class IS 'CharacterClass: warrior | mage';
COMMENT ON COLUMN characters.level IS '2단계에서는 항상 1. 클라이언트 요청으로 바뀌지 않는다';
COMMENT ON COLUMN characters.xp IS '현재 레벨 내 경험치. 클라이언트 요청으로 바뀌지 않는다';
COMMENT ON COLUMN characters.gold IS '잔액. 변경은 gold_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN characters.deleted_at IS '소프트 삭제. 삭제하면 이름과 계정 슬롯이 풀린다';
-- 살아 있는 캐릭터끼리만 이름 중복 불가 (대소문자 무시). 생성 시 23505를 NAME_TAKEN으로 바꾼다
CREATE UNIQUE INDEX characters_name_alive ON characters (lower(name)) WHERE deleted_at IS NULL;
-- 목록 조회와 계정당 최대 4개 검사
CREATE INDEX characters_account_alive ON characters (account_id) WHERE deleted_at IS NULL;

CREATE TABLE character_state (
  character_id BIGINT PRIMARY KEY REFERENCES characters(id),
  map_id       TEXT NOT NULL,
  pos_x        REAL,
  pos_y        REAL,
  facing       SMALLINT NOT NULL DEFAULT 0 CHECK (facing >= 0),
  quests       JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(quests) = 'array'),
  story_flags  TEXT[] NOT NULL DEFAULT '{}',
  tracked_quest TEXT NOT NULL DEFAULT '',
  passives     TEXT[] NOT NULL DEFAULT '{}',
  skill_gems   JSONB NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(skill_gems) = 'array'),
  version      INT NOT NULL DEFAULT 0 CHECK (version >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT character_state_pos_pair CHECK ((pos_x IS NULL) = (pos_y IS NULL))
);
COMMENT ON TABLE  character_state IS '재화가 아닌 상태. 클라이언트가 PUT으로 통째로 저장하고 서버는 검증만 한다';
COMMENT ON COLUMN character_state.map_id IS 'maps.json의 instanced=false 맵 id';
COMMENT ON COLUMN character_state.pos_x IS '월드 좌표. NULL(pos_y도 NULL) = 맵 시작 지점';
COMMENT ON COLUMN character_state.facing IS 'Facing enum 값. 상한은 enums.json facingCount';
COMMENT ON COLUMN character_state.quests IS '[{id,status,step,counts[]}] QuestSave 배열';
COMMENT ON COLUMN character_state.story_flags IS 'QuestJournal.Flags';
COMMENT ON COLUMN character_state.tracked_quest IS '추적 중인 퀘스트 id. 없으면 빈 문자열';
COMMENT ON COLUMN character_state.passives IS '찍은 패시브 노드 id (시작 노드 S 제외). 개수 <= 레벨-1';
COMMENT ON COLUMN character_state.skill_gems IS '[{slot,supports:[id|null,id|null]}] 보조 젬 장착';
COMMENT ON COLUMN character_state.version IS '낙관적 잠금. 저장 성공마다 +1, 생성 직후 0. 재화 변경은 올리지 않는다';

CREATE TABLE character_items (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  item_key     TEXT NOT NULL,
  count        INT NOT NULL CHECK (count > 0),
  location     TEXT NOT NULL CHECK (location IN ('bag', 'storage', 'worn', 'mail', 'auction')),
  slot         INT CHECK (slot >= 0),
  bind         TEXT NOT NULL DEFAULT 'none' CHECK (bind IN ('none', 'account', 'character')),
  version      INT NOT NULL DEFAULT 0,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  character_items IS '캐릭터 소지품. 2단계는 시작 지급품(bag, worn)만 만든다. 변경은 item_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN character_items.item_key IS '게임 데이터 아이템 키. 장비는 기본 id 또는 "id+강화단계"';
COMMENT ON COLUMN character_items.location IS 'bag 가방 / storage 창고 / worn 착용 / mail 우편 / auction 경매 보관';
COMMENT ON COLUMN character_items.slot IS 'worn은 EquipSlot 번호. bag·storage 쌓이는 물건은 NULL';
COMMENT ON COLUMN character_items.version IS '3단계 이후 낙관적 잠금용';
-- 한 캐릭터의 같은 위치·슬롯에 두 아이템이 겹치지 않게 (착용 슬롯 등)
CREATE UNIQUE INDEX character_items_slot_uq ON character_items (character_id, location, slot) WHERE slot IS NOT NULL;
-- 캐릭터 상세에서 가방·착용 목록을 한 번에 읽는다
CREATE INDEX character_items_char_loc ON character_items (character_id, location);

-- ---------- 멱등성·원장 ----------

CREATE TABLE request_log (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  request_id   UUID NOT NULL,
  endpoint     TEXT NOT NULL,
  request_hash TEXT NOT NULL,
  status_code  SMALLINT NOT NULL,
  response     JSONB NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id)
);
COMMENT ON TABLE  request_log IS '멱등성: 같은 계정의 같은 request_id는 처음 응답을 그대로 돌려준다';
COMMENT ON COLUMN request_log.endpoint IS '"POST /characters" 같은 메서드+경로 패턴';
COMMENT ON COLUMN request_log.request_hash IS '요청 본문 정규화 해시. 같은 id에 다른 본문이 오면 IDEMPOTENCY_MISMATCH';
COMMENT ON COLUMN request_log.response IS '처음 응답의 body 전체';
-- 보관 기간이 지난 행을 지우는 정리 작업용
CREATE INDEX request_log_created_idx ON request_log (created_at);

CREATE TABLE gold_ledger (
  id            BIGSERIAL PRIMARY KEY,
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  delta         BIGINT NOT NULL CHECK (delta <> 0),
  balance_after BIGINT NOT NULL CHECK (balance_after >= 0),
  reason        TEXT NOT NULL CHECK (reason IN ('starter')),
  ref           TEXT,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  gold_ledger IS '골드 변동 원장. 추가만 한다(트리거가 UPDATE/DELETE 차단)';
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. 3단계가 kill, quest, shop_buy 등을 마이그레이션으로 추가한다';
COMMENT ON COLUMN gold_ledger.ref IS '사유별 참조(캐릭터 uuid, 퀘스트 id 등)';
CREATE INDEX gold_ledger_char_idx ON gold_ledger (character_id, id);
-- 시작 지급은 캐릭터당 한 번만 (DB 수준 이중 지급 방지)
CREATE UNIQUE INDEX gold_ledger_starter_uq ON gold_ledger (character_id) WHERE reason = 'starter';
CREATE TRIGGER gold_ledger_append_only BEFORE UPDATE OR DELETE ON gold_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER gold_ledger_no_truncate BEFORE TRUNCATE ON gold_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

CREATE TABLE item_ledger (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  item_key     TEXT NOT NULL,
  delta        INT NOT NULL CHECK (delta <> 0),
  reason       TEXT NOT NULL CHECK (reason IN ('starter')),
  ref          TEXT,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  item_ledger IS '아이템 수량 변동 원장. 추가만 한다(트리거가 UPDATE/DELETE 차단)';
COMMENT ON COLUMN item_ledger.delta IS '양수 지급, 음수 소모';
COMMENT ON COLUMN item_ledger.reason IS '변동 사유. 3단계가 마이그레이션으로 추가한다';
CREATE INDEX item_ledger_char_idx ON item_ledger (character_id, id);
CREATE UNIQUE INDEX item_ledger_starter_uq ON item_ledger (character_id, item_key) WHERE reason = 'starter';
CREATE TRIGGER item_ledger_append_only BEFORE UPDATE OR DELETE ON item_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER item_ledger_no_truncate BEFORE TRUNCATE ON item_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ============ DOWN ============

DROP TABLE IF EXISTS item_ledger;
DROP TABLE IF EXISTS gold_ledger;
DROP TABLE IF EXISTS request_log;
DROP TABLE IF EXISTS character_items;
DROP TABLE IF EXISTS character_state;
DROP TABLE IF EXISTS characters;
DROP TABLE IF EXISTS refresh_tokens;
DROP TABLE IF EXISTS auth_identities;
DROP TABLE IF EXISTS accounts;
DROP FUNCTION IF EXISTS ledger_block_mutation();
