-- 0056_revive_coin_item: 부활 코인 아이템·구매, 보유 상한 100 (Docs/server/phase12_revive_coins.md 5절)
-- 대상: PostgreSQL 14 이상. 선행: 0055_daily_chests.
--   1. characters.revive_coins 보유 상한 5 -> 100. 일일 무료 지급은 여전히 5개까지만 채운다(서비스 REVIVE_DAILY_MAX)
--   2. revive_log.context 에 item(가방의 부활 코인 아이템 1개 -> 캐릭터 부활 코인 +1, free = false, coins_after = 쓴 뒤) 추가
--   3. star_ledger.reason 에 item_buy(별조각으로 산 가방 아이템, ref = 상품 id) 추가
--   4. item_ledger.reason 에 star_item_buy(별조각 상품 지급, ref = 상품 id) 추가

-- ============ UP ============
ALTER TABLE characters DROP CONSTRAINT IF EXISTS characters_revive_coins_check;
ALTER TABLE characters ADD CONSTRAINT characters_revive_coins_check CHECK (revive_coins BETWEEN 0 AND 100);
COMMENT ON COLUMN characters.revive_coins IS '부활 코인 보유 수(0~100). 일일 지급은 조회·사용 시점에 반영하고 5개까지만 채운다. 아이템(item_revive_coin)을 쓰면 100까지';

ALTER TABLE revive_log DROP CONSTRAINT IF EXISTS revive_log_context_check;
ALTER TABLE revive_log ADD CONSTRAINT revive_log_context_check CHECK (context IN ('field', 'dungeon', 'item'));
COMMENT ON COLUMN revive_log.context IS 'field 사냥터 부활 / dungeon 던전·레이드 부활 / item 부활 코인 아이템 사용(코인 +1)';

ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);

ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward',
                    'raid_shop_cost', 'raid_shop_result', 'trade_out', 'trade_in',
                    'gear_dismantle', 'dismantle_bonus', 'exchange_buy',
                    'inherit', 'daily_chest', 'fragment_merge', 'star_item_buy'));
COMMENT ON COLUMN item_ledger.reason IS '0055의 값 + star_item_buy(별조각 상품 지급, ref = 상품 id, request_id = 구매 요청)';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0055 DOWN과 같은 방식).
-- 5개를 넘게 가진 캐릭터는 5로 줄인 뒤 상한을 되돌린다.
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason = 'star_item_buy';
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
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward',
                    'raid_shop_cost', 'raid_shop_result', 'trade_out', 'trade_in',
                    'gear_dismantle', 'dismantle_bonus', 'exchange_buy',
                    'inherit', 'daily_chest', 'fragment_merge'));
ALTER TABLE star_spend_allocs DISABLE TRIGGER star_spend_allocs_append_only;
DELETE FROM star_spend_allocs WHERE ledger_id IN (SELECT id FROM star_ledger WHERE reason = 'item_buy');
ALTER TABLE star_spend_allocs ENABLE TRIGGER star_spend_allocs_append_only;
ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason = 'item_buy';
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
DELETE FROM revive_log WHERE context = 'item';
ALTER TABLE revive_log DROP CONSTRAINT IF EXISTS revive_log_context_check;
ALTER TABLE revive_log ADD CONSTRAINT revive_log_context_check CHECK (context IN ('field', 'dungeon'));
UPDATE characters SET revive_coins = 5 WHERE revive_coins > 5;
ALTER TABLE characters DROP CONSTRAINT IF EXISTS characters_revive_coins_check;
ALTER TABLE characters ADD CONSTRAINT characters_revive_coins_check CHECK (revive_coins BETWEEN 0 AND 5);
