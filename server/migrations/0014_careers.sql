-- ============ UP ============
ALTER TABLE character_state ADD COLUMN career jsonb NULL;
-- ============ DOWN ============
ALTER TABLE character_state DROP COLUMN career;
