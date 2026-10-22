// 장비 승급: 같은 부위의 다음 등급 장비로 바꾼다(강화 수치는 그대로). 재료 "고대의 핵"은 레이드 클리어 보상으로만 나온다.
// 표는 C# PromoteRules에서 내보낸 enhance.json의 promote(서버와 클라이언트가 같은 표를 쓴다).
import { assertOpen } from '../../gamedata/chapterGate';
import { randomUUID } from 'node:crypto';
import { assertNoHold } from '../antiabuse/holds';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { keyAt, parseItemKey } from '../../utils/itemKey';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import type { PromoteBody } from './promoteValidation';
import { moveGachaCredit } from '../starshop/cashValueService';
import * as enhanceRepo from '../enhance/enhanceRepository';

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
  // 9단계 12.5: 경제 정지 중에는 승급할 수 없다
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
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
  // 챕터 3 장비(원본이든 결과든)는 3장이 열릴 때까지 승급하지 못한다
  assertOpen('item', key);
  assertOpen('item', row.to);

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
  let promotedBind: string | null = null;
  if (wornSlot !== null) {
    const w = await ctx.client.query<{ bind: string }>(
      "SELECT bind FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2",
      [ctx.char.id, wornSlot],
    );
    promotedBind = w.rows[0]?.bind ?? null;
    await ctx.wornReplace(wornSlot, key, newKey, 'promote_result', logId);
  } else {
    // 귀속은 소모한 행 그대로(강화와 같다)
    const consumed = await ctx.removeItem('bag', key, 1, 'promote_result', logId);
    if (consumed !== null) await ctx.addConsumed('bag', newKey, consumed, 'promote_result', logId);
    promotedBind = consumed?.[0]?.bind ?? null;
  }
  // 0044: 뽑기 사본(계정 귀속)을 승급하면 뽑기 몫도 새 기본 id로 옮긴다(상점 판매 금지·분해 허용이 따라간다)
  if (promotedBind === 'account') await moveGachaCredit(ctx.client, ctx.char.id, parsed.base, row.to, 1);

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

// ---------------------------------------------------------------- 챕터 3 계승

/**
 * 장비 계승(챕터 3, Docs/ch3/PROGRESSION_AND_GEAR.md 4절): Lv.40 유니크·레전더리를 같은 부위·등급의 Lv.50 장비로 바꾼다.
 * 강화 수치는 그대로(결과 키 = 새 기본 id + 같은 강화 단계), 재료는 고대의 핵·침식 결정·잿빛 인장·골드(enhance.json promote.inherit).
 * 3장이 닫혀 있으면 422 CHAPTER_LOCKED(결과 장비가 챕터 3 태그). 보호권 이력(강화 실패 누적)은 새 키로 옮긴다.
 */
export function inherit(accountId: number, characterUuid: string, body: PromoteBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/promote/inherit',
    requestId,
    payload,
    handler: (ctx) => processInherit(ctx, body),
  });
}

const invalidInherit = () => new AppError(422, '계승할 수 없는 장비입니다.', 'INHERIT_INVALID_TARGET');

async function processInherit(ctx: EconCtx, body: PromoteBody) {
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
  const table = getGameData().economy.enhance.inherit;
  if (!table) throw new AppError(503, '계승을 준비 중입니다.', 'INHERIT_UNAVAILABLE');
  const wornSlot = 'worn_slot' in body.target ? body.target.worn_slot : null;

  const key = wornSlot !== null ? await ctx.wornKey(wornSlot) : (body.target as { bag_key: string }).bag_key;
  if (!key) throw invalidInherit();
  if (wornSlot === null && (await ctx.stackCount('bag', key)) < 1) throw invalidInherit();
  const parsed = parseItemKey(key);
  // 표에 있는 것만(Lv.40 유니크·레전더리). 커먼~에픽·다른 단계는 표에 없어 여기서 거절된다
  const row = parsed ? table.rows.get(parsed.base) : undefined;
  if (!parsed || !row) throw invalidInherit();
  assertOpen('item', row.to);

  const [cores, shards, seals] = await Promise.all([
    ctx.stackCount('bag', table.coreItem),
    ctx.stackCount('bag', table.shardItem),
    ctx.stackCount('bag', table.sealItem),
  ]);
  if (cores < row.cores || shards < row.shards || seals < row.seals || ctx.gold < row.gold) {
    throw new AppError(422, '계승 재료나 골드가 모자랍니다.', 'INHERIT_NOT_ENOUGH', {
      need: { cores: row.cores, shards: row.shards, seals: row.seals, gold: row.gold },
      have: { cores, shards, seals, gold: ctx.gold },
    });
  }

  const logId = randomUUID();
  if (row.gold > 0) await ctx.changeGold(-row.gold, 'inherit', logId);
  if (row.cores > 0) await ctx.removeItem('bag', table.coreItem, row.cores, 'promote_cost', logId);
  if (row.shards > 0) await ctx.removeItem('bag', table.shardItem, row.shards, 'promote_cost', logId);
  if (row.seals > 0) await ctx.removeItem('bag', table.sealItem, row.seals, 'promote_cost', logId);

  const newKey = keyAt(row.to, parsed.level);
  let bind: string | null = null;
  if (wornSlot !== null) {
    const w = await ctx.client.query<{ bind: string }>(
      "SELECT bind FROM character_items WHERE character_id = $1 AND location = 'worn' AND slot = $2",
      [ctx.char.id, wornSlot],
    );
    bind = w.rows[0]?.bind ?? null;
    await ctx.wornReplace(wornSlot, key, newKey, 'promote_result', logId);
  } else {
    // 귀속은 소모한 행 그대로(승급과 같다)
    const consumed = await ctx.removeItem('bag', key, 1, 'promote_result', logId);
    if (consumed !== null) await ctx.addConsumed('bag', newKey, consumed, 'promote_result', logId);
    bind = consumed?.[0]?.bind ?? null;
  }
  if (bind === 'account') await moveGachaCredit(ctx.client, ctx.char.id, parsed.base, row.to, 1);

  // 보호권 이력(강화 실패 누적) 이전: 새 키는 둘 중 큰 값, 옛 키는 같은 장비가 더 남아 있지 않을 때만 지운다
  const oldPity = await enhanceRepo.getPity(ctx.client, ctx.char.id, key);
  if (oldPity > 0) {
    const newPity = await enhanceRepo.getPity(ctx.client, ctx.char.id, newKey);
    await enhanceRepo.setPity(ctx.client, ctx.char.id, newKey, Math.max(oldPity, newPity), ctx.now);
    const stillBag = await ctx.stackCount('bag', key);
    const stillWorn = await ctx.client.query(
      "SELECT 1 FROM character_items WHERE character_id = $1 AND location = 'worn' AND item_key = $2 LIMIT 1",
      [ctx.char.id, key],
    );
    if (stillBag === 0 && stillWorn.rowCount === 0) await enhanceRepo.clearPity(ctx.client, ctx.char.id, key);
  }

  return {
    status: 200,
    data: {
      old_key: key,
      new_key: newKey,
      cost: { cores: row.cores, shards: row.shards, seals: row.seals, gold: row.gold },
      pity: oldPity,
      delta: ctx.delta(),
    },
  };
}
