-- 0053_boss_level(2026-10-23, 챕터 3 5단계 라이브 콘텐츠: Docs/ch3/LIVE_CONTENT.md 1·2절)
-- 1) 월드·길드보스 시즌에 보스 레벨을 기록한다. 시즌을 만들 때의 levelCap()(3장 닫힘 40, 열림 50)이고, 그 주 내내 이 값을 쓴다.
--    기존 시즌은 전부 Lv40 보스였으므로 기본값 40.
-- 2) 미니 던전 티어 40(잿빛 수련의 협곡, Lv40~49)을 받도록 티어·입장 레벨 CHECK를 넓힌다. 3장이 닫혀 있으면 서버가 입장을 거부한다.
-- ============ UP ============
ALTER TABLE boss_seasons ADD COLUMN boss_level INT NOT NULL DEFAULT 40 CHECK (boss_level BETWEEN 1 AND 100);
COMMENT ON COLUMN boss_seasons.boss_level IS '그 주 보스 레벨(시즌 생성 때 levelCap). 50이면 월드보스 체력 x2.0';
DO $$
DECLARE c TEXT;
BEGIN
  FOR c IN SELECT conname FROM pg_constraint
            WHERE conrelid = 'mini_dungeon_runs'::regclass AND contype = 'c'
              AND (pg_get_constraintdef(oid) LIKE '%tier = ANY%' OR pg_get_constraintdef(oid) LIKE '%tier IN%'
                   OR pg_get_constraintdef(oid) LIKE '%entry_level >= 10%') LOOP
    EXECUTE format('ALTER TABLE mini_dungeon_runs DROP CONSTRAINT %I', c);
  END LOOP;
END $$;
ALTER TABLE mini_dungeon_runs ADD CONSTRAINT mini_dungeon_runs_tier_check CHECK (tier IN (10, 20, 30, 40));
ALTER TABLE mini_dungeon_runs ADD CONSTRAINT mini_dungeon_runs_entry_level_check CHECK (entry_level BETWEEN 10 AND 49);
-- ============ DOWN ============
-- 티어 40 기록이 있으면 되돌릴 수 없다(기록 보존). 보스 레벨 열은 지운다.
ALTER TABLE mini_dungeon_runs DROP CONSTRAINT mini_dungeon_runs_tier_check;
ALTER TABLE mini_dungeon_runs DROP CONSTRAINT mini_dungeon_runs_entry_level_check;
ALTER TABLE mini_dungeon_runs ADD CONSTRAINT mini_dungeon_runs_tier_check CHECK (tier IN (10, 20, 30));
ALTER TABLE mini_dungeon_runs ADD CONSTRAINT mini_dungeon_runs_entry_level_check CHECK (entry_level BETWEEN 10 AND 39);
ALTER TABLE boss_seasons DROP COLUMN boss_level;
