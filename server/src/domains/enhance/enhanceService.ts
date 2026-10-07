// 강화: C# Equipment.TryEnhance / EnhanceRules와 같은 규칙을 서버가 굴린다.
import { randomUUID } from 'node:crypto';
import { assertNoHold } from '../antiabuse/holds';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { keyAt, parseItemKey } from '../../utils/itemKey';
import { getRng } from '../../utils/rng';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './enhanceRepository';
import type { EnhanceBody, TicketBody } from './enhanceValidation';

/** 파괴된 착용 무기를 시작 무기로 다시 채울지. C# Equipment.FixSlots는 전사를 뺀다(cls != Warrior) */
export function reissuesStarterWeapon(cls: string): boolean {
  return cls !== 'warrior';
}

const WEAPON_SLOT = 0;

export function enhance(accountId: number, characterUuid: string, body: EnhanceBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/enhance',
    requestId,
    payload,
    handler: (ctx) => processEnhance(ctx, body),
  });
}

const invalidTarget = () => new AppError(422, '강화할 수 없는 대상입니다.', 'ENHANCE_INVALID_TARGET');

async function processEnhance(ctx: EconCtx, body: EnhanceBody) {
  // 9단계 12.5: 경제 정지 중에는 강화할 수 없다
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
  const eco = getGameData().economy;
  const en = eco.enhance;
  const wornSlot = 'worn_slot' in body.target ? body.target.worn_slot : null;

  // 1. 대상 키: 착용 슬롯의 행 또는 가방 스택 수량 >= 1
  const key = wornSlot !== null ? await ctx.wornKey(wornSlot) : (body.target as { bag_key: string }).bag_key;
  if (!key) throw invalidTarget();
  if (wornSlot === null && (await ctx.stackCount('bag', key)) < 1) throw invalidTarget();
  const parsed = parseItemKey(key);
  const levels = parsed ? en.steps.get(parsed.base) : undefined;
  const item = parsed ? eco.shop.equipment.get(parsed.base) : undefined;
  if (!parsed || !levels || !item) throw invalidTarget();

  // 2. 최대 레벨
  const level = parsed.level;
  if (level >= en.maxEnhance) throw new AppError(422, '이미 최대 강화입니다.', 'ENHANCE_MAX_LEVEL');

  // 3. 시도 정보(표 확률 + 천장, 실패 규칙, 비용)
  const row = levels[level];
  if (!row) throw invalidTarget();
  const pityBefore = row.pity ? await repo.getPity(ctx.client, ctx.char.id, key) : 0;
  const successPercent = Math.min(100, row.successPercent + pityBefore);
  const hasTicket = (await ctx.stackCount('bag', en.ticketItem)) > 0;
  // 시작 장비는 무료로 다시 받으므로 보호권을 쓰지 않는다
  const usesTicket = row.failure === 'Destroy' && hasTicket && !item.starter;
  const cost = { gold: row.gold, bone: row.bone, ore: row.ore, essence: row.essence };

  // 4. 비용 확인: 골드와 재료(가방만, 창고는 안 쓴다)
  const mats: [string, number][] = [
    [en.materials.bone, cost.bone],
    [en.materials.ore, cost.ore],
    [en.materials.essence, cost.essence],
  ];
  const have = { gold: ctx.gold, bone: 0, ore: 0, essence: 0 };
  let enough = ctx.gold >= cost.gold;
  for (const [i, name] of (['bone', 'ore', 'essence'] as const).entries()) {
    const [itemKey, need] = mats[i] as [string, number];
    have[name] = need > 0 ? await ctx.stackCount('bag', itemKey) : 0;
    if (need > 0 && have[name] < need) enough = false;
  }
  if (!enough) {
    throw new AppError(422, '골드나 재료가 모자랍니다.', 'ENHANCE_NOT_ENOUGH', { need: cost, have });
  }

  // 5. 비용 지급 후 서버가 굴린다. roll < 성공 확률이면 성공
  const logId = randomUUID();
  await ctx.changeGold(-cost.gold, 'enhance_cost', logId);
  for (const [itemKey, need] of mats) {
    if (need > 0) await ctx.removeItem('bag', itemKey, need, 'enhance_cost', logId);
  }
  const roll = getRng().int(0, en.rollRange);
  const success = roll < successPercent;

  // 6. 결과 적용
  let outcome: 'success' | 'keep' | 'drop3' | 'destroyed' | 'protected';
  let newLevel = level;
  let newKey: string | null = key;
  let pityAfter = pityBefore;
  let ticketUsed = false;
  if (success) {
    outcome = 'success';
    newLevel = level + 1;
    newKey = keyAt(parsed.base, newLevel);
    if (row.pity) await repo.clearPity(ctx.client, ctx.char.id, key);
    pityAfter = 0;
  } else {
    if (row.pity) {
      pityAfter = Math.min(en.maxPity, pityBefore + en.pityPerFailure);
      await repo.setPity(ctx.client, ctx.char.id, key, pityAfter, ctx.now);
    }
    if (row.failure === 'Keep') {
      outcome = 'keep';
    } else if (row.failure === 'Drop3') {
      outcome = 'drop3';
      newLevel = Math.max(0, level - en.drop3Levels);
      newKey = keyAt(parsed.base, newLevel);
    } else {
      newLevel = 0;
      ticketUsed = usesTicket && (await ctx.removeItem('bag', en.ticketItem, 1, 'enhance_cost', logId)) !== null;
      outcome = ticketUsed ? 'protected' : 'destroyed';
      newKey = ticketUsed ? parsed.base : null;
    }
  }

  if (newKey !== key) await replaceTarget(ctx, wornSlot, key, newKey, logId);
  // 파괴된 착용 무기의 빈 자리: 시작 무기 재지급(전사 제외는 C# FixSlots와 같다)
  if (outcome === 'destroyed' && wornSlot === WEAPON_SLOT && reissuesStarterWeapon(ctx.char.class)) {
    const gear = getGameData().starter.gear[ctx.char.class];
    if (gear) await ctx.wornPut(gear.slot, gear.itemKey, 'enhance_result', logId);
  }

  await repo.insertEnhanceLog(ctx.client, {
    uuid: logId,
    characterId: ctx.char.id,
    requestId: ctx.requestId,
    fromKey: key,
    toKey: newKey,
    targetLocation: wornSlot !== null ? 'worn' : 'bag',
    targetSlot: wornSlot,
    outcome,
    roll,
    successPercent,
    pityBefore,
    pityAfter,
    gold: cost.gold,
    bone: cost.bone,
    ore: cost.ore,
    essence: cost.essence,
    ticketUsed,
    createdAt: ctx.now,
  });

  return {
    status: 200,
    data: {
      outcome,
      old_key: key,
      new_key: newKey,
      old_level: level,
      new_level: newKey === null ? 0 : newLevel,
      roll,
      success_percent: successPercent,
      cost: { ...cost, ticket_used: ticketUsed },
      pity: pityAfter,
      delta: ctx.delta(),
    },
  };
}

/** 대상의 키를 바꾼다(newKey null = 사라짐). 원장은 enhance_result(옛 키 -1, 새 키 +1) */
export async function replaceTarget(
  ctx: EconCtx,
  wornSlot: number | null,
  oldKey: string,
  newKey: string | null,
  logId: string,
): Promise<void> {
  if (wornSlot !== null) {
    if (newKey === null) await ctx.wornRemove(wornSlot, oldKey, 'enhance_result', logId);
    else await ctx.wornReplace(wornSlot, oldKey, newKey, 'enhance_result', logId);
    return;
  }
  // 강화해도 귀속은 소모한 행 그대로(파괴·+0 초기화도 같다)
  const consumed = await ctx.removeItem('bag', oldKey, 1, 'enhance_result', logId);
  if (newKey !== null && consumed !== null) await ctx.addConsumed('bag', newKey, consumed, 'enhance_result', logId);
}

// ---------- 강화권(14단계): 고른 장비의 강화 단계를 그 단계로 바로 올린다. 실패·파괴 없음, 비용 없음 ----------

export function applyTicket(accountId: number, characterUuid: string, body: TicketBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/enhance/ticket',
    requestId,
    payload,
    handler: (ctx) => processTicket(ctx, body),
  });
}

async function processTicket(ctx: EconCtx, body: TicketBody) {
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
  const eco = getGameData().economy;
  const en = eco.enhance;
  const ticket = eco.items.get(body.ticket_key);
  if (!ticket || ticket.use !== 'EnhanceTicket' || ticket.power < 1) {
    throw new AppError(422, '강화권이 아닙니다.', 'NOT_A_TICKET');
  }
  const target = ticket.power;
  const parsed = parseItemKey(body.gear_key);
  if (!parsed || !en.steps.has(parsed.base) || !eco.shop.equipment.has(parsed.base) || target > en.maxEnhance) throw invalidTarget();

  // 대상: 가방 스택 우선, 없으면 이 캐릭터가 착용 중인 슬롯(0~5)
  let wornSlot: number | null = null;
  if ((await ctx.stackCount('bag', body.gear_key)) < 1) {
    for (let slot = 0; slot <= 5 && wornSlot === null; slot++) {
      if ((await ctx.wornKey(slot)) === body.gear_key) wornSlot = slot;
    }
    if (wornSlot === null) throw invalidTarget();
  }
  if (parsed.level >= target) {
    throw new AppError(409, '이미 같거나 더 높은 강화 단계입니다.', 'ALREADY_HIGHER', { have: parsed.level, ticket: target });
  }
  const haveTicket = await ctx.stackCount('bag', body.ticket_key);
  if (haveTicket < 1) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have: haveTicket });

  const logId = randomUUID();
  const newKey = keyAt(parsed.base, target);
  if ((await ctx.removeItem('bag', body.ticket_key, 1, 'enhance_ticket', logId)) === null) {
    throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have: 0 });
  }
  // 옛 단계에 쌓인 천장은 의미를 잃는다(강화 성공 때와 같이 지운다)
  const pityBefore = await repo.getPity(ctx.client, ctx.char.id, body.gear_key);
  await repo.clearPity(ctx.client, ctx.char.id, body.gear_key);
  await replaceTarget(ctx, wornSlot, body.gear_key, newKey, logId);
  await repo.insertEnhanceLog(ctx.client, {
    uuid: logId,
    characterId: ctx.char.id,
    requestId: ctx.requestId,
    fromKey: body.gear_key,
    toKey: newKey,
    targetLocation: wornSlot !== null ? 'worn' : 'bag',
    targetSlot: wornSlot,
    outcome: 'ticket',
    roll: 0,
    successPercent: 100,
    pityBefore,
    pityAfter: 0,
    gold: 0,
    bone: 0,
    ore: 0,
    essence: 0,
    ticketUsed: false,
    createdAt: ctx.now,
  });
  return { status: 200, data: { gear_key_before: body.gear_key, gear_key_after: newKey, delta: ctx.delta() } };
}
