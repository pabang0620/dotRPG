-- 0016_starshop_skin: 스킨 뽑기
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0015와 같다. 선행: 0015_starshop_gear.
--
-- 이 마이그레이션이 하는 일
--   1. star_wallets.skin_pity: 스킨 뽑기 천장 진행도(오라 뽑기 천장과 따로 센다).
--   2. gacha_pulls.banner에 'skin'을 더한다.

-- ============ UP ============

ALTER TABLE star_wallets ADD COLUMN skin_pity INT NOT NULL DEFAULT 0 CHECK (skin_pity >= 0);
COMMENT ON COLUMN star_wallets.skin_pity IS '스킨 뽑기에서 스킨 없이 뽑은 횟수(천장 진행도)';
ALTER TABLE gacha_pulls DROP CONSTRAINT IF EXISTS gacha_pulls_banner_check;
ALTER TABLE gacha_pulls ADD CONSTRAINT gacha_pulls_banner_check CHECK (banner IN ('aura', 'weapon', 'armor', 'accessory', 'skin'));

-- ============ DOWN ============
-- 개발 DB 전용.

DELETE FROM gacha_pulls WHERE banner = 'skin';
ALTER TABLE gacha_pulls DROP CONSTRAINT IF EXISTS gacha_pulls_banner_check;
ALTER TABLE gacha_pulls ADD CONSTRAINT gacha_pulls_banner_check CHECK (banner IN ('aura', 'weapon', 'armor', 'accessory'));
ALTER TABLE star_wallets DROP COLUMN IF EXISTS skin_pity;
