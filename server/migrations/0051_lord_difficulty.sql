-- 0051_lord_difficulty(2026-10-22): 챕터 3 요일 던전 "군주" 난이도(번호 4)를 받도록 난이도 CHECK를 0..4로 넓힌다. 3장이 닫혀 있으면 서버(checkEntry)가 4를 거부한다.
-- (보스 시즌 레벨 마이그레이션은 0052로 미룬다. 0049는 출시용 장비 계승, 0050은 아카브 교환: Docs/ch3/IMPLEMENTATION_ORDER.md)
-- ============ UP ============
DO $$
DECLARE t TEXT; c TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['dungeon_runs', 'parties', 'party_runs', 'dungeon_sweeps'] LOOP
    FOR c IN SELECT conname FROM pg_constraint
              WHERE conrelid = t::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%difficulty%' LOOP
      EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', t, c);
    END LOOP;
    EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I CHECK (difficulty BETWEEN 0 AND 4)', t, t || '_difficulty_check');
  END LOOP;
END $$;
COMMENT ON COLUMN dungeon_runs.difficulty IS '0 일반 1 모험 2 왕 3 영웅 4 군주(챕터 3, 요일 던전만). 레이드는 0 레이드 1 영웅 레이드';
-- ============ DOWN ============
DELETE FROM dungeon_sweeps WHERE difficulty = 4;
UPDATE parties SET difficulty = 3 WHERE difficulty = 4;
DO $$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['dungeon_runs', 'parties', 'party_runs', 'dungeon_sweeps'] LOOP
    EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', t, t || '_difficulty_check');
    EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I CHECK (difficulty BETWEEN 0 AND 3) NOT VALID', t, t || '_difficulty_check');
  END LOOP;
END $$;
