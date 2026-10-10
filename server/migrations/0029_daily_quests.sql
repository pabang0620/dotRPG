-- 0029_daily_quests: 일일·주간 의뢰 (Docs/server/phase14_daily_quests.md 3절, 6절)
-- 대상: PostgreSQL 14 이상. 선행: 0028_raid_rewards.
--   1. daily_quests: 캐릭터별 기간(일/주)·날짜·칸마다 수락/청구 기록
--   2. xp_ledger.reason, gold_ledger.reason 에 daily_quest 추가

-- ============ UP ============
-- 1. 일일·주간 의뢰 칸. 행이 있으면 수락, claimed_at 이 있으면 청구 완료. PK가 "기간마다 한 칸 한 번"을 보장한다
CREATE TABLE daily_quests (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  period       TEXT NOT NULL DEFAULT 'day' CHECK (period IN ('day', 'week')),
  game_day     DATE NOT NULL,
  slot         SMALLINT NOT NULL CHECK (slot BETWEEN 0 AND 9),
  template_id  TEXT NOT NULL,
  accepted_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  claimed_at   TIMESTAMPTZ,
  reward       JSONB CHECK (reward IS NULL OR jsonb_typeof(reward) = 'object'),
  request_id   UUID,
  PRIMARY KEY (character_id, period, game_day, slot),
  CONSTRAINT daily_quests_claim_chk CHECK ((claimed_at IS NULL) = (reward IS NULL))
);
COMMENT ON TABLE  daily_quests IS '일일·주간 의뢰 수락·청구 기록. 수락하지 않은 칸은 행이 없다(목록은 daily_quests.json 과 (캐릭터, 날짜) 시드로 매번 계산)';
COMMENT ON COLUMN daily_quests.period IS 'day 일일 의뢰 / week 주간 의뢰';
COMMENT ON COLUMN daily_quests.game_day IS 'day: 06:00 KST 기준 게임 날짜(전날 칸은 carryDays 동안 오늘도 진행). week: 주 시작(목요일 06:00 KST)의 KST 날짜. 이월 없음';
COMMENT ON COLUMN daily_quests.slot IS '그 기간의 칸 번호 0..perDay-1 / 0..perWeek-1';
COMMENT ON COLUMN daily_quests.template_id IS '수락한 의뢰 id(daily_quests.json). 수락 뒤에는 레벨이 바뀌어도 그대로';
COMMENT ON COLUMN daily_quests.accepted_at IS '진행 집계 시작. 처치는 이 시각 이후 의뢰 맵의 kill_log, 던전·레이드는 이후 클리어, 주간 daily 형식은 이후 청구한 일일 칸만 센다';
COMMENT ON COLUMN daily_quests.reward IS '지급 스냅샷 {xp, gold, level}. xp_ledger/gold_ledger daily_quest 행과 같은 값';
COMMENT ON COLUMN daily_quests.request_id IS '청구 요청의 request_id (원장 행과 연결)';

-- 2. 원장 사유 확장
ALTER TABLE xp_ledger DROP CONSTRAINT xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check
  CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost', 'dungeon_sweep', 'daily_quest'));
COMMENT ON COLUMN xp_ledger.reason IS 'kill 처치 / quest_reward 퀘스트 보상 / dungeon_clear 던전 클리어 / test_boost 시험 서버 레벨 조정 / dungeon_sweep 던전 소탕(ref = 소탕 uuid) / daily_quest 일일·주간 의뢰(ref = 의뢰 id)';

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest'));
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. daily_quest 일일·주간 의뢰 보상(ref = 의뢰 id), raid_gold 레이드 클리어 확정 골드(ref = 던전 판 uuid). 그 밖의 설명은 0002, 0007, 0021 참고';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0028 DOWN과 같은 방식).
ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason = 'daily_quest';
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold'));
ALTER TABLE xp_ledger DISABLE TRIGGER xp_ledger_append_only;
DELETE FROM xp_ledger WHERE reason = 'daily_quest';
ALTER TABLE xp_ledger ENABLE TRIGGER xp_ledger_append_only;
ALTER TABLE xp_ledger DROP CONSTRAINT IF EXISTS xp_ledger_reason_check;
ALTER TABLE xp_ledger ADD CONSTRAINT xp_ledger_reason_check
  CHECK (reason IN ('kill', 'quest_reward', 'dungeon_clear', 'test_boost', 'dungeon_sweep'));
DROP TABLE IF EXISTS daily_quests;
