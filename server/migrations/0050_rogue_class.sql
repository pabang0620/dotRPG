-- 0050_rogue_class: 세 번째 기본 직업 도적(rogue)과 전직 5 어쌔신·6 닌자 (2026-10-23, Docs/PLAN_CLASS_ROGUE.md 5.1절). 선행: 0049.
--   1. characters.class 에 rogue 허용
--   2. auction_listings.class_only 에 rogue 허용(단검은 도적 전용)
--   3. character_career.career, character_career_trials.career 를 1~6 으로(5 어쌔신, 6 닌자)
-- 스킨·외형은 star_cosmetics 등에 직업 CHECK가 없어(아이템 id만 길이 검사) 바꿀 것이 없다.
-- 주의: ch3 브랜치의 0050·0051은 도적을 병합할 때 0051·0052로 번호를 미룬다(설계 5.1절).
-- ============ UP ============
ALTER TABLE characters DROP CONSTRAINT IF EXISTS characters_class_check;
ALTER TABLE characters ADD CONSTRAINT characters_class_check CHECK (class IN ('warrior', 'mage', 'rogue'));
COMMENT ON COLUMN characters.class IS 'CharacterClass: warrior | mage | rogue';

ALTER TABLE auction_listings DROP CONSTRAINT IF EXISTS auction_listings_class_only_check;
ALTER TABLE auction_listings ADD CONSTRAINT auction_listings_class_only_check CHECK (class_only IN ('warrior', 'mage', 'rogue'));

ALTER TABLE character_career DROP CONSTRAINT IF EXISTS character_career_career_check;
ALTER TABLE character_career ADD CONSTRAINT character_career_career_check CHECK (career BETWEEN 1 AND 6);

ALTER TABLE character_career_trials DROP CONSTRAINT IF EXISTS character_career_trials_career_check;
ALTER TABLE character_career_trials ADD CONSTRAINT character_career_trials_career_check CHECK (career BETWEEN 1 AND 6);

-- ============ DOWN ============
-- 도적 캐릭터·도적 전직(5·6)·도적 전용 경매 매물이 하나도 없을 때만 되돌린다(있으면 CHECK 추가가 실패한다).
ALTER TABLE character_career_trials DROP CONSTRAINT character_career_trials_career_check;
ALTER TABLE character_career_trials ADD CONSTRAINT character_career_trials_career_check CHECK (career BETWEEN 1 AND 4);
ALTER TABLE character_career DROP CONSTRAINT character_career_career_check;
ALTER TABLE character_career ADD CONSTRAINT character_career_career_check CHECK (career BETWEEN 1 AND 4);
ALTER TABLE auction_listings DROP CONSTRAINT auction_listings_class_only_check;
ALTER TABLE auction_listings ADD CONSTRAINT auction_listings_class_only_check CHECK (class_only IN ('warrior', 'mage'));
ALTER TABLE characters DROP CONSTRAINT characters_class_check;
ALTER TABLE characters ADD CONSTRAINT characters_class_check CHECK (class IN ('warrior', 'mage'));
COMMENT ON COLUMN characters.class IS 'CharacterClass: warrior | mage';
