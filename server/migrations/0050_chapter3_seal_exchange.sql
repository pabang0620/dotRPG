-- 0050_chapter3_seal_exchange: 챕터 3 아카브 교환(2026-10-22, Docs/ch3/IMPLEMENTATION_ORDER.md). 선행: 0049_gear_inherit.
--   골드 사유 seal_exchange(침식 결정 -> 잿빛 인장)와 계정 주간 카운터 seal_exchange(주 2회).
--   장비 계승 사유 inherit(40 -> 50 포함, 재료도 item_ledger inherit)는 0049_gear_inherit가 이미 넣었다. ch3 브랜치의 옛 0049_chapter3_inherit를 옮긴 것이다.
-- ============ UP ============
ALTER TABLE gold_ledger DROP CONSTRAINT IF EXISTS gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in', 'guild_create',
                    'inherit', 'seal_exchange'));
COMMENT ON COLUMN gold_ledger.reason IS '0049의 값 + seal_exchange(아카브 교환: 침식 결정 -> 잿빛 인장)';
DO $$
DECLARE c TEXT;
BEGIN
  SELECT conname INTO c FROM pg_constraint
   WHERE conrelid = 'account_week_counters'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%sweep_buy%';
  IF c IS NOT NULL THEN EXECUTE format('ALTER TABLE account_week_counters DROP CONSTRAINT %I', c); END IF;
END $$;
ALTER TABLE account_week_counters ADD CONSTRAINT account_week_counters_kind_check
  CHECK (kind IN ('sweep_buy', 'direct_clear', 'activity_claim', 'seal_exchange'));
COMMENT ON COLUMN account_week_counters.kind IS 'sweep_buy, direct_clear, activity_claim + seal_exchange(아카브 교환 횟수, 주 2회)';
-- ============ DOWN ============
DELETE FROM account_week_counters WHERE kind = 'seal_exchange';
ALTER TABLE account_week_counters DROP CONSTRAINT account_week_counters_kind_check;
ALTER TABLE account_week_counters ADD CONSTRAINT account_week_counters_kind_check
  CHECK (kind IN ('sweep_buy', 'direct_clear', 'activity_claim'));
ALTER TABLE gold_ledger DROP CONSTRAINT gold_ledger_reason_check;
ALTER TABLE gold_ledger ADD CONSTRAINT gold_ledger_reason_check
  CHECK (reason IN ('starter', 'drop_claim', 'quest_reward', 'shop_buy', 'shop_sell', 'enhance_cost', 'dungeon_card',
                    'auction_deposit', 'auction_bid', 'auction_buyout', 'mail_claim',
                    'promote_cost', 'admin_clawback', 'sweep_ticket_buy',
                    'raid_gold', 'daily_quest', 'trade_out', 'trade_in', 'guild_create',
                    'inherit'));
