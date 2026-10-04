-- 0010_test_boost: 시험 서버에서 시험 캐릭터의 레벨을 올린 기록을 경험치 원장에 남긴다
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0009와 같다. 선행: 0003_dungeon_solo.
--
-- 이 마이그레이션이 하는 일
--   1. xp_ledger.reason에 test_boost를 추가한다. scripts/test-level.mjs만 쓰고, 운영 서버(DEPLOY_STAGE=live)에서는 스크립트가 거절한다.

-- ============ UP ============

ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상 / dungeon_clear 던전 클리어 / test_boost 시험 서버 레벨 조정(scripts/test-level.mjs)';

-- ============ DOWN ============
-- 개발 DB 전용.

DELETE FROM xp_ledger WHERE reason = 'test_boost';
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상. 던전 클리어(dungeon_clear)는 0003이 추가';
