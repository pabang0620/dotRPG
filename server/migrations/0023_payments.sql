-- 0023_payments: 별조각 Steam 결제(Docs/server/phase11_payments.md 14절)
-- 대상: PostgreSQL 14 이상. 선행: 0013_starshop, 0016, 0018, 0020_anti_abuse(economy_holds, ledger_block_mutation), 0021.

-- ============ UP ============

-- 1. 확률표 스냅샷(추가 전용): 같은 버전에 다른 내용이면 서버가 기동하지 않는다
CREATE TABLE star_rates_snapshots (
  version      TEXT PRIMARY KEY CHECK (char_length(version) BETWEEN 1 AND 40),
  content      JSONB NOT NULL CHECK (jsonb_typeof(content) = 'object'),
  content_hash TEXT NOT NULL CHECK (content_hash ~ '^[0-9a-f]{64}$'),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  star_rates_snapshots IS '뽑기 확률표 버전별 스냅샷(추가 전용). gacha_pulls.rates_version이 가리킨다. 확률 분쟁·공개 자료의 근거';
COMMENT ON COLUMN star_rates_snapshots.content IS '그 버전의 확률·가격·천장·게이지·중복 규칙 전체(서버가 starshopDefs에서 만든다)';
CREATE TRIGGER star_rates_snapshots_append_only BEFORE UPDATE OR DELETE ON star_rates_snapshots
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_rates_snapshots_no_truncate BEFORE TRUNCATE ON star_rates_snapshots
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 2. 결제 계정 상태
CREATE TABLE payment_profiles (
  account_id       BIGINT PRIMARY KEY REFERENCES accounts(id),
  status           TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'blocked')),
  block_reason     TEXT CHECK (block_reason IN ('chargeback', 'refund_abuse', 'linked_chargeback', 'fraud_suspect', 'manual')),
  blocked_at       TIMESTAMPTZ,
  blocked_by       TEXT CHECK (char_length(blocked_by) <= 40),
  tier_floor       TEXT NOT NULL DEFAULT 'none' CHECK (tier_floor IN ('none', 'restricted')),
  tier_floor_until TIMESTAMPTZ,
  last_country     TEXT CHECK (last_country ~ '^[A-Z]{2}$'),
  last_currency    TEXT CHECK (last_currency ~ '^[A-Z]{3}$'),
  note             TEXT CHECK (char_length(note) <= 500),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT payment_profiles_block_chk CHECK ((status = 'blocked') = (block_reason IS NOT NULL AND blocked_at IS NOT NULL)),
  CONSTRAINT payment_profiles_floor_chk CHECK (tier_floor = 'none' OR tier_floor_until IS NOT NULL)
);
COMMENT ON TABLE  payment_profiles IS '계정의 결제 상태. 행이 없으면 정상(active, 제한 없음). 결제 정지와 제한 단계 하한만 담고 한도 사용량은 star_orders에서 계산한다(파생 값을 따로 저장하지 않는다)';
COMMENT ON COLUMN payment_profiles.status IS 'blocked면 새 주문을 만들 수 없다(B1, B2가 PAYMENT_BLOCKED). 해제는 owner';
COMMENT ON COLUMN payment_profiles.blocked_by IS '"system" 또는 관리자 login_id(표시용). 정본 기록은 admin_audit_log';
COMMENT ON COLUMN payment_profiles.tier_floor IS 'restricted면 tier_floor_until 전까지 제한 단계(낮은 한도) 적용. 자동(환불 반복, 국가 변경 등)과 수동이 올린다';
COMMENT ON COLUMN payment_profiles.last_country IS '마지막 주문에서 Steam이 알려 준 국가. 국가 변경 감지용';

-- 3. 주문
CREATE SEQUENCE star_order_no_seq AS BIGINT START WITH 1000000 INCREMENT BY 1 NO CYCLE;
COMMENT ON SEQUENCE star_order_no_seq IS 'Steam 주문 번호(uint64 범위 안의 양수). 서버만 발급하고 한 번 쓴 번호는 재사용하지 않는다';

CREATE TABLE star_orders (
  id               BIGSERIAL PRIMARY KEY,
  uuid             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id       BIGINT NOT NULL REFERENCES accounts(id),
  request_id       UUID NOT NULL,
  request_hash     TEXT NOT NULL,
  steam_order_id   BIGINT NOT NULL UNIQUE DEFAULT nextval('star_order_no_seq'),
  steam_trans_id   BIGINT UNIQUE,
  steam_id         TEXT NOT NULL CHECK (steam_id ~ '^[0-9]{17}$'),
  app_id           BIGINT NOT NULL CHECK (app_id > 0),
  product_id       TEXT NOT NULL CHECK (product_id ~ '^[a-z0-9_]{3,40}$'),
  steam_item_id    INT NOT NULL CHECK (steam_item_id > 0),
  catalog_version  TEXT NOT NULL,
  stars            INT NOT NULL CHECK (stars > 0),
  currency         TEXT NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
  amount_minor     BIGINT NOT NULL CHECK (amount_minor > 0),
  steam_country    TEXT CHECK (steam_country ~ '^[A-Z]{2}$'),
  steam_status     TEXT CHECK (char_length(steam_status) <= 40),
  steam_amount_minor BIGINT,
  steam_currency   TEXT CHECK (steam_currency ~ '^[A-Z]{3}$'),
  tier             TEXT NOT NULL CHECK (tier IN ('new', 'standard', 'restricted')),
  state            TEXT NOT NULL CHECK (state IN ('pending_init', 'created', 'authorized', 'finalized', 'granted',
                                                  'failed', 'expired', 'refunded', 'chargeback')),
  fail_reason      TEXT CHECK (fail_reason IN ('init_rejected', 'init_lost', 'user_denied', 'steam_failed', 'blocked', 'mismatch')),
  needs_review     BOOLEAN NOT NULL DEFAULT false,
  review_reason    TEXT CHECK (char_length(review_reason) <= 100),
  ip               INET,
  device_hash      TEXT CHECK (device_hash ~ '^[0-9a-f]{64}$'),
  lease_until      TIMESTAMPTZ,
  lease_token      UUID,
  attempts         INT NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  next_check_at    TIMESTAMPTZ,
  last_checked_at  TIMESTAMPTZ,
  expires_at       TIMESTAMPTZ NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  init_at          TIMESTAMPTZ,
  authorized_at    TIMESTAMPTZ,
  finalized_at     TIMESTAMPTZ,
  granted_at       TIMESTAMPTZ,
  reversed_at      TIMESTAMPTZ,
  closed_at        TIMESTAMPTZ,
  UNIQUE (account_id, request_id),
  CONSTRAINT star_orders_fail_chk      CHECK ((state = 'failed') = (fail_reason IS NOT NULL)),
  CONSTRAINT star_orders_granted_chk   CHECK (state <> 'granted' OR granted_at IS NOT NULL),
  CONSTRAINT star_orders_granted_at_chk CHECK (granted_at IS NULL OR state IN ('granted', 'refunded', 'chargeback')),
  CONSTRAINT star_orders_reversed_chk  CHECK ((state IN ('refunded', 'chargeback')) = (reversed_at IS NOT NULL)),
  CONSTRAINT star_orders_expiry_chk    CHECK (expires_at > created_at),
  CONSTRAINT star_orders_lease_chk     CHECK ((lease_until IS NULL) = (lease_token IS NULL)),
  CONSTRAINT star_orders_review_chk    CHECK (needs_review OR review_reason IS NULL)
);
COMMENT ON TABLE  star_orders IS '별조각 결제 주문(상태 기계, 정본). 가격·별조각·통화·Steam ID·appid는 주문 시점 스냅샷이며 상품표가 바뀌어도 변하지 않는다. 지우지 않는다';
COMMENT ON COLUMN star_orders.uuid IS '외부 노출 id(클라이언트·관리자). 내부 id와 steam_order_id는 직접 조회 키로 쓰지 않는다';
COMMENT ON COLUMN star_orders.request_id IS 'B2의 멱등 키. (account_id, request_id) 유일';
COMMENT ON COLUMN star_orders.request_hash IS '요청 본문 정규화 해시. 같은 request_id에 다른 본문이면 IDEMPOTENCY_MISMATCH';
COMMENT ON COLUMN star_orders.steam_order_id IS '서버가 발급해 InitTxn에 보낸 주문 번호. 클라이언트가 정하지 않는다';
COMMENT ON COLUMN star_orders.steam_trans_id IS 'Steam이 돌려준 거래 번호(응답에 있을 때). 유일';
COMMENT ON COLUMN star_orders.steam_id IS '결제 주체 Steam ID. 로그인 계정의 auth_identities에서만 얻는다(요청으로 받지 않는다)';
COMMENT ON COLUMN star_orders.catalog_version IS '주문 시점 star_products.json 버전';
COMMENT ON COLUMN star_orders.amount_minor IS '통화 최소 단위 정수. 서버 상품표 값';
COMMENT ON COLUMN star_orders.steam_amount_minor IS 'QueryTxn이 돌려준 항목 금액(검증 후 기록). amount_minor와 같아야 지급한다';
COMMENT ON COLUMN star_orders.steam_status IS '마지막 QueryTxn 상태 문자열(표시·감사용, 로직은 state)';
COMMENT ON COLUMN star_orders.tier IS '주문 시점 적용한 한도 단계';
COMMENT ON COLUMN star_orders.state IS 'pending_init InitTxn 결과 미확정 / created 승인 대기 / authorized Steam 승인됨(확정 전) / finalized 확정·성공 확인(지급 대기) / granted 지급 완료 / failed 청구 없이 종료 / expired 만료 / refunded 환불 / chargeback 지불 거절';
COMMENT ON COLUMN star_orders.needs_review IS '청구되었을 수 있는데 검증이 어긋난 주문 등 사람이 봐야 하는 주문. 상태와 별개로 대사 일정을 유지한다';
COMMENT ON COLUMN star_orders.ip IS '개인정보 성격. 보관 기간(PAY_IP_RETENTION_DAYS) 뒤 NULL로 지운다';
COMMENT ON COLUMN star_orders.lease_until IS '진행 임대 만료. 한 시점에 한 작업자만 주문을 진행한다';
COMMENT ON COLUMN star_orders.lease_token IS '임대 펜싱 토큰. 이후 쓰기는 이 값과 기대 상태를 WHERE에 건다';
COMMENT ON COLUMN star_orders.next_check_at IS '다음 Steam 확인 시각(열린 주문은 수십 초 간격, 지급된 주문은 나이별 감시 간격, 감시 종료 뒤 NULL)';
-- 계정당 열린 주문 1개(동시 주문·한도 우회 방지, 새 견적 전에 열린 주문을 먼저 대사하는 계약의 DB 보장)
CREATE UNIQUE INDEX star_orders_one_open ON star_orders (account_id)
  WHERE state IN ('pending_init', 'created', 'authorized', 'finalized');
-- 한도 합산(계정의 기간 내 주문), 내 주문 이력: account_id + 최신순
CREATE INDEX star_orders_account_time ON star_orders (account_id, created_at DESC);
-- 대사·감시 작업이 "확인할 시각이 된 주문"만 읽는다(열린 주문 + 지급된 주문 + 차지백 승격을 감시하는 환불 주문)
CREATE INDEX star_orders_due ON star_orders (next_check_at)
  WHERE next_check_at IS NOT NULL AND state IN ('pending_init', 'created', 'authorized', 'finalized', 'granted', 'refunded', 'failed');
-- 검토 큐(needs_review) 조회
CREATE INDEX star_orders_review ON star_orders (created_at) WHERE needs_review;
-- 관리자 목록(상태별 최신순)
CREATE INDEX star_orders_state_time ON star_orders (state, created_at DESC);
-- Steam ID로 주문 찾기(관리자 조회, 리포트 교차 점검)
CREATE INDEX star_orders_steam_id ON star_orders (steam_id, created_at DESC);

-- 4. 주문 이벤트(추가 전용): 결제의 모든 전이와 Steam 조회 결과
CREATE TABLE star_order_events (
  id          BIGSERIAL PRIMARY KEY,
  order_id    BIGINT NOT NULL REFERENCES star_orders(id),
  kind        TEXT NOT NULL CHECK (kind IN ('created', 'init_ok', 'init_failed', 'init_unknown', 'status_seen', 'authorized',
                                            'finalize_ok', 'finalize_failed', 'finalized', 'granted', 'failed', 'expired',
                                            'refunded', 'chargeback', 'revoked_stars', 'mismatch', 'needs_review',
                                            'review_cleared', 'admin_recheck', 'outcomes_revoked')),
  from_state  TEXT,
  to_state    TEXT,
  steam_status TEXT CHECK (char_length(steam_status) <= 40),
  actor       TEXT NOT NULL CHECK (actor IN ('player', 'job', 'admin', 'system')),
  detail      JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(detail) = 'object'),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE  star_order_events IS '주문 이벤트 원장(추가 전용). detail에는 허용 목록 필드(상태 문자열, 주문·거래 번호, 통화, 금액, 국가, 사유 코드)만 넣고 키·URL·원문 응답·IP는 넣지 않는다. 지우지 않는다';
-- 주문 상세에서 시간순 이벤트
CREATE INDEX star_order_events_order ON star_order_events (order_id, id);
CREATE TRIGGER star_order_events_append_only BEFORE UPDATE OR DELETE ON star_order_events
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_order_events_no_truncate BEFORE TRUNCATE ON star_order_events
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 5. 유료 로트(주문 1개 = 로트 1개) 와 소비 배분
CREATE TABLE star_paid_lots (
  order_id   BIGINT PRIMARY KEY REFERENCES star_orders(id),
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  granted    INT NOT NULL CHECK (granted > 0),
  remaining  INT NOT NULL CHECK (remaining >= 0 AND remaining <= granted),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  revoked_at TIMESTAMPTZ
);
COMMENT ON TABLE  star_paid_lots IS '유료 별조각 로트(지급된 주문마다 한 행, PK = order_id라 주문당 1회 지급을 한 번 더 막는다). 소비는 오래된 로트부터, 환불은 그 주문 로트만 회수한다. SUM(remaining) = star_wallets.paid_balance';
COMMENT ON COLUMN star_paid_lots.remaining IS '아직 쓰지 않은 유료 별조각. 소비·회수·부채 상환으로만 줄어든다';
-- 소비가 계정의 남은 로트를 오래된 순으로 읽는다(FOR UPDATE)
CREATE INDEX star_paid_lots_open ON star_paid_lots (account_id, created_at, order_id) WHERE remaining > 0;

CREATE TABLE star_spend_allocs (
  id        BIGSERIAL PRIMARY KEY,
  ledger_id BIGINT NOT NULL REFERENCES star_ledger(id),
  order_id  BIGINT NOT NULL REFERENCES star_orders(id),
  stars     INT NOT NULL CHECK (stars > 0),
  UNIQUE (ledger_id, order_id)
);
COMMENT ON TABLE  star_spend_allocs IS '유료 별조각 감소 원장 줄(소비·회수·상환)이 어느 주문 로트에서 몇 개를 가져갔나(추가 전용). 환불된 주문의 별조각으로 얻은 것을 되짚는 근거. 줄마다 SUM(stars) = -paid_delta';
-- "이 주문의 별조각이 어디에 쓰였나"(환불 분석, 관리자 상세)
CREATE INDEX star_spend_allocs_order ON star_spend_allocs (order_id);
CREATE TRIGGER star_spend_allocs_append_only BEFORE UPDATE OR DELETE ON star_spend_allocs
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_spend_allocs_no_truncate BEFORE TRUNCATE ON star_spend_allocs
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 6. 운영 지급·부채 탕감(2인 승인)
CREATE TABLE star_admin_grants (
  id               BIGSERIAL PRIMARY KEY,
  uuid             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  kind             TEXT NOT NULL CHECK (kind IN ('grant', 'debt_forgive')),
  account_id       BIGINT NOT NULL REFERENCES accounts(id),
  stars            BIGINT,
  related_order_id BIGINT REFERENCES star_orders(id),
  memo             TEXT NOT NULL CHECK (char_length(memo) BETWEEN 1 AND 200),
  state            TEXT NOT NULL DEFAULT 'pending' CHECK (state IN ('pending', 'applied', 'cancelled', 'expired')),
  request_id       UUID NOT NULL,
  created_by       BIGINT NOT NULL REFERENCES admin_users(id),
  approved_by      BIGINT REFERENCES admin_users(id),
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  approved_at      TIMESTAMPTZ,
  expires_at       TIMESTAMPTZ NOT NULL,
  closed_at        TIMESTAMPTZ,
  applied_stars    BIGINT,
  UNIQUE (created_by, request_id),
  CONSTRAINT star_admin_grants_stars_chk   CHECK ((kind = 'grant') = (stars IS NOT NULL AND stars > 0)),
  CONSTRAINT star_admin_grants_two_person  CHECK (approved_by IS NULL OR approved_by <> created_by),
  CONSTRAINT star_admin_grants_applied_chk CHECK ((state = 'applied') = (approved_by IS NOT NULL AND approved_at IS NOT NULL)),
  CONSTRAINT star_admin_grants_expiry_chk  CHECK (expires_at > created_at)
);
COMMENT ON TABLE  star_admin_grants IS '관리자 별조각 지급·부채 탕감(2인 승인, 추가 전용에 가깝다: 내용은 불변, 상태만 pending에서 한 번 변한다). 지급 경로를 만드는 유일한 운영 경로. 승인 전에는 별조각이 움직이지 않는다';
COMMENT ON COLUMN star_admin_grants.kind IS 'grant 무료 별조각 지급 / debt_forgive 승인 시점 부채 전액 탕감';
COMMENT ON COLUMN star_admin_grants.stars IS 'grant의 지급량(상한은 서버 환경변수가 검사). debt_forgive는 NULL(전액만, 금액을 입력하지 않는다)';
COMMENT ON COLUMN star_admin_grants.applied_stars IS '실제 적용량(grant는 stars, debt_forgive는 승인 시점 부채)';
COMMENT ON COLUMN star_admin_grants.approved_by IS '작성자와 다른 owner. DB CHECK가 이중 장치';
-- 대상 계정의 지급 이력(계정 상세), 관리자별·전체 일일 합계(상한 검사), 승인 대기 목록
CREATE INDEX star_admin_grants_account ON star_admin_grants (account_id, created_at DESC);
CREATE INDEX star_admin_grants_creator_time ON star_admin_grants (created_by, created_at) WHERE kind = 'grant';
CREATE INDEX star_admin_grants_pending ON star_admin_grants (created_at) WHERE state = 'pending';
CREATE FUNCTION star_admin_grants_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'star_admin_grants rows are never deleted';
  END IF;
  IF NEW.kind <> OLD.kind OR NEW.account_id <> OLD.account_id OR NEW.stars IS DISTINCT FROM OLD.stars
     OR NEW.memo <> OLD.memo OR NEW.created_by <> OLD.created_by OR NEW.created_at <> OLD.created_at
     OR NEW.related_order_id IS DISTINCT FROM OLD.related_order_id THEN
    RAISE EXCEPTION 'star_admin_grants content is immutable';
  END IF;
  IF OLD.state <> 'pending' AND NEW.state <> OLD.state THEN
    RAISE EXCEPTION 'star_admin_grants state is final';
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER star_admin_grants_guard BEFORE UPDATE OR DELETE ON star_admin_grants
  FOR EACH ROW EXECUTE FUNCTION star_admin_grants_guard();

-- 7. 결제 플래그(검토 큐)
CREATE TABLE payment_flags (
  id          BIGSERIAL PRIMARY KEY,
  uuid        UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  order_id    BIGINT REFERENCES star_orders(id),
  kind        TEXT NOT NULL CHECK (kind IN ('rapid_orders', 'limit_exceeded', 'fail_burst', 'country_changed', 'shared_device',
                                            'linked_chargeback', 'refund_after_spend', 'chargeback', 'partial_refund',
                                            'amount_mismatch', 'steamid_mismatch', 'appid_mismatch', 'unknown_steam_order',
                                            'stuck_order', 'report_gap', 'unknown_steam_status')),
  severity    SMALLINT NOT NULL CHECK (severity BETWEEN 1 AND 3),
  detail      JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(detail) = 'object'),
  state       TEXT NOT NULL DEFAULT 'open' CHECK (state IN ('open', 'confirmed', 'dismissed')),
  reviewed_by TEXT CHECK (char_length(reviewed_by) <= 40),
  reviewed_at TIMESTAMPTZ,
  note        TEXT CHECK (char_length(note) <= 500),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT payment_flags_review_chk CHECK ((state = 'open') = (reviewed_at IS NULL))
);
COMMENT ON TABLE  payment_flags IS '결제 이상 신호와 검토 큐. 막지 않고 기록하는 신호(한도 반복, 국가 변경 등)와 반드시 사람이 보는 신호(차지백, 금액 불일치, 미지 주문)를 함께 둔다. 검토 필드만 바뀐다';
COMMENT ON COLUMN payment_flags.account_id IS '신호의 대상 계정. 미지 주문처럼 계정을 특정하지 못하는 신호는 이 표에 넣지 않고 job_runs.detail과 경보로만 남긴다(unknown_steam_order는 Steam ID로 우리 계정을 찾은 경우에만 이 표에 들어간다)';
-- 검토 큐(열린 것, 심각도 높은 순, 오래된 순)
CREATE INDEX payment_flags_open ON payment_flags (severity DESC, created_at) WHERE state = 'open';
-- 계정 상세에서 그 계정의 플래그, 같은 종류 중복 억제 조회
CREATE INDEX payment_flags_account ON payment_flags (account_id, kind, created_at DESC);

-- 8. 별조각 지갑·원장 확장
ALTER TABLE star_wallets
  ADD COLUMN paid_balance BIGINT NOT NULL DEFAULT 0 CHECK (paid_balance >= 0),
  ADD COLUMN debt         BIGINT NOT NULL DEFAULT 0 CHECK (debt >= 0),
  ADD CONSTRAINT star_wallets_paid_le_balance CHECK (paid_balance <= balance);
COMMENT ON COLUMN star_wallets.paid_balance IS '그중 유료분(결제로 산 별조각). = SUM(star_paid_lots.remaining). 무료분 = balance - paid_balance';
COMMENT ON COLUMN star_wallets.debt IS '환불·차지백된 주문 중 이미 소비된 별조각. >0이면 별조각 소비 차단, 모든 입금이 먼저 상환한다. 음수 잔액 대신 쓴다';

ALTER TABLE star_ledger
  ADD COLUMN paid_delta         BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN paid_balance_after BIGINT NOT NULL DEFAULT 0 CHECK (paid_balance_after >= 0),
  ADD COLUMN debt_delta         BIGINT NOT NULL DEFAULT 0,
  ADD COLUMN debt_after         BIGINT NOT NULL DEFAULT 0 CHECK (debt_after >= 0),
  ADD CONSTRAINT star_ledger_paid_le_balance CHECK (paid_balance_after <= balance_after);
COMMENT ON COLUMN star_ledger.paid_delta IS '유료분 증감(유료 입금 +, 유료 소비·회수·상환 -). 무료 변동은 0';
COMMENT ON COLUMN star_ledger.debt_delta IS '부채 증감(회수 +, 상환·탕감 -)';

ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant'));
COMMENT ON COLUMN star_ledger.reason IS 'purchase 결제 지급(주문 필수) / admin_grant 승인된 운영 지급 / dismantle 여분 분해 / test_grant 시험 서버 전용 / gacha·exchange 소비 / refund_revoke·chargeback_revoke 환불·차지백 회수 / debt_settle 입금이 부채 상환 / debt_forgive 승인된 부채 탕감 / gacha_refund 새 입금 금지(과거 행만)';
COMMENT ON COLUMN star_ledger.ref IS 'purchase·refund_revoke·chargeback_revoke는 주문 uuid, admin_grant·debt_forgive는 star_admin_grants uuid';

-- 사유별 부호 규칙(앱 버그가 엉뚱한 부호로 원장을 쓰는 것을 DB가 막는다)
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange')                     AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);

-- 주문당 지급 1줄, 주문당 회수 1줄, 운영 지급당 1줄(이중 지급의 DB 보장)
CREATE UNIQUE INDEX star_ledger_purchase_uq ON star_ledger (ref) WHERE reason = 'purchase';
CREATE UNIQUE INDEX star_ledger_revoke_uq   ON star_ledger (ref) WHERE reason IN ('refund_revoke', 'chargeback_revoke');
CREATE UNIQUE INDEX star_ledger_admin_uq    ON star_ledger (ref) WHERE reason IN ('admin_grant', 'debt_forgive');
-- 일일 소비 상한: 계정의 오늘 gacha·exchange 합계(지갑 행을 잠근 상태에서 읽는다)
CREATE INDEX star_ledger_spend_time ON star_ledger (account_id, created_at) WHERE reason IN ('gacha', 'exchange');

-- 원장을 추가 전용으로(0013 이후 처음)
CREATE TRIGGER star_ledger_append_only BEFORE UPDATE OR DELETE ON star_ledger
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();
CREATE TRIGGER star_ledger_no_truncate BEFORE TRUNCATE ON star_ledger
  FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();

-- 근거 없는 입금을 커밋 시점에 거절(지연 제약 트리거: 같은 트랜잭션에서 주문·지급 상태를 먼저 바꿔도 된다)
CREATE FUNCTION star_ledger_backing_check() RETURNS trigger AS $$
BEGIN
  IF NEW.reason = 'purchase' THEN
    IF NOT EXISTS (SELECT 1 FROM star_orders o
                    WHERE o.uuid::text = NEW.ref AND o.account_id = NEW.account_id AND o.stars = NEW.delta
                      AND o.granted_at IS NOT NULL AND o.state IN ('granted', 'refunded', 'chargeback')) THEN
      RAISE EXCEPTION 'star_ledger purchase % has no granted order', NEW.ref;
    END IF;
  ELSIF NEW.reason IN ('refund_revoke', 'chargeback_revoke') THEN
    IF NOT EXISTS (SELECT 1 FROM star_orders o
                    WHERE o.uuid::text = NEW.ref AND o.account_id = NEW.account_id
                      AND o.granted_at IS NOT NULL AND o.state IN ('refunded', 'chargeback')) THEN
      RAISE EXCEPTION 'star_ledger % % has no reversed order', NEW.reason, NEW.ref;
    END IF;
  ELSIF NEW.reason IN ('admin_grant', 'debt_forgive') THEN
    IF NOT EXISTS (SELECT 1 FROM star_admin_grants g
                    WHERE g.uuid::text = NEW.ref AND g.account_id = NEW.account_id AND g.state = 'applied'
                      AND g.kind = CASE WHEN NEW.reason = 'admin_grant' THEN 'grant' ELSE 'debt_forgive' END) THEN
      RAISE EXCEPTION 'star_ledger % % has no approved grant', NEW.reason, NEW.ref;
    END IF;
  ELSIF NEW.reason = 'test_grant' THEN
    IF coalesce(current_setting('dotrpg.allow_test_grant', true), '') <> 'on' THEN
      RAISE EXCEPTION 'star_ledger test_grant is allowed only on the test stage';
    END IF;
  ELSIF NEW.reason = 'gacha_refund' THEN
    RAISE EXCEPTION 'star_ledger gacha_refund is retired';
  END IF;
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;
CREATE CONSTRAINT TRIGGER star_ledger_backing AFTER INSERT ON star_ledger
  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION star_ledger_backing_check();

-- 9. 경제 정지: 결제 종류 추가, 활성 정지 유일 인덱스에 "결제 여부"를 넣어 속도 정지와 공존
ALTER TABLE economy_holds DROP CONSTRAINT economy_holds_kind_check;
ALTER TABLE economy_holds ADD CONSTRAINT economy_holds_kind_check
  CHECK (kind IN ('velocity', 'auction', 'linked', 'manual', 'payment'));
DROP INDEX economy_holds_one_active;
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char, ((kind = 'payment')))
  WHERE state IN ('active', 'clawed_back');

-- 10. 감사 로그 대상 종류
ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail',
                         'server', 'hold', 'campaign', 'payment_order', 'payment_account', 'star_grant', 'payment_flag'));

-- ============ DOWN ============
-- 개발 DB 전용(운영에서는 돈 기록이므로 되돌리지 않는다). 0018 DOWN이 star_ledger를 DELETE하므로 반드시 이 DOWN이 먼저 실행된다.

ALTER TABLE admin_audit_log DROP CONSTRAINT admin_audit_log_target_type_check;
ALTER TABLE admin_audit_log ADD CONSTRAINT admin_audit_log_target_type_check
  CHECK (target_type IN ('account', 'character', 'report', 'dungeon_run', 'sanction', 'maintenance', 'job', 'admin', 'mail', 'server', 'hold', 'campaign'));

DELETE FROM economy_holds WHERE kind = 'payment';
DROP INDEX IF EXISTS economy_holds_one_active;
CREATE UNIQUE INDEX economy_holds_one_active ON economy_holds (account_id, scope_char) WHERE state IN ('active', 'clawed_back');
ALTER TABLE economy_holds DROP CONSTRAINT economy_holds_kind_check;
ALTER TABLE economy_holds ADD CONSTRAINT economy_holds_kind_check CHECK (kind IN ('velocity', 'auction', 'linked', 'manual'));

DROP TRIGGER IF EXISTS star_ledger_backing ON star_ledger;
DROP FUNCTION IF EXISTS star_ledger_backing_check();
DROP TRIGGER IF EXISTS star_ledger_no_truncate ON star_ledger;
DROP TRIGGER IF EXISTS star_ledger_append_only ON star_ledger;
DROP TRIGGER IF EXISTS star_spend_allocs_no_truncate ON star_spend_allocs;
DROP TRIGGER IF EXISTS star_spend_allocs_append_only ON star_spend_allocs;
DROP TABLE IF EXISTS star_spend_allocs;
DROP INDEX IF EXISTS star_ledger_spend_time;
DROP INDEX IF EXISTS star_ledger_admin_uq;
DROP INDEX IF EXISTS star_ledger_revoke_uq;
DROP INDEX IF EXISTS star_ledger_purchase_uq;
DELETE FROM star_ledger WHERE reason IN ('purchase', 'refund_revoke', 'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant');
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle'));
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_paid_le_balance;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS debt_after;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS debt_delta;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS paid_balance_after;
ALTER TABLE star_ledger DROP COLUMN IF EXISTS paid_delta;
ALTER TABLE star_wallets DROP CONSTRAINT IF EXISTS star_wallets_paid_le_balance;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS debt;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS paid_balance;

DROP TABLE IF EXISTS payment_flags;
DROP TRIGGER IF EXISTS star_admin_grants_guard ON star_admin_grants;
DROP TABLE IF EXISTS star_admin_grants;
DROP FUNCTION IF EXISTS star_admin_grants_guard();
DROP TABLE IF EXISTS star_paid_lots;
DROP TRIGGER IF EXISTS star_order_events_no_truncate ON star_order_events;
DROP TRIGGER IF EXISTS star_order_events_append_only ON star_order_events;
DROP TABLE IF EXISTS star_order_events;
DROP TABLE IF EXISTS star_orders;
DROP SEQUENCE IF EXISTS star_order_no_seq;
DROP TABLE IF EXISTS payment_profiles;
DROP TRIGGER IF EXISTS star_rates_snapshots_no_truncate ON star_rates_snapshots;
DROP TRIGGER IF EXISTS star_rates_snapshots_append_only ON star_rates_snapshots;
DROP TABLE IF EXISTS star_rates_snapshots;
