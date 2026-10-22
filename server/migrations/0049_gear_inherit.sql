-- 0049_gear_inherit: 장비 계승(2026-10-22, Docs/PLAN_GEAR_INHERIT_2026-10-22.md). 선행: 0048.
--   1. 골드·아이템 원장 사유 inherit(계승 비용, 옛 장비 -1 / 새 장비 +1).
--   2. 계승한 장비 키는 "id+강화^계승횟수"("eq_sword_30_r+12^2", 계승하면 +0도 적는다). 경매 등록 키 형식 검사를 넓힌다.
-- 주의: ch3 브랜치의 0049_chapter3_inherit와 번호가 겹친다. ch3을 합칠 때 번호를 다시 매기고 원장 사유 목록을 하나로 합친다.
-- ============ UP ============
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in', 'guild_create',
                    'inherit'));
COMMENT ON COLUMN gold_ledger.reason IS '0047의 값 + inherit(장비 계승 비용)';

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
                    'inherit'));
COMMENT ON COLUMN item_ledger.reason IS '0044의 값 + inherit(장비 계승: 옛 장비 -1, 새 장비 +1)';

DO $$
DECLARE c TEXT;
BEGIN
  SELECT conname INTO c FROM pg_constraint
   WHERE conrelid = 'auction_listings'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%item_key ~%';
  IF c IS NOT NULL THEN EXECUTE format('ALTER TABLE auction_listings DROP CONSTRAINT %I', c); END IF;
END $$;
ALTER TABLE auction_listings ADD CONSTRAINT auction_listings_item_key_check
  CHECK (item_key ~ '^[a-z][a-z0-9_]{0,39}(\+[1-9][0-9]?|\+(0|[1-9][0-9]?)\^[1-9])?$' AND item_key <> 'gold');
COMMENT ON COLUMN auction_listings.item_key IS '아이템 키: id, id+강화, 계승한 장비는 id+강화^계승횟수';
-- ============ DOWN ============
-- 계승한 장비 키가 경매에 걸려 있거나 원장에 inherit 사유가 생긴 뒤에는 되돌리지 않는다(원장은 추가만).
ALTER TABLE auction_listings DROP CONSTRAINT auction_listings_item_key_check;
ALTER TABLE auction_listings ADD CONSTRAINT auction_listings_item_key_check
  CHECK (item_key ~ '^[a-z][a-z0-9_]{0,39}(\+[1-9][0-9]?)?$' AND item_key <> 'gold');
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
                    'gear_dismantle', 'dismantle_bonus', 'exchange_buy'));
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in', 'guild_create'));
