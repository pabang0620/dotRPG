-- 0012_achievements: 업적과 칭호
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0011과 같다. 선행: 0002_economy, 0006_chat_social.
--
-- 이 마이그레이션이 하는 일
--   1. character_achievements: 캐릭터가 달성한 업적(추가만). 달성 판정은 서버가 기존 기록(enhance_log, kill_log,
--      dungeon_runs, quest_claims, characters)으로 한다. 업적 정의는 코드(achievementDefs.ts)에 있다.
--   2. characters.title_achievement: 장착한 칭호(달성한 업적 id 중 하나, 없으면 NULL).
--   3. chat_messages.sender_title: 보낼 때의 칭호 이름 스냅샷(지난 대화에도 그때 칭호가 보인다).

-- ============ UP ============

CREATE TABLE character_achievements (
  character_id   BIGINT NOT NULL REFERENCES characters(id),
  achievement_id TEXT NOT NULL CHECK (char_length(achievement_id) BETWEEN 1 AND 40),
  achieved_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, achievement_id)
);
COMMENT ON TABLE character_achievements IS '달성한 업적. 한 번 달성하면 남는다(조건 값이 나중에 줄어도 지우지 않는다)';

ALTER TABLE characters ADD COLUMN title_achievement TEXT;
COMMENT ON COLUMN characters.title_achievement IS '장착한 칭호의 업적 id. 서버가 달성 여부를 확인하고 바꾼다';

ALTER TABLE chat_messages ADD COLUMN sender_title TEXT;
COMMENT ON COLUMN chat_messages.sender_title IS '보낼 때 장착한 칭호 이름 스냅샷';

-- ============ DOWN ============
-- 개발 DB 전용.

ALTER TABLE chat_messages DROP COLUMN IF EXISTS sender_title;
ALTER TABLE characters DROP COLUMN IF EXISTS title_achievement;
DROP TABLE IF EXISTS character_achievements;
