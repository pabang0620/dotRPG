-- 0055_daily_chests: 일일·주간 완료 상자, 연속 완료, 강화 재료 상자(Docs/PLAN_DAILY_REWARDS_2026-10-24.md)
-- 대상: PostgreSQL 14 이상. 선행: 0054_skin_tiers.
--   1. daily_chests: 캐릭터별 기간(일/주)·날짜마다 완료 상자 한 번. 연속 일수(streak)와 지급 내용 스냅샷
--   2. item_ledger.reason 에 daily_chest(완료 상자 지급), fragment_merge(보호권 조각 -> 장비 보호권) 추가
--   3. sealed_pulls.source 에 daily_chest(완료 상자 보너스 굴림), mat_box(강화 재료 상자 열기) 추가

-- ============ UP ============
CREATE TABLE daily_chests (
  character_id  BIGINT NOT NULL REFERENCES characters(id),
  account_id    BIGINT NOT NULL REFERENCES accounts(id),
  period        TEXT NOT NULL CHECK (period IN ('day', 'week')),
  game_day      DATE NOT NULL,
  streak        INT NOT NULL CHECK (streak >= 0),
  step          SMALLINT NOT NULL CHECK (step >= 0),
  level         INT NOT NULL CHECK (level >= 1),
  rewards       JSONB NOT NULL CHECK (jsonb_typeof(rewards) = 'array'),
  skin_gauge    INT NOT NULL DEFAULT 0 CHECK (skin_gauge >= 0),
  rates_version TEXT NOT NULL,
  request_id    UUID NOT NULL,
  claimed_at    TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (character_id, period, game_day)
);
CREATE INDEX daily_chests_gauge_idx ON daily_chests (account_id, period, game_day) WHERE skin_gauge > 0;
COMMENT ON TABLE  daily_chests IS '일일·주간 완료 상자 기록(추가 위주). 그 기간 칸을 모두 청구한 뒤 한 번 연다. PK가 "기간마다 한 번"을 보장한다';
COMMENT ON COLUMN daily_chests.game_day IS 'day: 게임 날짜(06:00 KST). week: 주 시작(목요일 06:00 KST)의 KST 날짜. daily_quests.game_day 와 같은 값';
COMMENT ON COLUMN daily_chests.streak IS 'day: 연속 완료 일수(전날 상자가 있으면 전날 값 + 1, 없으면 1). 전날 상자를 늦게 열면 다음 날 행의 값만 이어 붙인다(보상은 다시 주지 않는다). week: 0';
COMMENT ON COLUMN daily_chests.step IS 'day: 상자 단계 1..주기(연속 일수를 주기로 나눈 자리, 7이면 금 상자). week: 0';
COMMENT ON COLUMN daily_chests.rewards IS '지급 스냅샷 [{item_key, count, source}]. source = fixed 고정 / max_level 만렙 추가 / bonus 보너스 굴림. item_ledger daily_chest 행과 같은 값';
COMMENT ON COLUMN daily_chests.skin_gauge IS '이 상자로 채운 스킨 선택 게이지 칸. 계정당 같은 주에는 한 캐릭터만 0보다 크다';

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
                    'gear_dismantle', 'dismantle_bonus', 'exchange_buy',
                    'inherit', 'daily_chest', 'fragment_merge'));
COMMENT ON COLUMN item_ledger.reason IS '0049의 값 + daily_chest(완료 상자 지급, ref = period:날짜) + fragment_merge(보호권 조각 per개 -1, 장비 보호권 +1)';

ALTER TABLE sealed_pulls DROP CONSTRAINT IF EXISTS sealed_pulls_source_check;
ALTER TABLE sealed_pulls ADD CONSTRAINT sealed_pulls_source_check CHECK (source IN ('shop', 'sealed_item', 'luck_box', 'daily_chest', 'mat_box'));
COMMENT ON COLUMN sealed_pulls.source IS 'shop 캐시샵 뽑기 / sealed_item 봉인된 상자 아이템 / luck_box 확률 상자 / daily_chest 완료 상자 보너스 굴림 / mat_box 강화 재료 상자(rate = 그 수량 구간 확률 %)';

-- ============ DOWN ============
-- 개발 DB 전용. 원장은 추가 전용이라 새 사유 행을 지울 때 트리거를 잠시 끈다(0029 DOWN과 같은 방식).
DELETE FROM sealed_pulls WHERE source IN ('daily_chest', 'mat_box');
ALTER TABLE sealed_pulls DROP CONSTRAINT IF EXISTS sealed_pulls_source_check;
ALTER TABLE sealed_pulls ADD CONSTRAINT sealed_pulls_source_check CHECK (source IN ('shop', 'sealed_item', 'luck_box'));
ALTER TABLE item_ledger DISABLE TRIGGER item_ledger_append_only;
DELETE FROM item_ledger WHERE reason IN ('daily_chest', 'fragment_merge');
ALTER TABLE item_ledger ENABLE TRIGGER item_ledger_append_only;
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
                    'gear_dismantle', 'dismantle_bonus', 'exchange_buy',
                    'inherit'));
DROP TABLE IF EXISTS daily_chests;
