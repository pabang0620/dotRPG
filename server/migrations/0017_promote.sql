-- 0017_promote: 장비 승급과 레이드 전용 재료(고대의 핵)
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0016과 같다. 선행: 0015_starshop_gear, 0007_auction.
--
-- 이 마이그레이션이 하는 일
--   1. item_ledger 사유에 'raid_core'(레이드 보상 핵), 'promote_cost'(승급에 쓴 핵), 'promote_result'(승급 전후 장비)를 더한다.
--   2. gold_ledger 사유에 'promote_cost'(승급 골드)를 더한다.

-- ============ UP ============

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result'));

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost'));

-- ============ DOWN ============
-- 개발 DB 전용.

DELETE FROM item_ledger WHERE reason IN ('raid_core', 'promote_cost', 'promote_result');
DELETE FROM gold_ledger WHERE reason = 'promote_cost';
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim'));
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha'));
