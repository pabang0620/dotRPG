-- 0015_starshop_gear: 별조각 뽑기 등급 개편(전설 -> 유니크)과 장비 뽑기(무기·방어구·장신구)
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0014와 같다. 선행: 0013_starshop, 0011_test_items.
--
-- 이 마이그레이션이 하는 일
--   1. gacha_pulls.rarity: 최상위 등급 이름을 'unique'로 바꾼다(기존 'legend' 행도 옮긴다). 전설 등급은 아직 열지 않는다.
--   2. gacha_pulls.kind / banner: 당첨 종류('cosmetic' 외형 / 'gear' 장비)와 뽑기 종류(aura·weapon·armor·accessory).
--      장비는 item_id에 아이템 키, rarity에 아이템 등급(common~legendary)이 들어간다.
--   3. item_ledger 사유에 'gacha'(뽑기로 받은 장비)를 더한다.

-- ============ UP ============

ALTER TABLE gacha_pulls DROP CONSTRAINT IF EXISTS gacha_pulls_rarity_check;
UPDATE gacha_pulls SET rarity = 'unique' WHERE rarity = 'legend';
ALTER TABLE gacha_pulls ADD CONSTRAINT gacha_pulls_rarity_check
  CHECK (rarity IN ('common', 'uncommon', 'rare', 'epic', 'unique', 'legendary'));
ALTER TABLE gacha_pulls ADD COLUMN kind TEXT NOT NULL DEFAULT 'cosmetic' CHECK (kind IN ('cosmetic', 'gear'));
ALTER TABLE gacha_pulls ADD COLUMN banner TEXT NOT NULL DEFAULT 'aura' CHECK (banner IN ('aura', 'weapon', 'armor', 'accessory'));
COMMENT ON COLUMN gacha_pulls.kind IS '당첨 종류: cosmetic(외형, item_id = 외형 id) / gear(장비, item_id = 아이템 키, 가방으로 지급)';

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha'));

-- ============ DOWN ============
-- 개발 DB 전용.

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost'));
ALTER TABLE gacha_pulls DROP COLUMN IF EXISTS banner;
ALTER TABLE gacha_pulls DROP COLUMN IF EXISTS kind;
ALTER TABLE gacha_pulls DROP CONSTRAINT IF EXISTS gacha_pulls_rarity_check;
DELETE FROM gacha_pulls WHERE kind = 'gear';
UPDATE gacha_pulls SET rarity = 'legend' WHERE rarity = 'unique';
ALTER TABLE gacha_pulls ADD CONSTRAINT gacha_pulls_rarity_check CHECK (rarity IN ('common', 'rare', 'legend'));
