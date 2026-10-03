// 물약 사용, 창고 이동, 장착·해제: 서버가 가진 수량 검증과 원장만 한다.
// HP·MP는 서버가 모른다(회복은 어뷰징 이득이 없다). 서버는 소모만 맡는다.
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { getRateLimitStore } from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { parseItemKey } from '../../utils/itemKey';
import type { EconCtx } from '../economy/economyContext';
import type { Consumed } from '../economy/economyRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './inventoryRepository';
import type { EquipBody, StorageBody, UnequipBody, UseBody } from './inventoryValidation';

// EquipSlot: Weapon 0, Necklace 1, Ring1 2, Ring2 3, Top 4, Bottom 5
const SLOT_OF: Record<string, number> = { Weapon: 0, Necklace: 1, Top: 4, Bottom: 5 };
const RING1 = 2;
const RING2 = 3;

export function useItem(accountId: number, characterUuid: string, body: UseBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/items/use',
    requestId,
    payload,
    handler: async (ctx) => {
      const usable = getGameData().economy.config.usableItems.some((u) => u.id === body.item_id);
      if (!usable) throw new AppError(422, '사용할 수 없는 아이템입니다.', 'NOT_USABLE');
      const have = await ctx.stackCount('bag', body.item_id);
      if (have < 1) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have });

      // 같은 아이템의 직전 사용과 최소 간격(C# 물약 재사용 대기 0.6초에 지연 여유)
      const gap = getConfig().policy.itemUseMinGapMs;
      const gapKey = `item-use:${ctx.char.id}:${body.item_id}`;
      const store = getRateLimitStore();
      if (gap > 0 && store.count(gapKey, gap).count >= 1) {
        throw new AppError(429, '같은 아이템을 너무 빨리 사용했습니다.', 'ITEM_COOLDOWN', { retry_after_sec: 1 });
      }
      await ctx.removeItem('bag', body.item_id, 1, 'use_item', body.item_id);
      if (gap > 0) store.add(gapKey, gap);
      return { status: 200, data: { item_id: body.item_id, delta: ctx.delta() } };
    },
  });
}

// ---------- 창고 ----------

export function moveStorage(accountId: number, characterUuid: string, body: StorageBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/storage/move',
    requestId,
    payload,
    handler: async (ctx) => {
      const capacity = getGameData().economy.config.storageCapacity;
      const results: { item_key: string; to: string; moved: number; error: string | null }[] = [];
      // 항목마다 독립이다. 하나가 실패해도 나머지는 진행한다(C# DepositMaterials가 가득 차면 건너뛰는 것과 같다)
      for (const m of body.moves) {
        const from = m.to === 'storage' ? 'bag' : 'storage';
        const have = await ctx.stackCount(from, m.item_key);
        const n = m.count === 'all' ? have : m.count;
        const fail = (error: string) => results.push({ item_key: m.item_key, to: m.to, moved: 0, error });
        if (n < 1 || have < n) {
          fail('NOT_ENOUGH_ITEMS');
          continue;
        }
        if (m.to === 'storage' && (await ctx.stackCount('storage', m.item_key)) === 0) {
          if ((await repo.storageKindCount(ctx.client, ctx.char.id)) >= capacity) {
            fail('STORAGE_FULL');
            continue;
          }
        }
        const consumed = await ctx.removeItem(from, m.item_key, n, 'storage_move', m.item_key);
        if (consumed === null) {
          fail('NOT_ENOUGH_ITEMS');
          continue;
        }
        await ctx.addConsumed(m.to, m.item_key, consumed, 'storage_move', m.item_key);
        results.push({ item_key: m.item_key, to: m.to, moved: n, error: null });
      }
      return { status: 200, data: { results, delta: ctx.delta() } };
    },
  });
}

// ---------- 장착·해제 ----------

export function equip(accountId: number, characterUuid: string, body: EquipBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/equipment/equip',
    requestId,
    payload,
    handler: (ctx) => processEquip(ctx, body),
  });
}

/** Equipment.TargetSlotFor: 반지 슬롯 1이 비었으면 1, 슬롯 1이 찼고 슬롯 2가 비었으면 2, 둘 다 차 있으면 1(교체) */
async function targetSlot(ctx: EconCtx, category: string, wanted: number | undefined): Promise<number> {
  if (category !== 'Ring') {
    if (wanted !== undefined) throw new AppError(422, '슬롯이 맞지 않습니다.', 'SLOT_MISMATCH');
    return SLOT_OF[category] as number;
  }
  if (wanted !== undefined) return wanted;
  const r1 = await ctx.wornKey(RING1);
  if (r1 === null) return RING1;
  return (await ctx.wornKey(RING2)) === null ? RING2 : RING1;
}

async function processEquip(ctx: EconCtx, body: EquipBody) {
  const eco = getGameData().economy;
  const have = await ctx.stackCount('bag', body.item_key);
  if (have < 1) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have });
  const p = parseItemKey(body.item_key);
  const item = p ? eco.shop.equipment.get(p.base) : undefined;
  if (!item) throw new AppError(422, '장비가 아닙니다.', 'NOT_EQUIPMENT');
  if (item.classOnly !== null && item.classOnly !== ctx.char.class) {
    throw new AppError(422, '이 직업은 쓸 수 없는 장비입니다.', 'CLASS_MISMATCH');
  }
  const slot = await targetSlot(ctx, item.category, body.slot);

  const consumed = await ctx.removeItem('bag', body.item_key, 1, 'equip', body.item_key);
  const previous = await ctx.wornKey(slot);
  if (previous !== null) {
    const prevBind = await ctx.wornRemove(slot, previous, 'equip', previous);
    await ctx.addItem('bag', previous, 1, 'equip', previous, prevBind);
  }
  await ctx.wornPut(slot, body.item_key, 'equip', body.item_key, (consumed as Consumed[])[0]?.bind);
  return { status: 200, data: { delta: ctx.delta() } };
}

export function unequip(accountId: number, characterUuid: string, body: UnequipBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/equipment/unequip',
    requestId,
    payload,
    handler: async (ctx) => {
      const key = await ctx.wornKey(body.slot);
      if (key === null) throw new AppError(422, '비어 있는 슬롯입니다.', 'SLOT_EMPTY');
      // C# FixSlots는 전사가 아니면 빈 무기 칸에 시작 무기를 채운다: 서버에서는 무기 해제를 막아 두 쪽을 같게 둔다
      if (body.slot === 0 && ctx.char.class !== 'warrior') {
        throw new AppError(422, '마법사는 무기를 뺄 수 없습니다. 다른 무기를 장착하면 바뀝니다.', 'WEAPON_REQUIRED');
      }
      const bind = await ctx.wornRemove(body.slot, key, 'unequip', key);
      await ctx.addItem('bag', key, 1, 'unequip', key, bind);
      return { status: 200, data: { delta: ctx.delta() } };
    },
  });
}
