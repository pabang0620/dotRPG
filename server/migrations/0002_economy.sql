-- 0002_economy: 3단계(경제 판정) 테이블과 원장 확장
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001과 같다. 선행: 0001_init.
-- 설계: Docs/server/phase3_api.md, Docs/server/phase3_mapping.md
--
-- 이 마이그레이션이 하는 일
--   1. gold_ledger / item_ledger 의 reason 목록을 3단계 경로로 넓히고, 요청 추적(request_id)과
--      아이템 위치(location)·잔액(balance_after)을 더한다. 아이템 재고는 (캐릭터, 위치, 아이템 키) 단위로
--      "원장 합 = character_items.count"가 항상 성립한다.
--   2. 처치 보고 기록, 드롭, 경험치 원장, 채집 노드 상태, 상자, 퀘스트 청구, 납품, 강화 천장·기록, 이상 기록 테이블을 만든다.
--   3. 던전(0003, 선택) 테이블은 여기에 넣지 않는다.
--
-- 적용 전 확인: 1~2단계 코드의 시작 지급 INSERT(item_ledger)가 시작 장비 행에 location='worn'을 쓰도록 맞춘다.
--   location 기본값이 'bag'이라 맞추지 않으면 시작 장비 원장이 bag으로 기록된다(재고 합 검증이 어긋난다).
-- 락 순서(3단계 공통): 같은 캐릭터를 바꾸는 모든 요청은 먼저 characters 행을 FOR UPDATE로 잠근 뒤 자식 테이블을 만진다.

-- ============ UP ============

-- ---------- 1. 기존 원장 확장 ----------

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost'));
ALTER TABLE gold_ledger ADD COLUMN request_id UUID;
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. drop_claim 줍기 / quest_reward 퀘스트 보상 / shop_buy·shop_sell 상점 / enhance_cost 강화 비용. 던전 카드(dungeon_card)는 0003이 추가';
COMMENT ON COLUMN gold_ledger.ref IS 'drop_claim: 드롭 uuid / quest_reward: 퀘스트 id / shop_*: 아이템 id / enhance_cost: enhance_log uuid';
COMMENT ON COLUMN gold_ledger.request_id IS '이 변동을 만든 요청의 request_id. 한 요청이 만든 모든 원장 행을 찾는다. starter 행은 NULL 가능';
-- 한 요청이 만든 원장 행 조회(분쟁 조사). 대부분 행이 값을 가지므로 NULL 제외 부분 인덱스
CREATE INDEX gold_ledger_request_idx ON gold_ledger (request_id) WHERE request_id IS NOT NULL;
-- 같은 드롭이 두 번 지급되는 것을 DB가 막는다(줍기 처리 버그가 있어도 중복 지급 불가)
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

-- 기존 행 보정: 3단계 기능이 아직 없던 시점이라 시작 지급 상태 그대로다. 보정 동안만 추가 전용 트리거를 끈다.
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

-- ---------- 2. 소지품 재고 규칙 ----------

-- 가방·창고는 같은 키가 한 행으로 쌓인다(C# Inventory가 키별 수량 사전). 착용(worn)은 슬롯 유일 인덱스가 따로 있다.
-- ON CONFLICT (character_id, location, item_key) WHERE location IN ('bag','storage') 로 수량을 더한다.
CREATE UNIQUE INDEX character_items_stack_uq ON character_items (character_id, location, item_key)
  WHERE location IN ('bag', 'storage');
ALTER TABLE character_items ADD CONSTRAINT character_items_no_gold CHECK (item_key <> 'gold');
COMMENT ON TABLE  character_items IS '캐릭터 소지품. 변경은 item_ledger와 같은 트랜잭션에서만. bag·storage는 키별 한 행(수량 합), worn은 슬롯별 한 행(count=1). 수량이 0이 되면 행을 지운다';
COMMENT ON COLUMN character_items.item_key IS '게임 데이터 아이템 키. 장비는 기본 id 또는 "id+강화단계". 골드는 아이템이 아니라 characters.gold';
COMMENT ON COLUMN character_items.version IS '행이 바뀔 때마다 +1(진단용). 동시성은 characters 행 잠금이 맡는다';

-- ---------- 3. 경험치 원장 ----------

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
-- 캐릭터별 경험치 이력(시간순). 시간당 경험치 같은 이상 탐지 조회
CREATE INDEX xp_ledger_char_idx ON xp_ledger (character_id, id);
CREATE TRIGGER xp_ledger_append_only BEFORE UPDATE OR DELETE ON xp_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER xp_ledger_no_truncate BEFORE TRUNCATE ON xp_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 4. 처치 보고 ----------

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
-- 속도 창 조회: "이 캐릭터의 최근 N초 처치 수" (WHERE character_id AND created_at > now() - 창). 키 순서가 조회 순서
CREATE INDEX kill_log_char_time ON kill_log (character_id, created_at);
-- 보관 기간이 지난 행을 지우는 정리 작업용
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

-- ---------- 5. 드롭 ----------

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
-- 캐릭터별 "미수령·유효" 드롭 수 상한 검사. 수령된 행은 인덱스에서 빠져 작게 유지된다
CREATE INDEX drops_char_open ON drops (character_id) WHERE claimed_at IS NULL;
-- 만료·수령된 행 정리 작업용
CREATE INDEX drops_expires_idx ON drops (expires_at);

-- ---------- 6. 채집 노드, 상자, 납품 ----------

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
-- 맵 입장 시 "아직 재생 중인 노드" 조회
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

-- ---------- 7. 퀘스트 청구 ----------

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

-- ---------- 8. 강화 ----------

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
-- 캐릭터별 강화 이력(시간순). 확률 편향 검사·분쟁 조사
CREATE INDEX enhance_log_char_idx ON enhance_log (character_id, id);
CREATE TRIGGER enhance_log_append_only BEFORE UPDATE OR DELETE ON enhance_log
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER enhance_log_no_truncate BEFORE TRUNCATE ON enhance_log
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- ---------- 9. 이상 기록 ----------

CREATE TABLE anomaly_log (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT REFERENCES characters(id),
  kind         TEXT NOT NULL CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                                             'gather_node', 'gather_early', 'drop_foreign', 'quest_denied', 'chest_unknown')),
  severity     SMALLINT NOT NULL DEFAULT 1 CHECK (severity BETWEEN 1 AND 3),
  detail       JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  anomaly_log IS '그럴듯하지 않아 지급을 막은 요청의 기록(PLAN_SERVER §5 "지급 보류 + 기록"). 지급을 미루지 않고 거절하며 기록만 남긴다. 제재는 7단계 관리자 도구가 이 표를 본다';
COMMENT ON COLUMN anomaly_log.kind IS 'kill_target 맵·몬스터 불일치 / kill_rate 1초 창 초과 / kill_supply 리스폰 공급 초과 / kill_power 화력 상한 초과 / gather_node 없는 노드 / gather_early 재생 전 채집 / drop_foreign 남의 드롭 id / quest_denied 근거 없는 퀘스트 청구 / chest_unknown 없는 상자';
COMMENT ON COLUMN anomaly_log.severity IS '1 참고(경계 오차 가능) / 2 의심 / 3 불가능한 값. 반복 횟수 기준 차단은 service 설정';
COMMENT ON COLUMN anomaly_log.detail IS '요청 값과 판정 근거(창 안 수, 한도, 맵·몬스터 등). 원본 요청 본문 전체는 넣지 않는다';
-- 캐릭터별 최근 이상 횟수(자동 차단 판정: 최근 10분 N회)
CREATE INDEX anomaly_log_char_time ON anomaly_log (character_id, created_at) WHERE character_id IS NOT NULL;
-- 보관 기간이 지난 행 정리, 관리자 도구의 최신순 조회
CREATE INDEX anomaly_log_created_idx ON anomaly_log (created_at);

-- ============ DOWN ============
-- 개발 DB 전용. 원장 행을 지우므로 골드·재고 잔액과 원장이 어긋난다. 운영에서는 실행하지 않는다.

DROP TABLE IF EXISTS anomaly_log;
DROP TABLE IF EXISTS enhance_log;
DROP TABLE IF EXISTS character_enhance_pity;
DROP TABLE IF EXISTS quest_claims;
DROP TABLE IF EXISTS site_deliveries;
DROP TABLE IF EXISTS character_chests;
DROP TABLE IF EXISTS character_node_state;
DROP TABLE IF EXISTS drops;
DROP TABLE IF EXISTS kill_stats;
DROP TABLE IF EXISTS kill_log;
DROP TABLE IF EXISTS xp_ledger;

DROP INDEX IF EXISTS character_items_stack_uq;
ALTER TABLE character_items DROP CONSTRAINT IF EXISTS character_items_no_gold;
COMMENT ON TABLE  character_items IS '캐릭터 소지품. 2단계는 시작 지급품(bag, worn)만 만든다. 변경은 item_ledger와 같은 트랜잭션에서만';
COMMENT ON COLUMN character_items.item_key IS '게임 데이터 아이템 키. 장비는 기본 id 또는 "id+강화단계"';
COMMENT ON COLUMN character_items.version IS '3단계 이후 낙관적 잠금용';

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason <> 'starter';
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
DROP INDEX IF EXISTS item_ledger_drop_uq;
DROP INDEX IF EXISTS item_ledger_request_idx;
ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_no_gold;
ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_balance_chk;
ALTER TABLE item_ledger DROP COLUMN IF EXISTS request_id;
ALTER TABLE item_ledger DROP COLUMN IF EXISTS balance_after;
ALTER TABLE item_ledger DROP COLUMN IF EXISTS location;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check CHECK (reason IN ('starter'));
COMMENT ON COLUMN item_ledger.reason IS '변동 사유. 3단계가 마이그레이션으로 추가한다';
COMMENT ON COLUMN item_ledger.ref IS NULL;

ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason <> 'starter';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
DROP INDEX IF EXISTS gold_ledger_drop_uq;
DROP INDEX IF EXISTS gold_ledger_request_idx;
ALTER TABLE gold_ledger DROP COLUMN IF EXISTS request_id;
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check CHECK (reason IN ('starter'));
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. 3단계가 kill, quest, shop_buy 등을 마이그레이션으로 추가한다';
COMMENT ON COLUMN gold_ledger.ref IS '사유별 참조(캐릭터 uuid, 퀘스트 id 등)';
