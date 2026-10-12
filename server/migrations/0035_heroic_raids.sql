-- 0035_heroic_raids: 영웅 레이드(레이드 난이도 1, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md D)
-- 대상: PostgreSQL 14 이상. 선행: 0034.
--   raid_claims에 난이도를 넣고 PK에 포함한다. 영웅 레이드 보상은 주 1회이고 일반 레이드 보상과 따로 센다
--   (중간 레이드의 일일 기간 시작과 주간 기간 시작이 목요일 06:00에 겹치므로 난이도 없이는 PK가 충돌한다).
-- 기존 행은 모두 난이도 0(일반 레이드)이다.

-- ============ UP ============
ALTER TABLE raid_claims ADD COLUMN difficulty SMALLINT NOT NULL DEFAULT 0 CHECK (difficulty IN (0, 1));
ALTER TABLE raid_claims DROP CONSTRAINT raid_claims_pkey;
ALTER TABLE raid_claims ADD PRIMARY KEY (character_id, dungeon_id, difficulty, period_start);
COMMENT ON COLUMN raid_claims.difficulty IS '0 레이드 / 1 영웅 레이드(주 1회, dungeons.json raidHeroic)';

-- ============ DOWN ============
-- 영웅 레이드 청구 행을 지운다(개발 DB 전용).
DELETE FROM raid_claims WHERE difficulty <> 0;
ALTER TABLE raid_claims DROP CONSTRAINT raid_claims_pkey;
ALTER TABLE raid_claims ADD PRIMARY KEY (character_id, dungeon_id, period_start);
ALTER TABLE raid_claims DROP COLUMN difficulty;
