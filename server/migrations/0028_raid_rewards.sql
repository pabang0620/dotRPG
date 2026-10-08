-- 0028_raid_rewards: 레이드 보상 개편(확정 골드, 카드 4장 모두 수령, 전용 재료, 레이드 상점) (Docs/server/phase13_raid_rewards.md 6.2)
-- 대상: PostgreSQL 14 이상. 선행: 0027_withdrawal.
--   1. dungeon_runs: cards_mode, cards_taken, cards_taken_at, raid_gold + 대기 카드 부분 인덱스
--   2. raid_shop_purchases: 레이드 상점 구매 기록(추가만)
--   3. gold_ledger.reason 에 raid_gold, item_ledger.reason 에 raid_shop_cost, raid_shop_result 추가

-- ============ UP ============
-- 1. 던전 판: 카드를 받는 방식, 받은 카드 집합, 확정 골드
ALTER TABLE dungeon_runs
  ADD COLUMN cards_mode     TEXT NOT NULL DEFAULT 'pick_one' CHECK (cards_mode IN ('pick_one', 'take_all')),
  ADD COLUMN cards_taken    SMALLINT NOT NULL DEFAULT 0 CHECK (cards_taken BETWEEN 0 AND 15),
  ADD COLUMN cards_taken_at TIMESTAMPTZ,
  ADD COLUMN raid_gold      INT NOT NULL DEFAULT 0 CHECK (raid_gold >= 0);
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_taken_chk CHECK ((cards_taken = 0) = (cards_taken_at IS NULL));
ALTER TABLE dungeon_runs ADD CONSTRAINT dungeon_runs_mode_chk
  CHECK (cards_mode = 'pick_one' OR (card_picked IS NULL AND card_picked_at IS NULL));
COMMENT ON COLUMN dungeon_runs.cards_mode IS 'pick_one 4장 중 1장(요일던전, 이 마이그레이션 전의 레이드 판) / take_all 4장 모두 받기(레이드). 클리어 확정 때 정해진다';
COMMENT ON COLUMN dungeon_runs.cards_taken IS 'take_all에서 이미 가방에 준 카드의 비트집합(비트 i = 카드 i). 15 = 4장 모두. 카드 수가 4가 아니게 바뀌면 이 열과 부분 인덱스를 함께 바꾼다';
COMMENT ON COLUMN dungeon_runs.cards_taken_at IS '마지막으로 카드를 준 시각';
COMMENT ON COLUMN dungeon_runs.raid_gold IS '클리어 확정 골드(레이드). gold_ledger raid_gold 행과 같은 값. 잠긴 판과 요일던전은 0';

-- 대기 카드 조회: 서버 틱(주기적으로 전체 스캔)과 접속 때 목록. 끝난 지 오래된 것부터 읽는다
CREATE INDEX dungeon_runs_cards_pending ON dungeon_runs (ended_at)
  WHERE state = 'cleared' AND cards IS NOT NULL
    AND ((cards_mode = 'pick_one' AND card_picked IS NULL) OR (cards_mode = 'take_all' AND cards_taken <> 15));

-- 2. 레이드 상점 구매 기록 (추가만, 확률 공시와 분쟁 조사용)
CREATE TABLE raid_shop_purchases (
  id            BIGSERIAL PRIMARY KEY,
  uuid          UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  request_id    UUID NOT NULL,
  product_id    TEXT NOT NULL,
  material_key  TEXT NOT NULL,
  material_cost INT NOT NULL CHECK (material_cost > 0),
  result_key    TEXT NOT NULL,
  result_rarity TEXT NOT NULL CHECK (result_rarity IN ('Epic', 'Unique', 'Legendary')),
  rates_version TEXT NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id)
);
CREATE INDEX raid_shop_purchases_char_idx ON raid_shop_purchases (character_id, id DESC);
COMMENT ON TABLE raid_shop_purchases IS '레이드 상점 구매 기록. 재료 차감과 장비 지급은 item_ledger(raid_shop_cost, raid_shop_result)가 정본이고 이 표는 어떤 상품에서 어떤 확률표로 뭐가 나왔는지 남긴다';

-- 3. 원장 사유 확장
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold'));
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward',
                    'raid_shop_cost', 'raid_shop_result'));
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. raid_gold 레이드 클리어 확정 골드(ref = 던전 판 uuid). 그 밖의 설명은 0002, 0007, 0021 참고';
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). dungeon_card 카드 지급(레이드는 ref = 판 uuid:카드번호) / raid_shop_cost 레이드 상점 재료 차감 / raid_shop_result 레이드 상점 장비 지급. 그 밖의 설명은 0002, 0007, 0019, 0020, 0026 참고';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0026 DOWN과 같은 방식). take_all 판에 이미 지급한 카드는 되돌리지 않는다.
ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason = 'raid_gold';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy'));
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason IN ('raid_shop_cost', 'raid_shop_result');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward'));
DROP TABLE IF EXISTS raid_shop_purchases;
DROP INDEX IF EXISTS dungeon_runs_cards_pending;
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_mode_chk;
ALTER TABLE dungeon_runs DROP CONSTRAINT IF EXISTS dungeon_runs_taken_chk;
ALTER TABLE dungeon_runs
  DROP COLUMN IF EXISTS raid_gold,
  DROP COLUMN IF EXISTS cards_taken_at,
  DROP COLUMN IF EXISTS cards_taken,
  DROP COLUMN IF EXISTS cards_mode;
