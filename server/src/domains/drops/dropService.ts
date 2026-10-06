import { runEconomy, type StoredResult } from '../economy/economyService';
import { assertActionPresence } from '../antiabuse/killPresence';
import * as econRepo from '../economy/economyRepository';
import * as repo from './dropRepository';
import type { ClaimBody } from './dropValidation';

const ENDPOINT = 'POST /characters/:uuid/drops/claim';

type ClaimResult = 'claimed' | 'already_claimed' | 'expired' | 'not_found';

export function claimDrops(accountId: number, characterUuid: string, body: ClaimBody): Promise<StoredResult> {
  const { request_id: requestId, drop_ids: ids } = body;
  const uniqueIds = [...new Set(ids)];
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: ENDPOINT,
    requestId,
    payload: { drop_ids: ids },
    handler: async (ctx) => {
      await assertActionPresence(ctx, 'drop_claim', null);
      const claimed = await repo.claimOpen(ctx.client, ctx.char.id, uniqueIds, ctx.now);
      const result = new Map<string, ClaimResult>();
      for (const d of claimed) {
        // 드롭마다 원장 한 줄(ref = 드롭 uuid). gold_ledger/item_ledger의 drop_uq가 중복 지급을 한 번 더 막는다
        if (d.item_key === 'gold') await ctx.changeGold(d.count, 'drop_claim', d.uuid);
        else await ctx.addItem('bag', d.item_key, d.count, 'drop_claim', d.uuid);
        result.set(d.uuid, 'claimed');
      }

      // 지급되지 않은 id를 분류한다. 남의 것과 없는 것은 구별할 수 없게 둘 다 not_found
      const rest = uniqueIds.filter((id) => !result.has(id));
      let foreign = 0;
      if (rest.length > 0) {
        const states = new Map((await repo.findDrops(ctx.client, rest)).map((s) => [s.uuid, s] as const));
        for (const id of rest) {
          const s = states.get(id);
          if (!s || Number(s.character_id) !== ctx.char.id) {
            if (s) foreign++;
            result.set(id, 'not_found');
          } else if (s.claimed_at) result.set(id, 'already_claimed');
          else result.set(id, 'expired');
        }
      }
      if (foreign > 0) {
        await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'drop_foreign', 2, { count: foreign });
      }

      return {
        status: 200,
        data: {
          results: uniqueIds.map((id) => ({ id, result: result.get(id) as ClaimResult })),
          delta: ctx.delta(),
        },
      };
    },
  });
}
