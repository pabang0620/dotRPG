-- 0058_crystal_convert: 별의 결정 -> 무료 별조각 바꾸기(사용자 확정 2026-10-29, src/domains/starshop/crystalConvert.ts). 선행: 0057.
-- 대상: PostgreSQL 14 이상. 새 테이블·열은 없다(탈퇴 익명화 대상 변화 없음). 원장 사유만 늘린다.
--   1. crystal_ledger.reason 에 convert_stars(결정을 내고 별조각을 받음, ref = 'convert', request_id = 요청) 추가
--   2. star_ledger.reason 에 crystal_convert(무료 입금: paid_delta = 0) 추가

-- ============ UP ============
ALTER TABLE crystal_ledger DROP CONSTRAINT IF EXISTS crystal_ledger_reason_check;
ALTER TABLE crystal_ledger ADD CONSTRAINT crystal_ledger_reason_check
  CHECK (reason IN ('dismantle_cosmetic', 'dismantle_gear', 'exchange_buy', 'refund_revoke',
                    'chargeback_revoke', 'debt_settle', 'admin_grant', 'test_grant', 'convert_stars'));

ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy', 'crystal_convert'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily', 'crystal_convert') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0056 DOWN과 같은 방식).
ALTER TABLE crystal_spend_allocs DISABLE TRIGGER USER;
DELETE FROM crystal_spend_allocs WHERE ledger_id IN (SELECT id FROM crystal_ledger WHERE reason = 'convert_stars');
ALTER TABLE crystal_spend_allocs ENABLE TRIGGER USER;
ALTER TABLE crystal_ledger DISABLE TRIGGER USER;
DELETE FROM crystal_ledger WHERE reason = 'convert_stars';
ALTER TABLE crystal_ledger ENABLE TRIGGER USER;
ALTER TABLE crystal_ledger DROP CONSTRAINT IF EXISTS crystal_ledger_reason_check;
ALTER TABLE crystal_ledger ADD CONSTRAINT crystal_ledger_reason_check
  CHECK (reason IN ('dismantle_cosmetic', 'dismantle_gear', 'exchange_buy', 'refund_revoke',
                    'chargeback_revoke', 'debt_settle', 'admin_grant', 'test_grant'));
ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason = 'crystal_convert';
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
