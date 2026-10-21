-- 2026-10-21 점검 반영: (1) 보스 도전 끝 보고가 피해 상한의 80% 이상이면 자르지는 않되 이상 기록, (2) 도전 중 길드를 떠난 기록은 guild_id NULL.
-- ============ UP ============
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution',
                  'sweep_denied', 'boss_damage', 'boss_damage_high'));
COMMENT ON COLUMN anomaly_log.kind IS '0047의 값 + boss_damage_high: 보스 도전 피해가 상한의 80% 이상(인정은 그대로, 조작 의심 후보)';
-- 길드보스 도전 중 길드를 떠나면 끝 보고 때 guild_id를 비워 어느 길드 점수에도 들어가지 않게 한다(길드 종류 + NULL 허용).
DO $$
DECLARE c TEXT;
BEGIN
  SELECT conname INTO c FROM pg_constraint
   WHERE conrelid = 'boss_attempts'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%guild_id IS NOT NULL%';
  IF c IS NOT NULL THEN EXECUTE format('ALTER TABLE boss_attempts DROP CONSTRAINT %I', c); END IF;
END $$;
ALTER TABLE boss_attempts ADD CONSTRAINT boss_attempts_guild_kind_chk CHECK (kind = 'guild' OR guild_id IS NULL);
COMMENT ON COLUMN boss_attempts.guild_id IS '길드보스 도전의 길드. 시작 뒤 길드를 떠났으면 끝 보고 때 NULL(점수·기여에 안 들어감, 참가 보상은 남음)';
-- ============ DOWN ============
ALTER TABLE boss_attempts DROP CONSTRAINT boss_attempts_guild_kind_chk;
DELETE FROM boss_attempts WHERE kind = 'guild' AND guild_id IS NULL;
ALTER TABLE boss_attempts ADD CONSTRAINT boss_attempts_guild_kind_chk CHECK ((kind = 'guild') = (guild_id IS NOT NULL));
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result',
                  'party_result', 'party_host', 'raid_enter',
                  'field_uncredited', 'field_host', 'relay_abuse',
                  'kill_presence', 'device_limit', 'ip_cluster', 'member_card', 'career_state', 'contribution',
                  'sweep_denied', 'boss_damage'));
