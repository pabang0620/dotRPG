-- XP-only solo training instances. Time is shared by all tiers on one character.
-- ============ UP ============
CREATE TABLE mini_dungeon_days (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  reset_day TIMESTAMPTZ NOT NULL,
  used_ms INT NOT NULL DEFAULT 0 CHECK (used_ms BETWEEN 0 AND 1800000),
  PRIMARY KEY (character_id, reset_day)
);
CREATE TABLE mini_dungeon_runs (
  id BIGSERIAL PRIMARY KEY,
  uuid UUID NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  reset_day TIMESTAMPTZ NOT NULL,
  tier SMALLINT NOT NULL CHECK (tier IN (10, 20, 30)),
  entry_level SMALLINT NOT NULL CHECK (entry_level BETWEEN 10 AND 39),
  xp_per_kill INT NOT NULL CHECK (xp_per_kill > 0),
  state TEXT NOT NULL DEFAULT 'playing' CHECK (state IN ('playing', 'left', 'death', 'disconnected', 'expired', 'level_cap')),
  started_at TIMESTAMPTZ NOT NULL,
  ends_at TIMESTAMPTZ NOT NULL,
  charged_until TIMESTAMPTZ NOT NULL,
  ended_at TIMESTAMPTZ,
  kill_credits_ms INT NOT NULL DEFAULT 0 CHECK (kill_credits_ms BETWEEN 0 AND 12000),
  credits_at TIMESTAMPTZ NOT NULL,
  kills INT NOT NULL DEFAULT 0 CHECK (kills >= 0),
  xp_granted BIGINT NOT NULL DEFAULT 0 CHECK (xp_granted >= 0),
  FOREIGN KEY (character_id, reset_day) REFERENCES mini_dungeon_days(character_id, reset_day),
  CHECK (ends_at > started_at AND charged_until >= started_at AND charged_until <= ends_at),
  CHECK ((state = 'playing') = (ended_at IS NULL)),
  CHECK (entry_level BETWEEN tier AND tier + 9)
);
CREATE UNIQUE INDEX mini_dungeon_one_playing ON mini_dungeon_runs(character_id) WHERE state = 'playing';
CREATE TABLE mini_dungeon_kills (
  run_id BIGINT NOT NULL REFERENCES mini_dungeon_runs(id),
  monster_ref UUID NOT NULL,
  request_id UUID NOT NULL,
  granted_xp INT NOT NULL CHECK (granted_xp >= 0),
  created_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (run_id, monster_ref)
);
CREATE INDEX mini_dungeon_kills_recent ON mini_dungeon_kills(run_id, created_at);
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check
  CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost', 'dungeon_sweep', 'daily_quest', 'mini_dungeon'));
COMMENT ON TABLE mini_dungeon_days IS 'Character-wide 30 minute budget per 06:00 KST day. Milliseconds prevent reconnect rounding exploits.';
COMMENT ON TABLE mini_dungeon_kills IS 'Mini dungeon XP only. Deliberately separate from kill_log and kill_stats: no quest, drops or gold progress.';
-- ============ DOWN ============
-- Preserve the append-only XP ledger: rollback is possible only before rewards exist.
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check
  CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost', 'dungeon_sweep', 'daily_quest'));
DROP TABLE mini_dungeon_kills;
DROP TABLE mini_dungeon_runs;
DROP TABLE mini_dungeon_days;
