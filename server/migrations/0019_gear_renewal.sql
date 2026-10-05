-- 0019_gear_renewal: 장비 전면 개편(Docs/PLAN_GEAR_RENEWAL.md). 옛 장비 17종을 새 레벨 단계 장비로 바꾼다
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0018과 같다. 선행: 0017_promote.
--
-- 이 마이그레이션이 하는 일
--   1. item_ledger 사유에 'gear_renewal'(옛 키 -n, 새 키 +n 쌍)을 더한다.
--   2. 대응표(C# GearCatalog.Legacy와 같다)로 옛 장비 키를 새 키로 바꾼다. 방어구는 그 캐릭터의 직업 재질(전사 갑옷·각반,
--      마법사 로브·치마). 강화 수치(+n)는 그대로 둔다.
--      바꾸는 곳: character_items(가방·창고·착용·우편·경매 보관) + 원장, drops(줍기 전), auction_listings, mails,
--      character_enhance_pity, 아직 고르지 않은 dungeon_runs.cards. 기록용 표(gacha_pulls, auction_trades, quest_claims)는 그대로.

-- ============ UP ============

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal'));

CREATE TEMP TABLE gear_map (old_id TEXT, cls TEXT, new_id TEXT) ON COMMIT DROP;
INSERT INTO gear_map (old_id, cls, new_id)
SELECT o, c, n FROM (VALUES
  ('eq_sword_iron', NULL, 'eq_sword_10_u'), ('eq_sword_bone', NULL, 'eq_sword_15_e'), ('eq_sword_dragon', NULL, 'eq_sword_20_l'),
  ('eq_staff_crystal', NULL, 'eq_staff_10_u'), ('eq_staff_moon', NULL, 'eq_staff_15_e'), ('eq_staff_star', NULL, 'eq_staff_20_l'),
  ('eq_neck_leaf', NULL, 'eq_neck_1_c'), ('eq_neck_bone', NULL, 'eq_neck_10_r'), ('eq_neck_king', NULL, 'eq_neck_20_un'),
  ('eq_ring_copper', NULL, 'eq_ring_1_c'), ('eq_ring_wind', NULL, 'eq_ring_10_r'), ('eq_ring_ruby', NULL, 'eq_ring_10_un'),
  ('eq_top_cloth', 'warrior', 'eq_plate_1_c'), ('eq_top_cloth', 'mage', 'eq_robe_1_c'),
  ('eq_top_leather', 'warrior', 'eq_plate_1_u'), ('eq_top_leather', 'mage', 'eq_robe_1_u'),
  ('eq_top_iron', 'warrior', 'eq_plate_10_e'), ('eq_top_iron', 'mage', 'eq_robe_10_e'),
  ('eq_bot_cloth', 'warrior', 'eq_greaves_1_c'), ('eq_bot_cloth', 'mage', 'eq_skirt_1_c'),
  ('eq_bot_leather', 'warrior', 'eq_greaves_1_u'), ('eq_bot_leather', 'mage', 'eq_skirt_1_u')
) AS v(o, c, n);

-- 키 하나("eq_top_iron+12")를 그 캐릭터 직업 기준 새 키("eq_plate_10_e+12")로. 대응이 없으면 NULL
CREATE FUNCTION pg_temp.renew_key(k TEXT, cls TEXT) RETURNS TEXT LANGUAGE sql AS $$
  SELECT m.new_id || COALESCE(substring(k FROM '(\+[0-9]+)$'), '')
    FROM gear_map m
   WHERE m.old_id = split_part(k, '+', 1) AND (m.cls IS NULL OR m.cls = cls)
   LIMIT 1
$$;

-- 1) 소지품: 바꿀 행과 새 키
CREATE TEMP TABLE gear_moves ON COMMIT DROP AS
SELECT ci.id, ci.character_id, ci.location, ci.slot, ci.bind, ci.count, ci.item_key AS old_key,
       pg_temp.renew_key(ci.item_key, c.class) AS new_key
  FROM character_items ci JOIN characters c ON c.id = ci.character_id
 WHERE pg_temp.renew_key(ci.item_key, c.class) IS NOT NULL;

-- 원장: (캐릭터, 위치, 옛 키)마다 -합계(이후 0), (캐릭터, 위치, 새 키)마다 +합계(이후 기존 수량 + 합계)
INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after)
SELECT character_id, old_key, -SUM(count), 'gear_renewal', '0019', location, 0
  FROM gear_moves GROUP BY character_id, location, old_key;
INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after)
SELECT g.character_id, g.new_key, SUM(g.count), 'gear_renewal', '0019', g.location,
       SUM(g.count) + COALESCE((SELECT SUM(x.count) FROM character_items x
                                 WHERE x.character_id = g.character_id AND x.location = g.location AND x.item_key = g.new_key), 0)
  FROM gear_moves g GROUP BY g.character_id, g.location, g.new_key;

-- 행 바꾸기: 같은 (캐릭터, 위치, 새 키, 귀속) 행이 이미 있으면 거기에 합치고, 없으면 키만 바꾼다(착용은 칸이 달라 합치지 않는다)
UPDATE character_items t SET count = t.count + s.total
  FROM (SELECT character_id, location, new_key, bind, SUM(count) AS total FROM gear_moves WHERE location <> 'worn'
         GROUP BY character_id, location, new_key, bind) s
 WHERE t.character_id = s.character_id AND t.location = s.location AND t.item_key = s.new_key AND t.bind = s.bind;
DELETE FROM character_items d USING gear_moves g
 WHERE d.id = g.id AND g.location <> 'worn'
   AND EXISTS (SELECT 1 FROM character_items t WHERE t.character_id = g.character_id AND t.location = g.location
                  AND t.item_key = g.new_key AND t.bind = g.bind AND t.id <> g.id);
UPDATE character_items t SET item_key = g.new_key FROM gear_moves g WHERE t.id = g.id;

-- 2) 줍기 전 드롭, 경매 등록, 우편, 강화 천장
UPDATE drops d SET item_key = pg_temp.renew_key(d.item_key, c.class)
  FROM characters c WHERE c.id = d.character_id AND pg_temp.renew_key(d.item_key, c.class) IS NOT NULL;
UPDATE auction_listings a SET item_key = pg_temp.renew_key(a.item_key, c.class)
  FROM characters c WHERE c.id = a.seller_character_id AND pg_temp.renew_key(a.item_key, c.class) IS NOT NULL;
UPDATE mails m SET item_key = pg_temp.renew_key(m.item_key, c.class)
  FROM characters c WHERE c.id = m.character_id AND m.item_key IS NOT NULL AND pg_temp.renew_key(m.item_key, c.class) IS NOT NULL;
UPDATE mails m SET ref_item_key = pg_temp.renew_key(m.ref_item_key, c.class)
  FROM characters c WHERE c.id = m.character_id AND m.ref_item_key IS NOT NULL AND pg_temp.renew_key(m.ref_item_key, c.class) IS NOT NULL;
DELETE FROM character_enhance_pity p USING characters c
 WHERE c.id = p.character_id AND pg_temp.renew_key(p.item_key, c.class) IS NOT NULL
   AND EXISTS (SELECT 1 FROM character_enhance_pity q WHERE q.character_id = p.character_id AND q.item_key = pg_temp.renew_key(p.item_key, c.class));
UPDATE character_enhance_pity p SET item_key = pg_temp.renew_key(p.item_key, c.class)
  FROM characters c WHERE c.id = p.character_id AND pg_temp.renew_key(p.item_key, c.class) IS NOT NULL;

-- 3) 아직 고르지 않은 던전 카드
UPDATE dungeon_runs r SET cards = (
  SELECT jsonb_agg(CASE WHEN pg_temp.renew_key(e->>'item_key', c.class) IS NULL THEN e
                        ELSE jsonb_set(e, '{item_key}', to_jsonb(pg_temp.renew_key(e->>'item_key', c.class))) END)
    FROM jsonb_array_elements(r.cards) e)
  FROM characters c
 WHERE c.id = r.character_id AND r.cards IS NOT NULL AND jsonb_array_length(r.cards) > 0
   AND EXISTS (SELECT 1 FROM jsonb_array_elements(r.cards) e WHERE pg_temp.renew_key(e->>'item_key', c.class) IS NOT NULL);

-- ============ DOWN ============
-- 개발 DB 전용. 키 변환은 되돌리지 않는다(새 장비만 게임 데이터에 있다). 원장 사유 제약만 앞 단계로 돌린다.

ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal'));
