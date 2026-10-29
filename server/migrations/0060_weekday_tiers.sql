-- 0060_weekday_tiers(2026-10-29): 요일 던전 난이도 악몽(4)·지옥(5)·심연(6)을 영웅(3)과 군주 사이에 넣는다(Docs/PLAN_WEEKDAY_TIERS_2026-10-29.md).
-- 군주는 4에서 7로 옮긴다. 난이도 CHECK를 0..7로 넓히고, 이미 저장된 군주 기록(4)을 7로 바꾼다.
-- 4는 지금까지 군주만 썼다(레이드는 0·1, 일반 던전은 0~3). 선행: 0052(0..4), 0059.
-- 대상: PostgreSQL 14 이상. 새 테이블·열은 없다.
-- ============ UP ============
DO $$
DECLARE t TEXT; c TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['dungeon_runs', 'parties', 'party_runs', 'dungeon_sweeps'] LOOP
    FOR c IN SELECT conname FROM pg_constraint
              WHERE conrelid = t::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%difficulty%' LOOP
      EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', t, c);
    END LOOP;
    EXECUTE format('UPDATE %I SET difficulty = 7 WHERE difficulty = 4', t);
    EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I CHECK (difficulty BETWEEN 0 AND 7)', t, t || '_difficulty_check');
  END LOOP;
END $$;
COMMENT ON COLUMN dungeon_runs.difficulty IS '0 일반 1 모험 2 왕 3 영웅 4 악몽 5 지옥 6 심연 7 군주(4~7은 요일 던전만, 7은 챕터 3). 레이드는 0 레이드 1 심화 레이드';
-- ============ DOWN ============
-- 악몽·지옥·심연 기록은 옛 번호에 자리가 없어 지운다(소탕·모집 글). 판 기록(dungeon_runs, party_runs)은 영웅(3)으로 내린다.
DELETE FROM dungeon_sweeps WHERE difficulty BETWEEN 4 AND 6;
UPDATE parties SET difficulty = 3 WHERE difficulty BETWEEN 4 AND 6;
UPDATE dungeon_runs SET difficulty = 3 WHERE difficulty BETWEEN 4 AND 6;
UPDATE party_runs SET difficulty = 3 WHERE difficulty BETWEEN 4 AND 6;
DO $$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['dungeon_runs', 'parties', 'party_runs', 'dungeon_sweeps'] LOOP
    EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', t, t || '_difficulty_check');
    EXECUTE format('UPDATE %I SET difficulty = 4 WHERE difficulty = 7', t);
    EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I CHECK (difficulty BETWEEN 0 AND 4)', t, t || '_difficulty_check');
  END LOOP;
END $$;
