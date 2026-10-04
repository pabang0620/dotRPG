-- 0011_test_items: 시험 서버에서 시험 캐릭터에게 아이템을 넣은 기록을 아이템 원장에 남긴다
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0010과 같다. 선행: 0008_ops.
--
-- 이 마이그레이션이 하는 일
--   1. item_ledger.reason에 test_boost를 추가한다. scripts/test-give.ts만 쓰고, 운영 서버(DEPLOY_STAGE=live)에서는 스크립트가 거절한다.

-- ============ UP ============

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost'));

-- ============ DOWN ============
-- 개발 DB 전용.

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason = 'test_boost';
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant'));
