-- 0034_star_pass: 별빛 정기권(30일 정액, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md B)
-- 대상: PostgreSQL 14 이상. 선행: 0033(다른 묶음). 이 파일은 0033과 겹치는 표를 건드리지 않는다.
--   1. account_star_pass: 계정별 정기권 만료 시각(재구매는 남은 기간에 일수를 더한다, 최대 60일)
--   2. star_pass_grants: 결제 주문별 기간 지급·회수 기록(주문당 지급 1줄, 회수 1줄)
--   3. star_pass_daily: 계정별 일일 지급 기록(초기화 구간당 1줄 = 이중 지급의 DB 보장)
--   4. 별조각 원장 사유 확장: star_ledger(pass_daily, 무료 입금)

-- ============ UP ============
CREATE TABLE account_star_pass (
  account_id  BIGINT PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  expires_at  TIMESTAMPTZ NOT NULL,
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE account_star_pass IS '별빛 정기권 만료 시각(계정 단위). expires_at > now() 이면 활성';

CREATE TABLE star_pass_grants (
  id          BIGSERIAL PRIMARY KEY,
  account_id  BIGINT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  order_id    BIGINT NOT NULL REFERENCES star_orders(id),
  kind        TEXT NOT NULL CHECK (kind IN ('grant', 'revoke')),
  -- 실제로 더하거나 뺀 기간(초). 상한(60일)에 걸리면 상품 일수보다 짧을 수 있다
  seconds     BIGINT NOT NULL CHECK (seconds >= 0),
  expires_after TIMESTAMPTZ NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (order_id, kind)
);
COMMENT ON TABLE star_pass_grants IS '정기권 기간 지급(grant)·환불 회수(revoke). 주문당 종류별 1줄';

CREATE TABLE star_pass_daily (
  account_id   BIGINT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  day_start    TIMESTAMPTZ NOT NULL,
  character_id BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
  mail_id      BIGINT,
  stars        INT NOT NULL CHECK (stars >= 0),
  request_id   TEXT NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, day_start)
);
COMMENT ON TABLE star_pass_daily IS '정기권 일일 지급(초기화 06:00 KST 구간당 계정 1줄). 별조각은 원장 pass_daily, 상자는 시스템 우편';

ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
-- 계정당 구간별 일일 지급 1줄
CREATE UNIQUE INDEX star_ledger_pass_daily_uq ON star_ledger (account_id, ref) WHERE reason = 'pass_daily';

-- ============ DOWN ============
-- 개발 DB 전용: 원장은 추가 전용이라 pass_daily 행을 지우려면 트리거를 잠시 끈다(0031 DOWN과 같은 방식).
DROP INDEX IF EXISTS star_ledger_pass_daily_uq;
ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason = 'pass_daily';
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
DROP TABLE IF EXISTS star_pass_daily;
DROP TABLE IF EXISTS star_pass_grants;
DROP TABLE IF EXISTS account_star_pass;
