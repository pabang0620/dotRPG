// 장비 승급: 같은 부위의 다음 등급 장비로 바꾼다(강화 수치는 그대로). 재료 "고대의 핵"은 레이드 클리어 보상으로만 나온다.
// 표는 C# PromoteRules에서 내보낸 enhance.json의 promote(서버와 클라이언트가 같은 표를 쓴다).
import { randomUUID } from 'node:crypto';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { keyAt, parseItemKey } from '../../utils/itemKey';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import type { PromoteBody } from './promoteValidation';

export function promote(accountId: number, characterUuid: string, body: PromoteBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/promote',
    requestId,
    payload,
    handler: (ctx) => processPromote(ctx, body),
  });
}

const invalidTarget = () => new AppError(422, '승급할 수 없는 장비입니다.', 'PROMOTE_INVALID_TARGET');

async function processPromote(ctx: EconCtx, body: PromoteBody) {
  const eco = getGameData().economy;
  const table = eco.enhance.promote;
  if (!table) throw new AppError(503, '승급을 준비 중입니다.', 'PROMOTE_UNAVAILABLE');
  const wornSlot = 'worn_slot' in body.target ? body.target.worn_slot : null;

  const key = wornSlot !== null ? await ctx.wornKey(wornSlot) : (body.target as { bag_key: string }).bag_key;
  if (!key) throw invalidTarget();
  if (wornSlot === null && (await ctx.stackCount('bag', key)) < 1) throw invalidTarget();
  const parsed = parseItemKey(key);
  const row = parsed ? table.rows.get(parsed.base) : undefined;
  if (!parsed || !row) throw invalidTarget();

  const cores = await ctx.stackCount('bag', table.coreItem);
  if (cores < row.cores || ctx.gold < row.gold) {
    throw new AppError(422, '고대의 핵이나 골드가 모자랍니다.', 'PROMOTE_NOT_ENOUGH', {
      need: { cores: row.cores, gold: row.gold },
      have: { cores, gold: ctx.gold },
    });
  }

  const logId = randomUUID();
  if (row.gold > 0) await ctx.changeGold(-row.gold, 'promote_cost', logId);
  await ctx.removeItem('bag', table.coreItem, row.cores, 'promote_cost', logId);
  const newKey = keyAt(row.to, parsed.level);
  if (wornSlot !== null) await ctx.wornReplace(wornSlot, key, newKey, 'promote_result', logId);
  else {
    // 귀속은 소모한 행 그대로(강화와 같다)
    const consumed = await ctx.removeItem('bag', key, 1, 'promote_result', logId);
    if (consumed !== null) await ctx.addConsumed('bag', newKey, consumed, 'promote_result', logId);
  }

  return {
    status: 200,
    data: {
      old_key: key,
      new_key: newKey,
      cost: { cores: row.cores, gold: row.gold },
      delta: ctx.delta(),
    },
  };
}
