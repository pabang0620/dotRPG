-- 0041_trades: 1:1 플레이어 교환(거래) (2026-10-14)
-- 대상: PostgreSQL 14 이상. 선행: 0040.
--   trades: 거래 한 건. 캐릭터가 요청(requester)하고 상대(target)가 수락하면 active, 양쪽이 제안을 잠그고 확정하면 한 트랜잭션에서 교환이 실행되어 completed.
--   trade_items: 양쪽 제안 품목(가방의 귀속 없음 스택만). 완료·취소된 뒤에도 관리자 조회용으로 남긴다.
--   한 캐릭터가 동시에 열린(pending, active) 거래를 하나만 갖는 것은 두 캐릭터 행을 id 순서로 잠근 서비스가 지킨다.
--   version은 제안(골드·품목)이 바뀔 때만 오른다. 잠금·확정은 그 version에 대한 동의다.
--   원장 reason에 trade_out(내가 준 것, -) / trade_in(내가 받은 것, +) 추가. ref = trades.uuid.

-- ============ UP ============
CREATE TABLE trades (
  id                     BIGSERIAL PRIMARY KEY,
  uuid                   UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
  requester_character_id BIGINT NOT NULL REFERENCES characters(id),
  target_character_id    BIGINT NOT NULL REFERENCES characters(id),
  requester_account_id   BIGINT NOT NULL REFERENCES accounts(id),
  target_account_id      BIGINT NOT NULL REFERENCES accounts(id),
  state                  TEXT NOT NULL DEFAULT 'pending'
                         CHECK (state IN ('pending', 'active', 'completed', 'declined', 'cancelled', 'expired', 'failed')),
  close_reason           TEXT,
  version                INT NOT NULL DEFAULT 0,
  requester_gold         BIGINT NOT NULL DEFAULT 0 CHECK (requester_gold >= 0),
  target_gold            BIGINT NOT NULL DEFAULT 0 CHECK (target_gold >= 0),
  requester_locked       BOOLEAN NOT NULL DEFAULT false,
  target_locked          BOOLEAN NOT NULL DEFAULT false,
  requester_confirmed    BOOLEAN NOT NULL DEFAULT false,
  target_confirmed       BOOLEAN NOT NULL DEFAULT false,
  expires_at             TIMESTAMPTZ NOT NULL,
  created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
  accepted_at            TIMESTAMPTZ,
  closed_at              TIMESTAMPTZ,
  CONSTRAINT trades_distinct_chk CHECK (requester_character_id <> target_character_id),
  CONSTRAINT trades_closed_chk CHECK ((state IN ('pending', 'active')) = (closed_at IS NULL))
);
COMMENT ON TABLE  trades IS '1:1 교환. pending 요청(60초) / active 진행 / completed 교환 완료 / declined 거절 / cancelled 취소 / expired 시간 초과 / failed 확정 때 재검증 실패';
COMMENT ON COLUMN trades.version IS '제안이 바뀔 때만 +1. 잠금·확정 요청은 자신이 본 version을 보내고, 다르면 409';
COMMENT ON COLUMN trades.close_reason IS '닫힌 사유: requester_cancel, target_cancel, timeout, items_changed, gold_changed, gold_cap, hold 등';
CREATE INDEX trades_requester_open ON trades (requester_character_id) WHERE state IN ('pending', 'active');
CREATE INDEX trades_target_open ON trades (target_character_id) WHERE state IN ('pending', 'active');
CREATE INDEX trades_requester_closed ON trades (requester_character_id, closed_at DESC) WHERE closed_at IS NOT NULL;
CREATE INDEX trades_target_closed ON trades (target_character_id, closed_at DESC) WHERE closed_at IS NOT NULL;

CREATE TABLE trade_items (
  id         BIGSERIAL PRIMARY KEY,
  trade_id   BIGINT NOT NULL REFERENCES trades(id),
  side       TEXT NOT NULL CHECK (side IN ('requester', 'target')),
  item_key   TEXT NOT NULL,
  count      INT NOT NULL CHECK (count > 0),
  item_uuid  UUID NOT NULL,
  UNIQUE (trade_id, side, item_key)
);
COMMENT ON TABLE  trade_items IS '교환 제안 품목. 제안을 바꾸면 그 쪽의 행을 모두 지우고 다시 넣는다(닫힌 뒤에는 마지막 제안이 남는다)';
COMMENT ON COLUMN trade_items.item_uuid IS '제안할 때 가리킨 character_items(bag) 행의 uuid. 행이 합쳐지거나 지워져도 기록으로만 남는다';

ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in'));
ALTER TABLE item_ledger DROP CONSTRAINT item_ledger_reason_check;
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
COMMENT ON COLUMN gold_ledger.reason IS '변동 사유. trade_out 교환으로 준 골드(-) / trade_in 교환으로 받은 골드(+), ref = trades.uuid. daily_quest 일일·주간 의뢰 보상(ref = 의뢰 id), raid_gold 레이드 클리어 확정 골드(ref = 던전 판 uuid). 그 밖의 설명은 0002, 0007, 0021 참고';
COMMENT ON COLUMN item_ledger.reason IS '변동 사유(경로). trade_out·trade_in 교환으로 준·받은 아이템(ref = trades.uuid). dungeon_card 카드 지급(레이드는 ref = 판 uuid:카드번호) / raid_shop_cost 레이드 상점 재료 차감 / raid_shop_result 레이드 상점 장비 지급. 그 밖의 설명은 0002, 0007, 0019, 0020, 0026 참고';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0029 DOWN과 같은 방식).
ALTER TABLE gold_ledger DISABLE TRIGGER gold_ledger_append_only;
DELETE FROM gold_ledger WHERE reason IN ('trade_out', 'trade_in');
ALTER TABLE gold_ledger ENABLE TRIGGER gold_ledger_append_only;
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason IN ('trade_out', 'trade_in');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest'));
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
                    'raid_shop_cost', 'raid_shop_result'));
DROP TABLE IF EXISTS trade_items;
DROP TABLE IF EXISTS trades;
