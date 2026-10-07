-- 0026_sealed_box_pass: 봉인된 상자(확률형)·강화권·성장 패스(Docs/PLAN_CASH_BOX_PASS.md 14단계)
-- 대상: PostgreSQL 14 이상. 선행: 0025_level_rewards.
--
--   1. account_sealed_state: 계정별 부스터 게이지(봉인 해제 게이지). 일반 10회 뒤 다음 1회가 부스터
--   2. sealed_pulls: 봉인된 상자·확률 상자 결과 한 줄씩(확률 공시·감사용, 추가만 한다)
--   3. account_growth_pass / account_pass_claims: 성장 패스 보유(계정당 1회)와 단계별 수령(계정당 단계별 1회)
--   4. 원장 사유 확장: star_ledger(sealed_pull, pass_buy) / item_ledger(sealed_box, box_open, enhance_ticket, pass_reward)
--      / sweep_ticket_ledger(sealed_box) / enhance_log.outcome(ticket)

-- ============ UP ============
CREATE TABLE account_sealed_state (
  account_id  BIGINT PRIMARY KEY REFERENCES accounts(id),
  gauge       INT NOT NULL DEFAULT 0 CHECK (gauge BETWEEN 0 AND 10),
  total_opens BIGINT NOT NULL DEFAULT 0 CHECK (total_opens >= 0),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
COMMENT ON TABLE account_sealed_state IS '봉인된 상자 부스터 게이지(계정별). gauge 10이면 다음 1회가 부스터이고 열면 0으로 돌아간다. 캐시샵 뽑기와 상자 아이템 열기가 같은 게이지를 쓴다';

CREATE TABLE sealed_pulls (
  id            BIGSERIAL PRIMARY KEY,
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  character_id  BIGINT NULL REFERENCES characters(id),
  request_id    UUID NOT NULL,
  seq           SMALLINT NOT NULL,
  source        TEXT NOT NULL CHECK (source IN ('shop', 'sealed_item', 'luck_box')),
  rates_version TEXT NOT NULL,
  row_id        TEXT NOT NULL,
  item_key      TEXT NOT NULL,
  count         INT NOT NULL CHECK (count > 0),
  tier          TEXT NOT NULL CHECK (tier IN ('common', 'rare')),
  boosted       BOOLEAN NOT NULL,
  rate          DOUBLE PRECISION NOT NULL,
  gauge_before  INT NULL,
  gauge_after   INT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id, seq)
);
CREATE INDEX sealed_pulls_account_idx ON sealed_pulls (account_id, id);
COMMENT ON TABLE sealed_pulls IS '봉인된 상자·확률 상자 결과 기록. rate = 그 굴림에 적용된 확률(%, 부스터면 부스터 표 값), 확률 상자(luck_box)는 성공 확률. gauge_* 는 부스터 게이지 전후(확률 상자는 NULL)';

CREATE TABLE account_growth_pass (
  id         BIGSERIAL PRIMARY KEY,
  account_id BIGINT NOT NULL UNIQUE REFERENCES accounts(id),
  stars      INT NOT NULL CHECK (stars > 0),
  request_id UUID NOT NULL,
  bought_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id)
);
COMMENT ON TABLE account_growth_pass IS '성장 패스 보유 기록(계정당 1회, 기간 없음). 가격은 server/data/level_rewards.json pass.price';

CREATE TABLE account_pass_claims (
  id           BIGSERIAL PRIMARY KEY,
  account_id   BIGINT NOT NULL REFERENCES accounts(id),
  level        INT NOT NULL,
  request_id   UUID NOT NULL,
  character_id BIGINT NOT NULL REFERENCES characters(id),
  claimed_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (account_id, level),
  UNIQUE (account_id, request_id)
);
COMMENT ON TABLE account_pass_claims IS '성장 패스 보상 수령 기록(계정당 단계별 1회). 아이템은 수령한 캐릭터의 가방으로 간다';

-- 별조각 원장
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward',
                    'sealed_pull', 'pass_buy'));
ALTER TABLE star_ledger DROP CONSTRAINT star_ledger_sign_chk;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy') AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
-- 패스 구매 이중 차감은 account_growth_pass UNIQUE(account_id)와 계정 행 잠금이 막는다(환불 회수 뒤 재구매를 허용하려고 원장 유일 인덱스는 두지 않는다)

-- 아이템 원장
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback',
                    'sealed_box', 'box_open', 'enhance_ticket', 'pass_reward'));
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). sealed_box 캐시샵 봉인된 상자 지급 / box_open 상자 아이템 열기(상자 -1, 결과 +n) / enhance_ticket 강화권 소모 / pass_reward 성장 패스 보상. 그 밖의 설명은 0002·0007·0019·0020 참고';

-- 클리어권 원장: 봉인된 상자에서 나온 클리어권 지급
ALTER TABLE sweep_ticket_ledger DROP CONSTRAINT sweep_ticket_ledger_reason_check;
ALTER TABLE sweep_ticket_ledger ADD CONSTRAINT sweep_ticket_ledger_reason_check
  CHECK (reason IN ('shop_buy', 'weekly_activity', 'campaign_claim', 'sweep_use', 'expire', 'sealed_box'));

-- 강화 기록: 강화권 적용
ALTER TABLE enhance_log DROP CONSTRAINT enhance_log_outcome_check;
ALTER TABLE enhance_log ADD CONSTRAINT enhance_log_outcome_check
  CHECK (outcome IN ('success', 'keep', 'drop3', 'destroyed', 'protected', 'ticket'));

-- ============ DOWN ============
-- 개발 DB 전용: 원장은 추가 전용이라 새 사유의 행이 있으면 트리거를 잠시 끄고 지운다(0025 DOWN과 같다).
ALTER TABLE enhance_log DROP CONSTRAINT IF EXISTS enhance_log_outcome_check;
DELETE FROM enhance_log WHERE outcome = 'ticket';
ALTER TABLE enhance_log ADD CONSTRAINT enhance_log_outcome_check
  CHECK (outcome IN ('success', 'keep', 'drop3', 'destroyed', 'protected'));

ALTER TABLE sweep_ticket_ledger DISABLE TRIGGER sweep_ticket_ledger_append_only;
DELETE FROM sweep_ticket_ledger WHERE reason = 'sealed_box';
ALTER TABLE sweep_ticket_ledger ENABLE TRIGGER sweep_ticket_ledger_append_only;
ALTER TABLE sweep_ticket_ledger DROP CONSTRAINT IF EXISTS sweep_ticket_ledger_reason_check;
ALTER TABLE sweep_ticket_ledger ADD CONSTRAINT sweep_ticket_ledger_reason_check
  CHECK (reason IN ('shop_buy', 'weekly_activity', 'campaign_claim', 'sweep_use', 'expire'));

ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason IN ('sealed_box', 'box_open', 'enhance_ticket', 'pass_reward');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE item_ledger DROP CONSTRAINT IF EXISTS item_ledger_reason_check;
ALTER TABLE item_ledger ADD CONSTRAINT item_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'gather', 'chest', 'quest_reward', 'quest_consume', 'delivery',
                    'shop_buy', 'shop_sell', 'enhance_cost', 'enhance_result',
                    'equip', 'unequip', 'storage_move', 'use_item', 'dungeon_card',
                    'raid_key', 'raid_key_cost',
                    'auction_list', 'auction_return', 'auction_sold', 'auction_buy', 'mail_claim', 'mail_expire',
                    'admin_grant', 'test_boost', 'gacha',
                    'raid_core', 'promote_cost', 'promote_result', 'gear_renewal', 'admin_clawback'));

ALTER TABLE star_ledger DISABLE TRIGGER star_ledger_append_only;
DELETE FROM star_ledger WHERE reason IN ('sealed_pull', 'pass_buy');
ALTER TABLE star_ledger ENABLE TRIGGER star_ledger_append_only;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_sign_chk;
ALTER TABLE star_ledger DROP CONSTRAINT IF EXISTS star_ledger_reason_check;
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_reason_check
  CHECK (reason IN ('purchase', 'test_grant', 'gacha', 'gacha_refund', 'exchange', 'refund_revoke', 'dismantle',
                    'chargeback_revoke', 'debt_settle', 'debt_forgive', 'admin_grant', 'level_reward'));
ALTER TABLE star_ledger ADD CONSTRAINT star_ledger_sign_chk CHECK (
     (reason = 'purchase'                                 AND delta > 0 AND paid_delta = delta AND debt_delta = 0)
  OR (reason IN ('refund_revoke', 'chargeback_revoke')    AND delta <= 0 AND paid_delta = delta AND debt_delta >= 0)
  OR (reason IN ('gacha', 'exchange')                     AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = 0)
  OR (reason = 'debt_settle'                              AND delta < 0 AND paid_delta BETWEEN delta AND 0 AND debt_delta = delta)
  OR (reason = 'debt_forgive'                             AND delta = 0 AND paid_delta = 0 AND debt_delta < 0)
  OR (reason IN ('admin_grant', 'dismantle', 'test_grant', 'gacha_refund', 'level_reward') AND delta > 0 AND paid_delta = 0 AND debt_delta = 0)
);
DROP TABLE IF EXISTS account_pass_claims;
DROP TABLE IF EXISTS account_growth_pass;
DROP TABLE IF EXISTS sealed_pulls;
DROP TABLE IF EXISTS account_sealed_state;
