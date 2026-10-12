-- 0033_gacha_ceilings: 봉인된 상자·장비 뽑기 천장 게이지 (Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A)
-- 대상: PostgreSQL 14 이상. 선행: 0032_remove_gathering.
--   1. account_ceilings: 계정별 천장 게이지 두 개. 열거나 뽑을 때마다 +1, 가득 차면 이용자가 골라서 교환한다(자동 지급 없음)
--   2. ceiling_claims: 교환 기록(감사용, 추가만 한다). 지급 자체는 item_ledger(sealed_box / gacha, ref ceiling...)에 남는다

-- ============ UP ============
CREATE TABLE account_ceilings (
  account_id    BIGINT PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  sealed_gauge  INT NOT NULL DEFAULT 0 CHECK (sealed_gauge >= 0),
  gear_gauge    INT NOT NULL DEFAULT 0 CHECK (gear_gauge >= 0),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE account_ceilings IS '천장 게이지(계정별). sealed_gauge 봉인된 상자 개봉 수(상점·가방 상자), gear_gauge 장비 뽑기 횟수. 교환하면 그 크기만큼 줄고 넘친 만큼은 남는다';

CREATE TABLE ceiling_claims (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  character_id  BIGINT NULL REFERENCES characters(id),
  request_id    UUID NOT NULL,
  kind          TEXT NOT NULL CHECK (kind IN ('sealed', 'gear')),
  item_key      TEXT NOT NULL,
  count         INT NOT NULL CHECK (count > 0),
  gauge_before  INT NOT NULL,
  gauge_after   INT NOT NULL CHECK (gauge_after >= 0),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  -- 기록용 키. 재전송은 runEconomy 가 막고, 다른 기능에서 쓴 request_id 와 겹쳐도 500이 나지 않게 계정·종류 단위로 둔다
  UNIQUE (account_id, kind, request_id)
);
CREATE INDEX ceiling_claims_account_idx ON ceiling_claims (account_id, created_at DESC);
COMMENT ON TABLE ceiling_claims IS '천장 교환 기록. sealed = 봉인된 상자 천장(+12 강화권), gear = 장비 뽑기 천장(고른 유니크 장비)';

-- ============ DOWN ============
DROP TABLE IF EXISTS ceiling_claims;
DROP TABLE IF EXISTS account_ceilings;
