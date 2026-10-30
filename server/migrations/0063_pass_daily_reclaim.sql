-- 0063_pass_daily_reclaim(2026-10-30): 정기권 환불·차지백 때 그 주문 기간에 이미 받은 일일 지급(무료 별조각 30 + 봉인된 상자 1)도 되돌린다.
--   1. star_ledger.reason 에 pass_daily_revoke(무료분 회수: delta <= 0, paid_delta = 0, debt_delta >= 0 = 이미 쓴 몫은 부채) 추가
--   2. star_pass_daily.reclaimed_order_id: 어느 환불 주문이 이 일일 지급을 회수했는지(같은 지급을 두 주문이 회수하지 않게 한다, 상자 회수 계획의 근거)
-- 선행: 0034, 0059. 대상: PostgreSQL 14 이상.
-- ============ UP ============
ALTER TABLE star_pass_daily ADD COLUMN reclaimed_order_id BIGINT NULL REFERENCES star_orders(id);
COMMENT ON COLUMN star_pass_daily.reclaimed_order_id IS '이 일일 지급을 되돌린 환불·차지백 주문(NULL = 회수 안 됨)';
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy', 'crystal_convert', 'star_box', 'pass_daily_revoke'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily', 'crystal_convert', 'star_box') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
  OR (reason = 'pass_daily_revoke'                        AND delta <= 0 AND paid_delta = 0 AND debt_delta >= 0)
);
-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0059 DOWN과 같은 방식).
ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason = 'pass_daily_revoke';
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy', 'crystal_convert', 'star_box'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily', 'crystal_convert', 'star_box') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
ALTER TABLE star_pass_daily DROP COLUMN IF EXISTS reclaimed_order_id;
