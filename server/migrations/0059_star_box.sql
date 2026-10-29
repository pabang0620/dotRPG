-- 0059_star_box: 봉인된 상자 확률표 sealed-2의 별조각 상자(box_stars) 열기(2026-10-29, Docs/PLAN_CASH_BOX_PASS.md 8절). 선행: 0058.
-- 대상: PostgreSQL 14 이상. 새 테이블·열은 없다(탈퇴 익명화 대상 변화 없음). 원장 사유와 결과 기록 출처만 늘린다.
--   1. star_ledger.reason 에 star_box(무료 입금: paid_delta = 0, ref = 'box_stars', request_id = 열기 요청) 추가
--   2. sealed_pulls.source 에 star_box(rate = 그 수량의 확률 %, item_key = 'stars', count = 받은 별조각) 추가

-- ============ UP ============
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy', 'crystal_convert', 'star_box'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily', 'crystal_convert', 'star_box') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);

ALTER TABLE sealed_pulls DROP CONSTRAINT IF EXISTS sealed_pulls_source_check;
ALTER TABLE sealed_pulls ADD CONSTRAINT sealed_pulls_source_check CHECK (source IN ('shop', 'sealed_item', 'luck_box', 'daily_chest', 'mat_box', 'star_box'));
COMMENT ON COLUMN sealed_pulls.source IS 'shop 캐시샵 뽑기 / sealed_item 봉인된 상자 아이템 / luck_box 확률 상자 / daily_chest 완료 상자 보너스 굴림 / mat_box 강화 재료 상자(rate = 그 수량 구간 확률 %) / star_box 별조각 상자(rate = 그 수량 확률 %, count = 무료 별조각)';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0058 DOWN과 같은 방식).
DELETE FROM sealed_pulls WHERE source = 'star_box';
ALTER TABLE sealed_pulls DROP CONSTRAINT IF EXISTS sealed_pulls_source_check;
ALTER TABLE sealed_pulls ADD CONSTRAINT sealed_pulls_source_check CHECK (source IN ('shop', 'sealed_item', 'luck_box', 'daily_chest', 'mat_box'));
ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason = 'star_box';
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy', 'skill_style_buy', 'pass_daily', 'item_buy', 'crystal_convert'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy', 'item_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward', 'pass_daily', 'crystal_convert') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
