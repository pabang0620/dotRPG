-- 0018_starshop_synth: 외형 여분·합성·분해·컬렉션
-- 대상: PostgreSQL 14 이상. UP/DOWN 마커 형식은 0001~0017과 같다. 선행: 0013_starshop, 0016_starshop_skin.
--
-- 이 마이그레이션이 하는 일
--   1. account_cosmetics.copies: 이미 가진 외형이 또 나오면 별조각으로 바꾸지 않고 여분으로 쌓는다(합성·분해 재료).
--   2. star_wallets.synth_fail_*: 등급별 연속 합성 실패 수(천장). 성공하면 0.
--   3. account_collections: 등록한 컬렉션(외형은 소모하지 않는다).
--   4. star_synth_log: 합성 기록(넣은 외형, 성공 여부, 결과, 천장 전후).
--   5. star_ledger 사유에 'dismantle'(여분 분해)을 더한다.

-- ============ UP ============

ALTER TABLE account_cosmetics ADD COLUMN copies INT NOT NULL DEFAULT 0 CHECK (copies >= 0);
COMMENT ON COLUMN account_cosmetics.copies IS '가진 외형 위에 더 받은 여분 수(합성·분해 재료). 착용 권한과는 따로 센다';
ALTER TABLE account_cosmetics DROP CONSTRAINT IF EXISTS account_cosmetics_source_check;
ALTER TABLE account_cosmetics ADD CONSTRAINT account_cosmetics_source_check CHECK (source IN ('gacha', 'exchange', 'grant', 'synth'));

ALTER TABLE star_wallets ADD COLUMN synth_fail_rare INT NOT NULL DEFAULT 0 CHECK (synth_fail_rare >= 0);
ALTER TABLE star_wallets ADD COLUMN synth_fail_epic INT NOT NULL DEFAULT 0 CHECK (synth_fail_epic >= 0);
ALTER TABLE star_wallets ADD COLUMN synth_fail_unique INT NOT NULL DEFAULT 0 CHECK (synth_fail_unique >= 0);

CREATE TABLE account_collections (
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  set_id        TEXT NOT NULL CHECK (char_length(set_id) BETWEEN 1 AND 40),
  registered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, set_id)
);
COMMENT ON TABLE account_collections IS '등록한 외형 컬렉션. 세트의 외형을 모두 가졌을 때만 등록되고, 외형은 그대로 남는다';

CREATE TABLE star_synth_log (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  request_id   UUID NOT NULL,
  seq          SMALLINT NOT NULL,
  from_rarity  TEXT NOT NULL CHECK (from_rarity IN ('common', 'rare', 'epic')),
  inputs       JSONB NOT NULL,
  success      BOOLEAN NOT NULL,
  by_pity      BOOLEAN NOT NULL,
  result_item  TEXT NOT NULL,
  fails_before INT NOT NULL,
  fails_after  INT NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id, seq)
);
CREATE INDEX star_synth_log_account_idx ON star_synth_log (account_id, id);
COMMENT ON TABLE star_synth_log IS '외형 합성 기록. 실패하면 result_item은 돌려준 같은 등급 외형';

ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle'));

-- ============ DOWN ============
-- 개발 DB 전용.

DELETE FROM star_ledger WHERE reason = 'dismantle';
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke'));
DROP TABLE IF EXISTS star_synth_log;
DROP TABLE IF EXISTS account_collections;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS synth_fail_unique;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS synth_fail_epic;
ALTER TABLE star_wallets DROP COLUMN IF EXISTS synth_fail_rare;
UPDATE account_cosmetics SET source = 'gacha' WHERE source = 'synth';
ALTER TABLE account_cosmetics DROP CONSTRAINT IF EXISTS account_cosmetics_source_check;
ALTER TABLE account_cosmetics ADD CONSTRAINT account_cosmetics_source_check CHECK (source IN ('gacha', 'exchange', 'grant'));
ALTER TABLE account_cosmetics DROP COLUMN IF EXISTS copies;
