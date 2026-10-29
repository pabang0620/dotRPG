-- 0057_gear_hidden_option: 장비 히든 옵션(2026-10-29, Docs/PLAN_GEAR_HIDDEN_2026-10-29.md). 선행: 0056.
--   히든 옵션이 붙은 장비 키는 맨 뒤에 "~옵션 글자"가 붙고 +0도 적는다("eq_sword_20_x+0~d", "eq_sword_30_x+12^2~c").
--   새 테이블·열은 없다. 경매 등록 키 형식 검사만 넓힌다(가방·원장·우편·드롭의 item_key 열은 형식 검사가 없다).
-- ============ UP ============
DO $$
DECLARE c TEXT;
BEGIN
  SELECT conname INTO c FROM pg_constraint
   WHERE conrelid = 'auction_listings'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%item_key ~%';
  IF c IS NOT NULL THEN EXECUTE format('ALTER TABLE auction_listings DROP CONSTRAINT %I', c); END IF;
END $$;
ALTER TABLE auction_listings ADD CONSTRAINT auction_listings_item_key_check
  CHECK (item_key ~ '^[a-z][a-z0-9_]{0,39}(\+[1-9][0-9]?|\+(0|[1-9][0-9]?)\^[1-9]|\+(0|[1-9][0-9]?)(\^[1-9])?~[a-z])?$' AND item_key <> 'gold');
COMMENT ON COLUMN auction_listings.item_key IS '아이템 키: id, id+강화, 계승한 장비는 id+강화^계승횟수, 히든 옵션 장비는 끝에 ~옵션 글자';
-- ============ DOWN ============
-- 히든 옵션 키가 경매에 걸린 뒤에는 되돌리지 않는다.
ALTER TABLE auction_listings DROP CONSTRAINT auction_listings_item_key_check;
ALTER TABLE auction_listings ADD CONSTRAINT auction_listings_item_key_check
  CHECK (item_key ~ '^[a-z][a-z0-9_]{0,39}(\+[1-9][0-9]?|\+(0|[1-9][0-9]?)\^[1-9])?$' AND item_key <> 'gold');
