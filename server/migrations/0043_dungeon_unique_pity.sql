-- 0043_dungeon_unique_pity: 요일 던전 유니크 불운 보정(2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0042.
--   요일 던전을 클리어할 때마다 22% 확률로 운 카드 한 장이 유니크가 된다. 유니크 없이 지난 판 수를 캐릭터마다 세어
--   8판째(UNIQUE_BONUS.pityRuns)는 반드시 유니크가 나온다. 나오면 0으로 돌아간다.

-- ============ UP ============
CREATE TABLE dungeon_unique_pity (
  character_id BIGINT PRIMARY KEY REFERENCES characters(id),
  misses       INT NOT NULL DEFAULT 0 CHECK (misses >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE dungeon_unique_pity IS '요일 던전 유니크 불운 보정(0043). misses = 유니크 없이 지난 클리어 수, 8판째 보장';

-- ============ DOWN ============
DROP TABLE IF EXISTS dungeon_unique_pity;
