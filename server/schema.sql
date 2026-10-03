-- dotRPG 서버 전체 스키마 (1~3단계 기준). 정본은 migrations/*.sql 이고, 이 파일은 읽기용으로 UP 본문을 모아 둔 것이다.
-- 마이그레이션을 추가하면 이 파일도 같은 커밋에서 맞춘다. 구성: 0001_init(1~2단계) + 0002_economy(3단계) + 0003_dungeon_solo(3단계-b, 선택).
--
-- 컨벤션
--   - 내부 키 id BIGSERIAL(API 비노출), 외부 노출은 uuid UUID UNIQUE DEFAULT gen_random_uuid()
--     (1:1 상태 표와 "캐릭터당 한 번" 같은 복합 키 표는 자연 키를 PK로 쓴다: character_state, quest_claims 등)
--   - 열거 값은 TEXT + CHECK, 시각은 TIMESTAMPTZ(now()), 삭제는 deleted_at(소프트), 의미는 COMMENT ON
--   - 재화(골드·아이템·경험치)가 움직이는 쪽: 잔액 BIGINT CHECK(>=0) + 추가만 하는 원장 + request_log의 UNIQUE(account_id, request_id)
--     아이템 재고는 (character_id, location, item_key)별로 "원장 delta 합 = character_items.count"가 성립한다
--   - 같은 캐릭터를 바꾸는 요청은 characters 행을 먼저 FOR UPDATE로 잠근 뒤 자식 표를 만진다(락 순서 하나)
--   - 클라이언트가 보내는 값은 행동·대상 id·request_id뿐. 금액·확률·보상량·시간은 받지 않는다
--   - 게임 데이터 값(드롭률·가격 등)은 SQL에 복사하지 않고 server/data/*.json 이름으로만 참조한다
--   - 시각 경계(06:00 일일, 목요일 06:00 주간)는 서버 KST 함수 하나로만 계산한다 (Docs/server/phase1_2_api.md 0.5)
--
-- 기능별 테이블 사용처
--   | 기능                         | 읽기                          | 쓰기                                                        |
--   |------------------------------|-------------------------------|-------------------------------------------------------------|
--   | /health, /meta               | (SELECT 1)                    | -                                                           |
--   | dev register                 | auth_identities               | accounts, auth_identities, refresh_tokens                   |
--   | dev login / refresh / logout | accounts, auth_identities, refresh_tokens | refresh_tokens, accounts.last_login_at          |
--   | GET /me                      | accounts, auth_identities, characters |  -                                                  |
--   | 캐릭터 생성                  | accounts(행 잠금), characters, request_log | characters, character_state, character_items, gold_ledger, item_ledger, request_log |
--   | 캐릭터 목록·상세             | characters, character_state, character_items, quest_claims, character_enhance_pity, character_chests, site_deliveries | - |
--   | 캐릭터 삭제                  | characters                    | characters.deleted_at                                       |
--   | 상태 저장 PUT                | characters(level, class)      | character_state                                             |
--   | 처치 보고                    | characters(행 잠금), kill_log(속도 창), kill_stats, character_items(착용 무기 화력 상한) | kill_log, kill_stats, drops, xp_ledger, characters(level, xp), anomaly_log(거절), request_log |
--   | 드롭 줍기                    | characters(행 잠금), drops    | drops.claimed_at, characters.gold, character_items, gold_ledger, item_ledger, request_log, anomaly_log(남의 id) |
--   | 채집·노드 쿨다운 조회        | characters(행 잠금), character_node_state | character_node_state, character_items, item_ledger, request_log, anomaly_log |
--   | 상자 열기                    | characters(행 잠금), character_chests | character_chests, character_items, item_ledger, request_log, anomaly_log |
--   | 공사장 납품                  | characters(행 잠금), quest_claims, site_deliveries, character_items | site_deliveries, character_items, item_ledger, request_log |
--   | 퀘스트 보상 청구             | characters(행 잠금), quest_claims, kill_stats, character_state(story_flags), site_deliveries, dungeon_runs(0003), character_items | quest_claims, characters(level, xp, gold), character_items, gold_ledger, item_ledger, xp_ledger, request_log, anomaly_log |
--   | 상점 구매·판매               | characters(행 잠금), character_items | characters.gold, character_items, gold_ledger, item_ledger, request_log |
--   | 강화                         | characters(행 잠금), character_items, character_enhance_pity | characters.gold, character_items, character_enhance_pity, enhance_log, gold_ledger, item_ledger, request_log |
--   | 아이템 사용(물약·주문서·당근)| characters(행 잠금), character_items | character_items, item_ledger, request_log                 |
--   | 창고 이동                    | characters(행 잠금), character_items | character_items, item_ledger, request_log                |
--   | 장착·해제                    | characters(class, 행 잠금), character_items | character_items, item_ledger, request_log          |
--   | 솔로 던전(0003)              | characters(행 잠금), dungeon_runs, kill_log | dungeon_runs, kill_log, drops, xp_ledger, characters, character_items, gold_ledger, item_ledger, request_log, anomaly_log |
--   | (4단계 이후 테이블: 파티, 던전 방 단위, 채팅, 경매는 해당 단계 마이그레이션에서 추가)                      |
--
--   정리 작업(7단계 전까지는 스크립트): request_log 7일, kill_log 7일, drops(만료·수령) 1일, anomaly_log 30일. drops는 kill_log 삭제 때 함께 지워진다(ON DELETE CASCADE).
--

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

-- =====================================================================================
-- 0002_economy (3단계 경제 판정). 아래는 migrations/0002_economy.sql 의 UP 본문이다.
-- =====================================================================================

-- ---------- 3단계: 기존 원장 확장 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost'));
ALTER TABLE gold_ledger ADD COLUMN request_id UUID;
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. drop_claim 줍기 / quest_reward 퀘스트 보상 / shop_buy·shop_sell 상점 / enhance_cost 강화 비용. 던전 카드(dungeon_card)는 0003이 추가';
COMMENT ON COLUMN gold_ledger.ref IS 'drop_claim: 드롭 uuid / quest_reward: 퀘스트 id / shop_*: 아이템 id / enhance_cost: enhance_log uuid';
COMMENT ON COLUMN gold_ledger.request_id IS '이 변동을 만든 요청의 request_id. 한 요청이 만든 모든 원장 행을 찾는다. starter 행은 NULL 가능';
CREATE INDEX gold_ledger_request_idx ON gold_ledger (request_id) WHERE request_id IS NOT NULL;
CREATE UNIQUE INDEX gold_ledger_drop_uq ON gold_ledger (ref) WHERE reason = 'drop_claim';

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item'));
ALTER TABLE item_ledger ADD COLUMN location TEXT NOT NULL DEFAULT 'bag'
  CHECK (location IN ('bag', 'storage', 'worn', 'mail', 'auction'));
ALTER TABLE item_ledger ADD COLUMN balance_after INT;
ALTER TABLE item_ledger ADD COLUMN request_id UUID;

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
UPDATE item_ledger l SET location = 'worn'
 WHERE l.reason = 'starter'
   AND EXISTS (SELECT 1 FROM character_items ci
                WHERE ci.character_id = l.character_id AND ci.item_key = l.item_key AND ci.location = 'worn');
UPDATE item_ledger SET balance_after = delta WHERE reason = 'starter' AND balance_after IS NULL;
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;

ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_balance_chk
  CHECK (balance_after >= 0 AND (reason = 'starter' OR balance_after IS NOT NULL));
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_no_gold CHECK (item_key <> 'gold');
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). enhance_result는 강화 결과로 키가 바뀌는 쌍(-1 옛 키, +1 새 키) / equip·unequip·storage_move는 위치 이동 쌍 / use_item은 물약·주문서·당근 소모. 던전 카드는 0003이 추가';
COMMENT ON COLUMN item_ledger.ref IS 'drop_claim: 드롭 uuid / gather: 노드 id / chest: 상자 id / quest_*: 퀘스트 id / delivery: 납품처 id / shop_*: 아이템 id / enhance_*: enhance_log uuid / use_item: 아이템 id';
COMMENT ON COLUMN item_ledger.location IS '이 변동이 일어난 위치. 위치 이동은 (출발 -n, 도착 +n) 두 행. 합계는 (character_id, location, item_key)별로 character_items.count와 같다';
COMMENT ON COLUMN item_ledger.balance_after IS '변동 직후 그 위치의 수량(0 가능). starter 행만 NULL 허용';
COMMENT ON COLUMN item_ledger.request_id IS '이 변동을 만든 요청의 request_id. starter 행은 NULL 가능';
CREATE INDEX item_ledger_request_idx ON item_ledger (request_id) WHERE request_id IS NOT NULL;
CREATE UNIQUE INDEX item_ledger_drop_uq ON item_ledger (ref) WHERE reason = 'drop_claim';

-- ---------- 3단계: 소지품 재고 규칙 ----------

CREATE UNIQUE INDEX character_items_stack_uq ON character_items (character_id, location, item_key)
  WHERE location IN ('bag', 'storage');
ALTER TABLE character_items ADD CONSTRAINT character_items_no_gold CHECK (item_key <> 'gold');
COMMENT ON TABLE  character_items IS '캐릭터 소지품. 변경은 item_ledger와 같은 트랜잭션에서만. bag·storage는 키별 한 행(수량 합), worn은 슬롯별 한 행(count=1). 수량이 0이 되면 행을 지운다';
COMMENT ON COLUMN character_items.item_key IS '게임 데이터 아이템 키. 장비는 기본 id 또는 "id+강화단계". 골드는 아이템이 아니라 characters.gold';
COMMENT ON COLUMN character_items.version IS '행이 바뀔 때마다 +1(진단용). 동시성은 characters 행 잠금이 맡는다';

-- ---------- 3단계: 경험치 원장 ----------

CREATE TABLE xp_ledger (
  id           BIGSERIAL PRIMARY KEY,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  delta        INT NOT NULL CHECK (delta > 0),
  level_after  INT NOT NULL CHECK (level_after >= 1),
  xp_after     INT NOT NULL CHECK (xp_after >= 0),
  reason       TEXT NOT NULL CHECK (reason IN ('kill', 'quest_reward')),
  ref          TEXT,
  request_id   UUID,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  xp_ledger IS '경험치 지급 원장. 추가만 한다. characters.level/xp는 이 원장과 같은 트랜잭션에서만 바뀐다. 만렙에서 버려지는 경험치는 기록하지 않는다';
COMMENT ON COLUMN xp_ledger.delta IS '요청이 준 경험치(몬스터 레벨 보정 후). 레벨업으로 xp가 줄어도 양수';
COMMENT ON COLUMN xp_ledger.level_after IS '지급 직후 레벨. 레벨 변화 이력 조회용';
COMMENT ON COLUMN xp_ledger.xp_after IS '지급 직후 현재 레벨 내 경험치';
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상. 던전 클리어(dungeon_clear)는 0003이 추가';
COMMENT ON COLUMN xp_ledger.ref IS 'kill: 몬스터 id / quest_reward: 퀘스트 id';
CREATE INDEX xp_ledger_char_idx ON xp_ledger (character_id, id);
CREATE TRIGGER xp_ledger_append_only BEFORE UPDATE OR DELETE ON xp_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER xp_ledger_no_truncate BEFORE TRUNCATE ON xp_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 3단계: 처치 보고 ----------

CREATE TABLE kill_log (
  id            BIGSERIAL PRIMARY KEY,
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  map_id        TEXT NOT NULL,
  monster_id    TEXT NOT NULL,
  monster_level SMALLINT NOT NULL CHECK (monster_level >= 1),
  context       TEXT NOT NULL CHECK (context IN ('field', 'scripted', 'dungeon')),
  hits          SMALLINT NOT NULL DEFAULT 0 CHECK (hits >= 0),
  xp_granted    INT NOT NULL CHECK (xp_granted >= 0),
  request_id    UUID NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  kill_log IS '서버가 받아들인 처치 보고(그럴듯함 검사를 통과한 것만). 속도 창 계산과 퀘스트 처치 검증, 사후 추적에 쓴다. 7일 보관 후 정리';
COMMENT ON COLUMN kill_log.map_id IS '보고된 맵 id. 던전 처치는 방 맵 id';
COMMENT ON COLUMN kill_log.monster_level IS '서버가 계산한 몬스터 레벨(필드 1, 던전 1+난이도 몬스터 레벨+그룹 오프셋). 요청으로 받지 않는다';
COMMENT ON COLUMN kill_log.context IS 'field 필드 스포너 / scripted 연출 스폰(마을 해골 습격) / dungeon 던전 방(0003의 run_id와 함께)';
COMMENT ON COLUMN kill_log.hits IS '황금 해골류가 타격마다 흘린 골드 횟수(서버가 상한으로 자른 값). 그 밖의 몬스터는 0';
COMMENT ON COLUMN kill_log.xp_granted IS '이 처치로 지급한 경험치(레벨 보정 후)';
CREATE INDEX kill_log_char_time ON kill_log (character_id, created_at);
CREATE INDEX kill_log_created_idx ON kill_log (created_at);

CREATE TABLE kill_stats (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  monster_id   TEXT NOT NULL,
  kills        BIGINT NOT NULL DEFAULT 0 CHECK (kills >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, monster_id)
);
COMMENT ON TABLE  kill_stats IS '캐릭터·몬스터별 누적 처치 수. 퀘스트 처치 목표 검증(서버가 받아들인 처치만 센다)과 통계. kill_log 정리와 무관하게 평생 누적';
COMMENT ON COLUMN kill_stats.kills IS '받아들인 처치 누적. 처치를 받아들일 때 같은 트랜잭션에서 +1';

-- ---------- 3단계: 드롭 ----------

CREATE TABLE drops (
  id           BIGSERIAL PRIMARY KEY,
  uuid         UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  kill_id      BIGINT NOT NULL REFERENCES kill_log(id) ON DELETE CASCADE,
  item_key     TEXT NOT NULL,
  count        INT NOT NULL CHECK (count > 0),
  expires_at   TIMESTAMPTZ NOT NULL,
  claimed_at   TIMESTAMPTZ,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  drops IS '서버가 굴린 드롭. 줍기는 uuid로만, 한 번만. 한 행 = 바닥의 줍기 물체 하나(골드 더미, 재료 1개, 장비 1개)';
COMMENT ON COLUMN drops.uuid IS '외부 노출 id(응답의 drop id). 줍기 요청이 이 값으로만 온다';
COMMENT ON COLUMN drops.item_key IS '아이템 키. 골드 더미는 "gold"(이 테이블에서만 허용)';
COMMENT ON COLUMN drops.count IS '골드면 금액, 그 밖에는 개수';
COMMENT ON COLUMN drops.expires_at IS '이 시각이 지나면 줍기 불가(미수령은 그냥 사라진다)';
COMMENT ON COLUMN drops.claimed_at IS '줍기 완료 시각. NULL = 미수령. UPDATE ... WHERE claimed_at IS NULL AND expires_at > now() 한 문장으로 한 번만 성립';
CREATE INDEX drops_char_open ON drops (character_id) WHERE claimed_at IS NULL;
CREATE INDEX drops_expires_idx ON drops (expires_at);

-- ---------- 3단계: 채집 노드, 상자, 납품 ----------

CREATE TABLE character_node_state (
  character_id     BIGINT NOT NULL REFERENCES characters(id),
  node_id          TEXT NOT NULL,
  map_id           TEXT NOT NULL,
  last_gathered_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (character_id, node_id)
);
COMMENT ON TABLE  character_node_state IS '캐릭터별 채집 노드(나무·바위·당근밭) 마지막 채집 시각. 재생 시간 판정의 정본. 한 번도 안 캔 노드는 행이 없다(= 언제든 가능)';
COMMENT ON COLUMN character_node_state.node_id IS '"맵id:x:y" (상자 id와 같은 규칙, 서버 데이터 maps.json의 nodes[].id)';
COMMENT ON COLUMN character_node_state.map_id IS 'node_id의 맵 부분(맵 입장 때 쿨다운 목록을 한 번에 읽기 위해 따로 둔다)';
COMMENT ON COLUMN character_node_state.last_gathered_at IS '마지막으로 받아들인 채집 시각(서버 시계). 재생 시간 이후에만 다시 채집 가능';
CREATE INDEX character_node_state_map ON character_node_state (character_id, map_id, last_gathered_at);

CREATE TABLE character_chests (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  chest_id     TEXT NOT NULL,
  opened_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, chest_id)
);
COMMENT ON TABLE  character_chests IS '연 상자(SaveData.openedChests). PK가 "캐릭터당 한 번"을 DB 수준에서 보장한다';
COMMENT ON COLUMN character_chests.chest_id IS '"맵id:x:y" (WorldBuilder의 TreasureChest id와 같은 규칙)';

CREATE TABLE site_deliveries (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  site_id      TEXT NOT NULL,
  item_key     TEXT NOT NULL,
  delivered    INT NOT NULL DEFAULT 0 CHECK (delivered >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, site_id, item_key)
);
COMMENT ON TABLE  site_deliveries IS '납품처(공방 공사장)에 낸 재료 누적(QuestProgress.woodDelivered/stoneDelivered 대체). 필요량은 서버 데이터 deliverySites';
COMMENT ON COLUMN site_deliveries.site_id IS '납품처 id (예: workshop)';
COMMENT ON COLUMN site_deliveries.delivered IS '지금까지 낸 수량. 필요량을 넘지 않는다(서버가 min으로 자른다)';

-- ---------- 3단계: 퀘스트 청구 ----------

CREATE TABLE quest_claims (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  quest_id     TEXT NOT NULL,
  claimed_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  reward       JSONB NOT NULL CHECK (jsonb_typeof(reward) = 'object'),
  request_id   UUID,
  PRIMARY KEY (character_id, quest_id)
);
COMMENT ON TABLE  quest_claims IS '퀘스트 보상 청구 기록. PK가 "퀘스트당 한 번"을 DB 수준에서 보장한다. character_state.quests(클라이언트 저장 상태)와 무관한 정본';
COMMENT ON COLUMN quest_claims.reward IS '지급 당시 보상 스냅샷 {xp, gold, items:[{item_key,count}], max_health, set_flags:[], consumed:[{item_key,count}]}. 최대 체력 보너스 합계는 이 값에서 계산(max_health)';
COMMENT ON COLUMN quest_claims.request_id IS '청구 요청의 request_id (원장 행과 연결)';

-- ---------- 3단계: 강화 ----------

CREATE TABLE character_enhance_pity (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  item_key     TEXT NOT NULL,
  pity         SMALLINT NOT NULL CHECK (pity BETWEEN 0 AND 100),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, item_key)
);
COMMENT ON TABLE  character_enhance_pity IS '강화 천장(보정 %p). 무기 +10/+11 시도에서 실패마다 +1, 성공하면 그 키의 행을 지운다. 키(예: eq_sword_iron+10) 단위라 같은 키의 모든 복사본이 공유한다(C# Equipment.pity와 같다)';
COMMENT ON COLUMN character_enhance_pity.item_key IS '시도하는 쪽 키("id+레벨"). 시도 레벨 키로만 쌓인다';
COMMENT ON COLUMN character_enhance_pity.pity IS '보정 %p. 상한 100(서버 데이터 enhance.json maxPity)';

CREATE TABLE enhance_log (
  id              BIGSERIAL PRIMARY KEY,
  uuid            UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id    BIGINT NOT NULL REFERENCES characters(id),
  request_id      UUID NOT NULL,
  from_key        TEXT NOT NULL,
  to_key          TEXT,
  target_location TEXT NOT NULL CHECK (target_location IN ('bag', 'worn')),
  target_slot     SMALLINT CHECK (target_slot >= 0),
  outcome         TEXT NOT NULL CHECK (outcome IN ('success', 'keep', 'drop3', 'destroyed', 'protected')),
  roll            SMALLINT NOT NULL CHECK (roll BETWEEN 0 AND 99),
  success_percent SMALLINT NOT NULL CHECK (success_percent BETWEEN 0 AND 100),
  pity_before     SMALLINT NOT NULL CHECK (pity_before BETWEEN 0 AND 100),
  pity_after      SMALLINT NOT NULL CHECK (pity_after BETWEEN 0 AND 100),
  gold            INT NOT NULL CHECK (gold >= 0),
  bone            INT NOT NULL CHECK (bone >= 0),
  ore             INT NOT NULL CHECK (ore >= 0),
  essence         INT NOT NULL CHECK (essence >= 0),
  ticket_used     BOOLEAN NOT NULL DEFAULT false,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT enhance_log_slot_chk CHECK ((target_location = 'worn') = (target_slot IS NOT NULL))
);
COMMENT ON TABLE  enhance_log IS '강화 시도 기록(추가만). 굴린 값·확률·비용을 남겨 분쟁과 확률 통계를 검증한다. 원장 행은 이 행의 uuid를 ref로 가리킨다';
COMMENT ON COLUMN enhance_log.from_key IS '시도 전 키';
COMMENT ON COLUMN enhance_log.to_key IS '시도 후 키. 파괴되면 NULL. protected(보호권)는 +0 기본 id';
COMMENT ON COLUMN enhance_log.outcome IS 'success 성공 / keep 실패·유지 / drop3 실패·3단계 하락 / destroyed 파괴 / protected 파괴를 보호권이 막아 +0으로 초기화';
COMMENT ON COLUMN enhance_log.roll IS '서버 RNG 0..99. roll < success_percent 이면 성공';
COMMENT ON COLUMN enhance_log.success_percent IS '표 확률 + 천장 보정(상한 100)';
COMMENT ON COLUMN enhance_log.pity_before IS '시도 직전 천장 값(천장 없는 레벨은 0)';
COMMENT ON COLUMN enhance_log.pity_after IS '시도 직후 천장 값';
COMMENT ON COLUMN enhance_log.gold IS '낸 골드(재료는 bone/ore/essence). 보호권 소모는 ticket_used';
CREATE INDEX enhance_log_char_idx ON enhance_log (character_id, id);
CREATE TRIGGER enhance_log_append_only BEFORE UPDATE OR DELETE ON enhance_log
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER enhance_log_no_truncate BEFORE TRUNCATE ON enhance_log
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 3단계: 이상 기록 ----------

CREATE TABLE anomaly_log (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT REFERENCES characters(id),
  kind         TEXT NOT NULL CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                                             'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown')),
  severity     SMALLINT NOT NULL DEFAULT 1 CHECK (severity BETWEEN 1 AND 3),
  detail       JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  anomaly_log IS '그럴듯하지 않아 지급을 막은 요청의 기록(PLAN_SERVER §5 "지급 보류 + 기록"). 지급을 미루지 않고 거절하며 기록만 남긴다. 제재는 7단계 관리자 도구가 이 표를 본다';
COMMENT ON COLUMN anomaly_log.kind IS 'kill_target 맵·몬스터 불일치 / kill_rate 1초 창 초과 / kill_supply 리스폰 공급 초과 / kill_power 화력 상한 초과 / gather_node 없는 노드 / gather_early 재생 전 채집 / drop_foreign 남의 드롭 id / quest_denied 근거 없는 퀘스트 청구 / chest_unknown 없는 상자';
COMMENT ON COLUMN anomaly_log.severity IS '1 참고(경계 오차 가능) / 2 의심 / 3 불가능한 값. 반복 횟수 기준 차단은 service 설정';
COMMENT ON COLUMN anomaly_log.detail IS '요청 값과 판정 근거(창 안 수, 한도, 맵·몬스터 등). 원본 요청 본문 전체는 넣지 않는다';
CREATE INDEX anomaly_log_char_time ON anomaly_log (character_id, created_at) WHERE character_id IS NOT NULL;
CREATE INDEX anomaly_log_created_idx ON anomaly_log (created_at);

-- =====================================================================================
-- 0003_dungeon_solo (3단계-b, 선택: 솔로 요일 던전). 아래는 migrations/0003_dungeon_solo.sql 의 UP 본문이다.
-- 채택하지 않으면 이 구역을 지운다(설계 문서 Docs/server/phase3_api.md 9절).
-- =====================================================================================

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card'));

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card'));

ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear'));

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result'));

CREATE TABLE dungeon_runs (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  dungeon_id    TEXT NOT NULL,
  difficulty    SMALLINT NOT NULL CHECK (difficulty BETWEEN 0 AND 3),
  party_size    SMALLINT NOT NULL DEFAULT 1 CHECK (party_size BETWEEN 1 AND 4),
  state         TEXT NOT NULL DEFAULT 'playing'
                CHECK (state IN ('playing', 'cleared', 'failed', 'abandoned', 'held')),
  reset_day     TIMESTAMPTZ NOT NULL,
  started_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  ended_at      TIMESTAMPTZ,
  room_index    SMALLINT NOT NULL DEFAULT 0 CHECK (room_index >= 0),
  room_kills    JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(room_kills) = 'object'),
  stats         JSONB,
  rank          SMALLINT CHECK (rank BETWEEN 0 AND 8),
  score         JSONB,
  xp_granted    INT CHECK (xp_granted >= 0),
  hold_reason   TEXT,
  cards         JSONB CHECK (cards IS NULL OR jsonb_typeof(cards) = 'array'),
  card_picked   SMALLINT CHECK (card_picked >= 0),
  card_picked_at TIMESTAMPTZ,
  CONSTRAINT dungeon_runs_cleared_chk CHECK (state <> 'cleared' OR (rank IS NOT NULL AND cards IS NOT NULL AND ended_at IS NOT NULL)),
  CONSTRAINT dungeon_runs_ended_chk CHECK ((state = 'playing') = (ended_at IS NULL)),
  CONSTRAINT dungeon_runs_card_chk CHECK ((card_picked IS NULL) = (card_picked_at IS NULL))
);
COMMENT ON TABLE  dungeon_runs IS '솔로 요일 던전 도전 기록. 입장 횟수(일일)·클리어·최고 랭크·카드가 모두 이 표에서 나온다(별도 집계 표 없음)';
COMMENT ON COLUMN dungeon_runs.uuid IS '외부 노출 id(run id). 처치 보고·결과·카드 선택 요청이 이 값으로 온다';
COMMENT ON COLUMN dungeon_runs.dungeon_id IS 'dungeons.json의 요일 던전 id(레이드는 4단계 이후)';
COMMENT ON COLUMN dungeon_runs.difficulty IS 'DungeonDifficulty 0 일반 1 모험 2 왕 3 영웅';
COMMENT ON COLUMN dungeon_runs.party_size IS '3단계는 항상 1(AI 용병 없음). 4단계에서 파티 인원';
COMMENT ON COLUMN dungeon_runs.state IS 'playing 진행 / cleared 클리어(카드 있음) / failed 실패 보고 / abandoned 방치 만료 / held 결과가 불가능해 보상 보류(관리자 검토)';
COMMENT ON COLUMN dungeon_runs.reset_day IS '입장 시각이 속한 일일 초기화 구간의 시작(서버 KST 06:00 함수를 UTC로 바꾼 값). 오늘 입장 수 집계 키';
COMMENT ON COLUMN dungeon_runs.room_index IS '서버가 인정한 현재 방. 처치 보고가 다음 방 몬스터를 가리키면 앞 방이 충분히 정리됐을 때만 올라간다';
COMMENT ON COLUMN dungeon_runs.room_kills IS '{"방번호:몬스터id": 받아들인 처치 수}. 방 구성(그룹 count)을 넘는 보고를 막고 클리어 검증에 쓴다';
COMMENT ON COLUMN dungeon_runs.stats IS '클라이언트가 보고한 사실 {elapsed_ms, hits_taken, max_combo, revives_used} 와 서버가 자른 값. 서버가 검증하지 못하는 항목은 랭크 점수에만 쓰인다';
COMMENT ON COLUMN dungeon_runs.rank IS '서버가 계산한 랭크 0 SSS ~ 8 F';
COMMENT ON COLUMN dungeon_runs.score IS '점수 구성 {time, hits, kills, combo, revive_penalty, total}';
COMMENT ON COLUMN dungeon_runs.xp_granted IS '클리어 경험치(난이도·랭크 보정 후). 처치별 경험치는 xp_ledger(kill)';
COMMENT ON COLUMN dungeon_runs.hold_reason IS 'held일 때 사유 코드(예: TOO_FAST, ROOMS_NOT_CLEARED). 클라이언트에는 알리지 않는다';
COMMENT ON COLUMN dungeon_runs.cards IS '서버가 굴린 카드 4장 [{item_key,count}]. 선택 전에는 클라이언트에 내용을 주지 않는다';
COMMENT ON COLUMN dungeon_runs.card_picked IS '고른 카드 번호(0부터). NULL = 아직 안 골랐다';
CREATE UNIQUE INDEX dungeon_runs_one_playing ON dungeon_runs (character_id) WHERE state = 'playing';
CREATE INDEX dungeon_runs_char_day ON dungeon_runs (character_id, reset_day);
CREATE INDEX dungeon_runs_clears ON dungeon_runs (character_id, dungeon_id, difficulty) WHERE state = 'cleared';

ALTER TABLE kill_log ADD COLUMN run_id BIGINT REFERENCES dungeon_runs(id);
ALTER TABLE kill_log ADD COLUMN room_index SMALLINT CHECK (room_index >= 0);
ALTER TABLE kill_log ADD CONSTRAINT kill_log_run_chk CHECK ((run_id IS NULL) = (room_index IS NULL) AND ((context = 'dungeon') = (run_id IS NOT NULL)));
COMMENT ON COLUMN kill_log.run_id IS '던전 처치일 때 dungeon_runs.id. 필드·연출 처치는 NULL';
COMMENT ON COLUMN kill_log.room_index IS '던전 방 번호(run_id와 함께)';
CREATE INDEX kill_log_run_idx ON kill_log (run_id) WHERE run_id IS NOT NULL;

