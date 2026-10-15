-- 0044_cash_value: 캐시 아이템 가치 보존(2026-10-14, Docs/PLAN_CASH_ITEM_VALUE.md)
-- 대상: PostgreSQL 14 이상. 선행: 0043.
--   1. 별의 결정: 외형 여분·뽑기 장비를 분해하면 받는 교환 포인트. 별조각·골드로 바꿀 수 없고 거래할 수 없다.
--      유료 별조각으로 뽑은 몫은 결제 주문별 로트(crystal_paid_lots)로 나눠 기록하고, 환불·차지백 때 남은 몫을 걷고
--      이미 쓴 몫은 부채로 남긴다(별조각과 같은 방식). 부채가 있으면 교환소를 쓸 수 없고 다음 결정 입금이 먼저 갚는다.
--   2. 별빛 교환소 구매 기록(주간 재고 계산), 3. 뽑기 장비 몫(상점 골드 판매 금지·분해 허용 판정), 4. 장비 도감.

-- ============ UP ============
CREATE TABLE crystal_wallets (
  account_id   BIGINT PRIMARY KEY REFERENCES accounts(id),
  balance      BIGINT NOT NULL DEFAULT 0 CHECK (balance >= 0),
  paid_balance BIGINT NOT NULL DEFAULT 0 CHECK (paid_balance >= 0),
  debt         BIGINT NOT NULL DEFAULT 0 CHECK (debt >= 0),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT crystal_wallets_paid_chk CHECK (paid_balance <= balance)
);
COMMENT ON TABLE crystal_wallets IS '별의 결정 지갑(0044). paid_balance = 유료 별조각에서 나온 몫(SUM(crystal_paid_lots.remaining)), debt = 환불됐는데 이미 쓴 몫';

CREATE TABLE crystal_ledger (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  delta         BIGINT NOT NULL,
  balance_after BIGINT NOT NULL CHECK (balance_after >= 0),
  paid_delta    BIGINT NOT NULL DEFAULT 0,
  debt_delta    BIGINT NOT NULL DEFAULT 0,
  debt_after    BIGINT NOT NULL DEFAULT 0 CHECK (debt_after >= 0),
  reason        TEXT NOT NULL CHECK (reason IN ('dismantle_cosmetic', 'dismantle_gear', 'exchange_buy', 'refund_revoke',
                                                'chargeback_revoke', 'debt_settle', 'admin_grant', 'test_grant')),
  ref           TEXT,
  request_id    UUID,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX crystal_ledger_account_idx ON crystal_ledger (account_id, id);
COMMENT ON TABLE crystal_ledger IS '별의 결정 원장(0044). 추가만 한다. ref: 분해한 외형 id·장비 키, 교환소 품목 id, 환불 주문 uuid';

CREATE TABLE crystal_paid_lots (
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  order_id   BIGINT NOT NULL REFERENCES star_orders(id),
  granted    BIGINT NOT NULL DEFAULT 0 CHECK (granted >= 0),
  remaining  BIGINT NOT NULL DEFAULT 0 CHECK (remaining >= 0),
  revoked_at TIMESTAMPTZ,
  PRIMARY KEY (order_id),
  CONSTRAINT crystal_paid_lots_rem_chk CHECK (remaining <= granted)
);
CREATE INDEX crystal_paid_lots_account_idx ON crystal_paid_lots (account_id, order_id);
COMMENT ON TABLE crystal_paid_lots IS '유료 별조각 주문별로 나온 결정 몫(0044). 분해할 때 그 계정 뽑기 소비의 유료 비율만큼 주문별로 나눠 쌓는다';

CREATE TABLE crystal_spend_allocs (
  ledger_id BIGINT NOT NULL REFERENCES crystal_ledger(id),
  order_id  BIGINT NOT NULL REFERENCES star_orders(id),
  crystals  BIGINT NOT NULL CHECK (crystals > 0)
);
CREATE INDEX crystal_spend_allocs_order_idx ON crystal_spend_allocs (order_id);
COMMENT ON TABLE crystal_spend_allocs IS '교환소에서 쓴 결정 중 유료 몫이 어느 주문 로트에서 나갔는지(0044)';

CREATE TABLE exchange_purchases (
  id         BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  character_id BIGINT NOT NULL REFERENCES characters(id),
  good_id    TEXT NOT NULL,
  item_id    TEXT,
  price      INT NOT NULL CHECK (price > 0),
  week_start DATE NOT NULL,
  request_id UUID NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX exchange_purchases_week_idx ON exchange_purchases (account_id, week_start, good_id);
COMMENT ON TABLE exchange_purchases IS '별빛 교환소 구매(0044). 주간 재고는 account_id+week_start+good_id 로 센다(결제 한도가 아니라 교환소 재고)';

CREATE TABLE gacha_gear_credits (
  character_id BIGINT NOT NULL REFERENCES characters(id),
  base_id      TEXT NOT NULL,
  count        INT NOT NULL DEFAULT 0 CHECK (count >= 0),
  PRIMARY KEY (character_id, base_id)
);
COMMENT ON TABLE gacha_gear_credits IS '뽑기(천장 포함)로 받은 장비 몫(0044, 기본 id 단위, 강화 단계 무관). 이 몫은 상점에 골드로 팔 수 없고 분해만 된다';

-- 이미 받은 뽑기 장비: 뽑기 원장 합계를 지금 가진 수(모든 위치·강화 단계)로 자른다
INSERT INTO gacha_gear_credits (character_id, base_id, count)
SELECT g.character_id, g.base, LEAST(g.n, COALESCE(o.n, 0))::INT
  FROM (SELECT character_id, split_part(item_key, '+', 1) AS base, SUM(delta) AS n
          FROM item_ledger WHERE reason = 'gacha' AND item_key LIKE 'eq\_%' GROUP BY 1, 2) g
  LEFT JOIN (SELECT character_id, split_part(item_key, '+', 1) AS base, SUM(count) AS n
               FROM character_items WHERE item_key LIKE 'eq\_%' GROUP BY 1, 2) o
    ON o.character_id = g.character_id AND o.base = g.base
 WHERE LEAST(g.n, COALESCE(o.n, 0)) > 0;

CREATE TABLE gear_codex (
  account_id BIGINT NOT NULL REFERENCES accounts(id),
  base_id    TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, base_id)
);
COMMENT ON TABLE gear_codex IS '장비 도감(0044): 처음 얻은 유니크·레전더리 장비 기본 id. 한 줄마다 계정 체력 +0.5%(상한 10%). 분해해도 남는다';

-- 분해한 뽑기 장비(아이템 원장 사유), 교환소에서 받은 물건
ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward',
                    'raid_shop_cost', 'raid_shop_result', 'trade_out', 'trade_in',
                    'gear_dismantle', 'dismantle_bonus', 'exchange_buy'));

-- 교환소에서 얻은 외형의 출처
ALTER TABLE account_cosmetics DROP CONSTRAINT IF EXISTS account_cosmetics_source_check;
ALTER TABLE account_cosmetics ADD CONSTRAINT account_cosmetics_source_check CHECK (source IN ('gacha', 'exchange', 'grant', 'synth', 'crystal_shop'));

-- ============ DOWN ============
ALTER TABLE account_cosmetics DROP CONSTRAINT IF EXISTS account_cosmetics_source_check;
ALTER TABLE account_cosmetics ADD CONSTRAINT account_cosmetics_source_check CHECK (source IN ('gacha', 'exchange', 'grant', 'synth'));
ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward',
                    'raid_shop_cost', 'raid_shop_result', 'trade_out', 'trade_in'));
DROP TABLE IF EXISTS gear_codex;
DROP TABLE IF EXISTS gacha_gear_credits;
DROP TABLE IF EXISTS exchange_purchases;
DROP TABLE IF EXISTS crystal_spend_allocs;
DROP TABLE IF EXISTS crystal_paid_lots;
DROP TABLE IF EXISTS crystal_ledger;
DROP TABLE IF EXISTS crystal_wallets;
