-- 0036_remove_pay_limits: 결제 일·월 한도와 별조각 일일 소비 상한 제거(2026-10-13, Docs/server/phase11_payments.md 10.1)
-- 대상: PostgreSQL 14 이상. 선행: 0035.
--   소비 상한 합산에만 쓰던 부분 인덱스를 지우고, 한도를 전제로 한 주석을 고친다.
--   payment_flags.kind 의 'limit_exceeded' 는 예전 행이 남아 있을 수 있어 CHECK 에서 빼지 않는다(새로 쓰지 않는다).

-- ============ UP ============
DROP INDEX IF EXISTS star_ledger_spend_time;
COMMENT ON TABLE  payment_profiles IS '계정의 결제 상태. 행이 없으면 정상(active, 제한 없음). 결제 정지와 제한 단계 하한만 담는다(결제 금액 한도는 없다)';
COMMENT ON COLUMN payment_profiles.tier_floor IS 'restricted면 tier_floor_until 전까지 제한 단계로 기록한다(결제를 막지 않는다. 이상 신호·관리자 조회용). 자동(환불 반복, 국가 변경 등)과 수동이 올린다';
COMMENT ON COLUMN star_orders.tier IS '주문 시점의 단계(new/standard/restricted). 결제 한도는 없고 기록·조회용';
COMMENT ON TABLE  payment_flags IS '결제 이상 신호와 검토 큐. 막지 않고 기록하는 신호(빠른 재주문, 국가 변경 등)와 반드시 사람이 보는 신호(차지백, 금액 불일치, 미지 주문)를 함께 둔다. 검토 필드만 바뀐다';

-- ============ DOWN ============
CREATE INDEX IF NOT EXISTS star_ledger_spend_time ON star_ledger (account_id, created_at) WHERE reason IN ('gacha', 'exchange');
COMMENT ON TABLE  payment_profiles IS '계정의 결제 상태. 행이 없으면 정상(active, 제한 없음). 결제 정지와 제한 단계 하한만 담고 한도 사용량은 star_orders에서 계산한다(파생 값을 따로 저장하지 않는다)';
COMMENT ON COLUMN payment_profiles.tier_floor IS 'restricted면 tier_floor_until 전까지 제한 단계(낮은 한도) 적용. 자동(환불 반복, 국가 변경 등)과 수동이 올린다';
COMMENT ON COLUMN star_orders.tier IS '주문 시점 적용한 한도 단계';
COMMENT ON TABLE  payment_flags IS '결제 이상 신호와 검토 큐. 막지 않고 기록하는 신호(한도 반복, 국가 변경 등)와 반드시 사람이 보는 신호(차지백, 금액 불일치, 미지 주문)를 함께 둔다. 검토 필드만 바뀐다';
