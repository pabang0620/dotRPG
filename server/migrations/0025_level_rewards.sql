-- 0025_level_rewards: 레벨 달성 보상(Docs/server/phase13_level_rewards.md)
-- 대상: PostgreSQL 14 이상. 선행: 0024_revive_coins.
--
-- 계정당 단계별 1회, 무료 별조각 지급. 원장 reason 'level_reward'(ref 'level:<n>')를 허용하고 부호 규칙에 넣는다.

-- ============ UP ============
CREATE TABLE account_level_rewards (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  level        INT NOT NULL,
  stars        INT NOT NULL CHECK (stars > 0),
  request_id   UUID NOT NULL,
  character_id BIGINT NULL REFERENCES characters(id),
  claimed_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, level),
  UNIQUE (account_id, request_id)
);
COMMENT ON TABLE account_level_rewards IS '레벨 달성 보상 수령 기록(계정당 단계별 1회). 단계 표는 server/data/level_rewards.json';

ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange')                     AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
-- 계정당 단계별 지급 1줄(이중 지급의 DB 보장)
CREATE UNIQUE INDEX star_ledger_level_reward_uq ON star_ledger (account_id, ref) WHERE reason = 'level_reward';

-- ============ DOWN ============
-- 개발 DB 전용: 원장은 추가 전용이라 level_reward 행이 있으면 되돌릴 수 없다(0023 DOWN과 같은 이유로 트리거를 잠시 끈다).
DROP INDEX IF EXISTS star_ledger_level_reward_uq;
ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason = 'level_reward';
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange')                     AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
DROP TABLE IF EXISTS account_level_rewards;
