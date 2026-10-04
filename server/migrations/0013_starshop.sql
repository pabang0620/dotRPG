-- 0013_starshop: 캐시샵(별조각)과 외형 뽑기
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0012와 같다. 선행: 0001_init(accounts).
--
-- 이 마이그레이션이 하는 일
--   1. star_wallets: 계정의 별조각(유료 재화) 잔액과 천장 진행도. 캐릭터가 아니라 계정에 묶인다.
--   2. star_ledger: 별조각 증감 원장(추가만). 지급·사용·환급 전부 한 줄씩 남는다.
--   3. account_cosmetics: 계정이 가진 외형(뽑기·교환으로 얻음). 무료 외형은 넣지 않는다.
--   4. gacha_pulls: 뽑기 1회마다 적용 확률표 버전, 천장 전후 값, 결과, 중복 환급을 남긴다.
-- 확률표·가격·외형 목록은 코드(starshopDefs.ts)에 있고 rates_version으로 어떤 표를 썼는지 기록한다.

-- ============ UP ============

CREATE TABLE star_wallets (
  account_id BIGINT PRIMARY KEY REFERENCES accounts(id),
  balance    BIGINT NOT NULL DEFAULT 0 CHECK (balance >= 0),
  pity       INT NOT NULL DEFAULT 0 CHECK (pity >= 0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE star_wallets IS '별조각 지갑. 서버만 바꾼다';
COMMENT ON COLUMN star_wallets.pity IS '마지막 전설 이후 전설 없이 뽑은 횟수(천장 진행도). 시즌 초기화 없음';

CREATE TABLE star_ledger (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  delta         BIGINT NOT NULL,
  balance_after BIGINT NOT NULL CHECK (balance_after >= 0),
  reason        TEXT NOT NULL CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke')),
  ref           TEXT,
  request_id    UUID,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX star_ledger_account_idx ON star_ledger (account_id, id);
COMMENT ON TABLE star_ledger IS '별조각 원장(추가만)';

CREATE TABLE account_cosmetics (
  account_id  BIGINT NOT NULL REFERENCES accounts(id),
  item_id     TEXT NOT NULL CHECK (char_length(item_id) BETWEEN 1 AND 40),
  source      TEXT NOT NULL CHECK (source IN ('gacha', 'exchange', 'grant')),
  acquired_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, item_id)
);
COMMENT ON TABLE account_cosmetics IS '계정이 가진 외형. 능력치가 없는 외형만 들어간다';

CREATE TABLE gacha_pulls (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  request_id    UUID NOT NULL,
  seq           SMALLINT NOT NULL,
  rates_version TEXT NOT NULL,
  rarity        TEXT NOT NULL CHECK (rarity IN ('common', 'rare', 'legend')),
  item_id       TEXT NOT NULL,
  pity_before   INT NOT NULL,
  pity_after    INT NOT NULL,
  by_pity       BOOLEAN NOT NULL,
  duplicate     BOOLEAN NOT NULL,
  refund        INT NOT NULL DEFAULT 0,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id, seq)
);
CREATE INDEX gacha_pulls_account_idx ON gacha_pulls (account_id, id);
COMMENT ON TABLE gacha_pulls IS '뽑기 결과 기록. 적용한 확률표 버전과 천장 전후 값을 함께 남긴다';

-- ============ DOWN ============
-- 개발 DB 전용.

DROP TABLE IF EXISTS gacha_pulls;
DROP TABLE IF EXISTS account_cosmetics;
DROP TABLE IF EXISTS star_ledger;
DROP TABLE IF EXISTS star_wallets;
