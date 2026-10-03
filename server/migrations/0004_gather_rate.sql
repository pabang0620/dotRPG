-- ============ UP ============
-- 채집 분당 상한 초과(gather_rate)를 이상 기록 종류에 더한다

ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'gather_rate', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result'));

-- ============ DOWN ============

DELETE FROM anomaly_log WHERE kind = 'gather_rate';
ALTER TABLE anomaly_log DROP CONSTRAINT anomaly_log_kind_check;
ALTER TABLE anomaly_log ADD CONSTRAINT anomaly_log_kind_check
  CHECK (kind IN ('kill_target', 'kill_rate', 'kill_supply', 'kill_power',
                  'gather_node', 'gather_early', 'drop_foreign', 'quest_denied', 'chest_unknown',
                  'dungeon_enter', 'dungeon_result'));
