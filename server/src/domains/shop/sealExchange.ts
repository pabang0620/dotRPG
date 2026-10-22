// 아카브 교환(챕터 3, Docs/ch3/PROGRESSION_AND_GEAR.md): 침식 결정 5 + 골드 10,000 -> 잿빛 인장 1, 계정당 주 2회.
// 2026-10-23 7단계: 20,000이면 3장 무자본 비율(공개 시점 잔고 기준)이 0.838이라 10,000으로 내렸다(theory_gold.py --to 50 --mini 20 → 0.854).
// 무자본 플레이어가 카마츠 레이드 없이도 계승 재료를 천천히 모으는 길이고, 쌓인 골드를 흡수하는 싱크다.
import { randomUUID } from 'node:crypto';
import { assertOpen } from '../../gamedata/chapterGate';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { weekStartOf } from '../sweep/weeklyCounter';
import type { ExchangeBody } from './shopValidation';

export const SEAL_EXCHANGE = { id: 'akab_seal', shards: 5, gold: 10_000, seals: 1, weeklyLimit: 2 } as const;

export function exchange(accountId: number, characterUuid: string, body: ExchangeBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/shop/exchange',
    requestId,
    payload,
    handler: async (ctx) => {
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      const inherit = getGameData().economy.enhance.inheritTiers;
      if (!inherit?.sealItem) throw new AppError(503, '교환을 준비 중입니다.', 'EXCHANGE_UNAVAILABLE');
      assertOpen('item', inherit.sealItem);
      const x = SEAL_EXCHANGE;
      const shards = await ctx.stackCount('bag', inherit.shardItem);
      if (shards < x.shards || ctx.gold < x.gold) {
        throw new AppError(422, '침식 결정이나 골드가 모자랍니다.', 'EXCHANGE_NOT_ENOUGH', {
          need: { shards: x.shards, gold: x.gold },
          have: { shards, gold: ctx.gold },
        });
      }
      // 계정 주간 카운터(조건부 UPDATE: 한도를 넘으면 행이 바뀌지 않는다). 계정 행은 runEconomy가 잠근다
      const weekStart = weekStartOf(ctx.now);
      const bumped = await ctx.client.query(
        `INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES ($1, $2, 'seal_exchange', 1)
         ON CONFLICT (account_id, week_start, kind) DO UPDATE SET used = account_week_counters.used + 1, updated_at = now()
           WHERE account_week_counters.used + 1 <= $3`,
        [ctx.char.accountId, weekStart, x.weeklyLimit],
      );
      if ((bumped.rowCount ?? 0) !== 1) {
        throw new AppError(422, `이번 주 교환은 ${x.weeklyLimit}번까지입니다.`, 'EXCHANGE_WEEKLY_LIMIT', { limit: x.weeklyLimit });
      }
      const used = await ctx.client.query<{ used: number }>(
        "SELECT used FROM account_week_counters WHERE account_id = $1 AND week_start = $2 AND kind = 'seal_exchange'",
        [ctx.char.accountId, weekStart],
      );
      const logId = randomUUID();
      await ctx.changeGold(-x.gold, 'seal_exchange', logId);
      await ctx.removeItem('bag', inherit.shardItem, x.shards, 'raid_shop_cost', logId);
      await ctx.addItem('bag', inherit.sealItem, x.seals, 'raid_shop_result', logId);
      return {
        status: 200,
        data: {
          exchange_id: x.id,
          used_this_week: used.rows[0]?.used ?? 1,
          weekly_limit: x.weeklyLimit,
          delta: ctx.delta(),
        },
      };
    },
  });
}
