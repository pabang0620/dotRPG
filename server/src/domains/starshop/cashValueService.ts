// 캐시 아이템 가치 보존(0044, Docs/PLAN_CASH_ITEM_VALUE.md): 뽑기 장비 분해, 별빛 교환소, 장비 도감.
// 락 순서: 캐릭터(runEconomy) -> 별조각 지갑 -> 결정 지갑 -> 천장 행. 결정은 별조각·골드로 되돌릴 수 없고 거래할 수 없다.
import type { Queryable } from '../../db/pool';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { parseItemKey } from '../../utils/itemKey';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { getNow } from '../../utils/clock';
import { assertNoHold } from '../antiabuse/holds';
import * as ceilings from '../ceilings/ceilingRepository';
import * as charRepo from '../characters/characterRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as crystals from './crystalWallet';
import {
  CODEX_HEALTH_MAX,
  CODEX_HEALTH_PER,
  CODEX_RARITIES,
  COLLECTIONS,
  COLLECTION_DUP_BONUS,
  DISMANTLE_CRYSTALS,
  EXCHANGE_GOODS,
  EXCHANGE_GOOD_BY_ID,
  GEAR_DISMANTLE,
  GEAR_DISMANTLE_ENHANCE_BONUS,
  SKIN_GAUGE_MAX,
  STAR_COSMETICS,
  STAR_COSMETIC_BY_ID,
  type CosmeticDef,
  type ExchangeGood,
  type Rarity,
} from './starshopDefs';
import * as repo from './starshopRepository';
import * as wallets from './starWallet';

// ---------------- 뽑기 장비 몫·도감 (다른 경로가 부른다) ----------------

/** 뽑기(천장 포함)로 장비를 줄 때 같은 트랜잭션에서 부른다: 상점 판매 금지·분해 허용 몫, 유니크·레전더리면 도감 */
export async function noteGachaGear(db: Queryable, characterId: number, accountId: number, itemKey: string, n: number): Promise<void> {
  const base = parseItemKey(itemKey)?.base ?? itemKey;
  await db.query(
    `INSERT INTO gacha_gear_credits (character_id, base_id, count) VALUES ($1, $2, $3)
     ON CONFLICT (character_id, base_id) DO UPDATE SET count = gacha_gear_credits.count + EXCLUDED.count`,
    [characterId, base, n],
  );
  await recordCodex(db, accountId, [base]);
}

export async function gachaCreditOf(db: Queryable, characterId: number, base: string): Promise<number> {
  const r = await db.query<{ count: number }>('SELECT count FROM gacha_gear_credits WHERE character_id = $1 AND base_id = $2', [characterId, base]);
  return r.rows[0]?.count ?? 0;
}

/**
 * 상점 판매 계획(뽑기 몫이 있는 기본 id): 이 키의 가방 행 중 귀속별로 팔 수 있는 수.
 * 계정 귀속은 그 기본 id의 계정 귀속 전체(모든 위치·강화 단계)가 뽑기 몫을 넘는 만큼만 판다(뽑기 사본은 계정 귀속).
 */
export async function gachaSafeSalePlan(
  db: Queryable,
  characterId: number,
  base: string,
  itemKey: string,
): Promise<{ none: number; character: number; account: number }> {
  const bag = await db.query<{ bind: string; n: string }>(
    "SELECT bind, COALESCE(SUM(count), 0) AS n FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2 GROUP BY bind",
    [characterId, itemKey],
  );
  const inBag = (b: string): number => Number(bag.rows.find((r) => r.bind === b)?.n ?? 0);
  const acc = await db.query<{ n: string }>(
    "SELECT COALESCE(SUM(count), 0) AS n FROM character_items WHERE character_id = $1 AND bind = 'account' AND split_part(item_key, '+', 1) = $2",
    [characterId, base],
  );
  const freeAccount = Math.max(0, Number(acc.rows[0]?.n ?? 0) - (await gachaCreditOf(db, characterId, base)));
  return { none: inBag('none'), character: inBag('character'), account: Math.min(inBag('account'), freeAccount) };
}

/** 승급으로 기본 id가 바뀐 뽑기 사본: 몫을 새 기본 id로 옮긴다(같은 트랜잭션). 옮긴 수 */
export async function moveGachaCredit(db: Queryable, characterId: number, fromBase: string, toBase: string, n: number): Promise<number> {
  if (n <= 0 || fromBase === toBase) return 0;
  const r = await db.query<{ count: number }>(
    'SELECT count FROM gacha_gear_credits WHERE character_id = $1 AND base_id = $2 FOR UPDATE',
    [characterId, fromBase],
  );
  const move = Math.min(n, r.rows[0]?.count ?? 0);
  if (move <= 0) return 0;
  await db.query('UPDATE gacha_gear_credits SET count = count - $3 WHERE character_id = $1 AND base_id = $2', [characterId, fromBase, move]);
  await db.query(
    `INSERT INTO gacha_gear_credits (character_id, base_id, count) VALUES ($1, $2, $3)
     ON CONFLICT (character_id, base_id) DO UPDATE SET count = gacha_gear_credits.count + EXCLUDED.count`,
    [characterId, toBase, move],
  );
  return move;
}

/** 그 기본 id 장비 중 상점에 골드로 팔 수 있는 수: 가진 수(모든 위치·강화 단계) - 뽑기 몫 */
export async function sellableNonGacha(db: Queryable, characterId: number, base: string): Promise<number> {
  const owned = await db.query<{ n: string }>(
    "SELECT COALESCE(SUM(count), 0) AS n FROM character_items WHERE character_id = $1 AND split_part(item_key, '+', 1) = $2",
    [characterId, base],
  );
  return Math.max(0, Number(owned.rows[0]?.n ?? 0) - (await gachaCreditOf(db, characterId, base)));
}

function gearRarity(base: string): string | null {
  return getGameData().economy.shop.equipment.get(base)?.rarity ?? null;
}

/** 유니크·레전더리 기본 id를 도감에 기록한다(이미 있으면 그대로). 새로 기록한 수 */
export async function recordCodex(db: Queryable, accountId: number, bases: string[]): Promise<number> {
  let added = 0;
  for (const b of new Set(bases)) {
    const r = gearRarity(b);
    if (!r || !(CODEX_RARITIES as readonly string[]).includes(r)) continue;
    const ins = await db.query('INSERT INTO gear_codex (account_id, base_id) VALUES ($1, $2) ON CONFLICT DO NOTHING', [accountId, b]);
    added += ins.rowCount ?? 0;
  }
  return added;
}

export async function codexOf(db: Queryable, accountId: number): Promise<string[]> {
  const r = await db.query<{ base_id: string }>('SELECT base_id FROM gear_codex WHERE account_id = $1 ORDER BY created_at, base_id', [accountId]);
  return r.rows.map((x) => x.base_id);
}

export const codexHealth = (entries: number): number => Math.min(CODEX_HEALTH_MAX, entries * CODEX_HEALTH_PER);

// ---------------- 외형 여분 분해의 결정 몫 (starshopSynth.dismantle 이 부른다) ----------------

/** 여분 count개 분해의 결정: 등급 값 x 개수, 그 외형이 등록한 컬렉션 세트에 들어 있으면 +20% */
export function cosmeticCrystals(def: CosmeticDef, count: number, registeredSets: Set<string>): number {
  const inSet = COLLECTIONS.some((c) => registeredSets.has(c.id) && c.members.includes(def.id));
  return Math.floor(DISMANTLE_CRYSTALS[def.rarity] * count * (inSet ? 1 + COLLECTION_DUP_BONUS : 1));
}

// ---------------- 뽑기 장비 분해 ----------------

/** 강화 단계를 반영한 장비 1개 분해 값 */
export function gearDismantleValue(itemKey: string): { crystals: number; items: [string, number][]; rarity: string } | null {
  const k = parseItemKey(itemKey);
  if (!k) return null;
  const rarity = gearRarity(k.base);
  const rule = rarity ? GEAR_DISMANTLE[rarity] : undefined;
  if (!rarity || !rule) return null;
  return { crystals: Math.round(rule.crystals * (1 + GEAR_DISMANTLE_ENHANCE_BONUS * k.level)), items: rule.items, rarity };
}

/** 가방의 뽑기 장비 중 분해할 수 있는 것: 기본 id별 뽑기 몫만큼(강화한 것부터 보여 준다) */
async function dismantlableOf(db: Queryable, characterId: number) {
  const credits = await db.query<{ base_id: string; count: number }>('SELECT base_id, count FROM gacha_gear_credits WHERE character_id = $1 AND count > 0', [characterId]);
  if (credits.rows.length === 0) return [];
  const bag = await db.query<{ item_key: string; n: string }>(
    "SELECT item_key, SUM(count) AS n FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key LIKE 'eq\\_%' GROUP BY item_key",
    [characterId],
  );
  const out: { item_key: string; count: number; rarity: string; crystals_each: number }[] = [];
  for (const c of credits.rows) {
    let left = c.count;
    const rows = bag.rows
      .filter((r) => (parseItemKey(r.item_key)?.base ?? r.item_key) === c.base_id)
      .sort((a, b) => (parseItemKey(b.item_key)?.level ?? 0) - (parseItemKey(a.item_key)?.level ?? 0));
    for (const r of rows) {
      if (left <= 0) break;
      const v = gearDismantleValue(r.item_key);
      if (!v) continue;
      const n = Math.min(left, Number(r.n));
      out.push({ item_key: r.item_key, count: n, rarity: v.rarity, crystals_each: v.crystals });
      left -= n;
    }
  }
  return out;
}

export interface GearDismantleBody {
  request_id: string;
  items: { item_key: string; count: number }[];
}

/** POST /characters/:uuid/starshop/gear-dismantle: 가방의 뽑기 장비를 결정·강화 재료로 */
export function dismantleGear(accountId: number, characterUuid: string, body: GearDismantleBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/gear-dismantle',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      await assertNoHold(db, ctx.char.accountId, ctx.char.id);
      await wallets.lockWallet(db, accountId);
      // 같은 기본 id를 여러 줄로 보내도 뽑기 몫을 넘지 못한다
      const perBase = new Map<string, number>();
      for (const it of body.items) {
        const k = parseItemKey(it.item_key);
        if (!k || !gearDismantleValue(it.item_key)) throw new AppError(422, '분해할 수 없는 물건입니다.', 'NOT_DISMANTLABLE', { item_key: it.item_key });
        perBase.set(k.base, (perBase.get(k.base) ?? 0) + it.count);
      }
      for (const [base, n] of perBase) {
        const credit = await db.query<{ count: number }>(
          'SELECT count FROM gacha_gear_credits WHERE character_id = $1 AND base_id = $2 FOR UPDATE',
          [ctx.char.id, base],
        );
        const have = credit.rows[0]?.count ?? 0;
        if (have < n) {
          throw new AppError(422, '뽑기로 얻은 장비만 분해할 수 있습니다.', 'NOT_GACHA_GEAR', { base_id: base, need: n, have });
        }
      }
      let total = 0;
      const bonus = new Map<string, number>();
      for (const it of body.items) {
        const v = gearDismantleValue(it.item_key) as NonNullable<ReturnType<typeof gearDismantleValue>>;
        // 뽑기 사본(계정 귀속)만 분해한다: 같은 id의 퀘스트 보상(캐릭터 귀속)이 먼저 빠지면 뽑기 사본이 남아 상점에 팔린다
        if ((await ctx.removeItem('bag', it.item_key, it.count, 'gear_dismantle', it.item_key, 'account')) === null) {
          throw new AppError(422, '가방에 그만큼 없습니다.', 'NOT_ENOUGH_ITEMS', { item_key: it.item_key });
        }
        total += v.crystals * it.count;
        for (const [id, n] of v.items) bonus.set(id, (bonus.get(id) ?? 0) + n * it.count);
      }
      for (const [base, n] of perBase) {
        await db.query('UPDATE gacha_gear_credits SET count = count - $3 WHERE character_id = $1 AND base_id = $2', [ctx.char.id, base, n]);
      }
      for (const [id, n] of bonus) await ctx.addItem('bag', id, n, 'dismantle_bonus', 'gear_dismantle');
      await recordCodex(db, accountId, [...perBase.keys()]);
      const w = await crystals.creditCrystals(db, { accountId, amount: total, reason: 'dismantle_gear', ref: body.items.map((i) => i.item_key).join(',').slice(0, 200), requestId });
      return {
        status: 200,
        data: { crystals: total, crystal_balance: w.balance, settled: w.settled, items: [...bonus].map(([item_key, count]) => ({ item_key, count })), delta: ctx.delta() },
      };
    },
  });
}

// ---------------- 별빛 교환소 ----------------

const weekStartOf = (now: Date): string => resetBoundaries(now).weeklyStartAt.slice(0, 10);

async function boughtThisWeek(db: Queryable, accountId: number, week: string): Promise<Map<string, number>> {
  const r = await db.query<{ good_id: string; n: string }>(
    'SELECT good_id, COUNT(*) AS n FROM exchange_purchases WHERE account_id = $1 AND week_start = $2 GROUP BY good_id',
    [accountId, week],
  );
  return new Map(r.rows.map((x) => [x.good_id, Number(x.n)]));
}

/** 고를 수 있는 외형(교환소 전용이 아닌 그 등급·종류, 아직 없는 것) */
export function pickChoices(good: ExchangeGood, owned: Set<string>): CosmeticDef[] {
  if (good.kind !== 'cosmetic') return [];
  if (good.itemId) {
    const d = STAR_COSMETIC_BY_ID.get(good.itemId);
    return d && !owned.has(d.id) ? [d] : [];
  }
  const p = good.pick as { rarity: Rarity; skin: boolean | null };
  return STAR_COSMETICS.filter(
    (c) => !c.exchangeOnly && c.rarity === p.rarity && !owned.has(c.id) && (p.skin === null || (p.skin ? !!c.skin : !c.skin)),
  );
}

/** GET /characters/:uuid/starshop/exchange-shop: 결정, 품목과 이번 주 남은 재고, 분해할 수 있는 뽑기 장비, 도감 */
export async function exchangeShopView(accountId: number, characterUuid: string) {
  const db = getPool();
  const ch = await charRepo.findOwnedAlive(db, accountId, characterUuid);
  if (!ch) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const now = getNow();
  const week = weekStartOf(now);
  const [w, bought, owned, codex, dismantlable] = await Promise.all([
    crystals.readCrystals(db, accountId),
    boughtThisWeek(db, accountId, week),
    repo.ownedOf(db, accountId),
    codexOf(db, accountId),
    dismantlableOf(db, ch.id),
  ]);
  return {
    crystals: w.balance,
    crystal_debt: w.debt,
    week_start: week,
    next_reset_at: resetBoundaries(now).nextWeeklyAt,
    goods: EXCHANGE_GOODS.map((g) => ({
      id: g.id,
      name: g.name,
      price: g.price,
      weekly: g.weekly,
      left: g.weekly > 0 ? Math.max(0, g.weekly - (bought.get(g.id) ?? 0)) : null,
      kind: g.kind,
      item_id: g.itemId ?? null,
      count: g.count ?? null,
      choices: pickChoices(g, owned).map((c) => ({ id: c.id, name: c.name, rarity: c.rarity, skin: c.skin ?? null })),
    })),
    dismantle: { cosmetic: DISMANTLE_CRYSTALS, collection_bonus: COLLECTION_DUP_BONUS, gear: GEAR_DISMANTLE, enhance_bonus: GEAR_DISMANTLE_ENHANCE_BONUS },
    dismantlable,
    codex: { entries: codex, health: codexHealth(codex.length), per: CODEX_HEALTH_PER, max: CODEX_HEALTH_MAX },
  };
}

export interface ExchangeBuyBody {
  request_id: string;
  good_id: string;
  item_id?: string;
}

/** POST /characters/:uuid/starshop/exchange-shop/buy */
export function buyExchangeGood(accountId: number, characterUuid: string, body: ExchangeBuyBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/exchange-shop/buy',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const good = EXCHANGE_GOOD_BY_ID.get(body.good_id);
      if (!good) throw new AppError(404, '교환소에 없는 품목입니다.', 'GOOD_UNKNOWN');
      await assertNoHold(db, ctx.char.accountId, ctx.char.id);
      const star = await wallets.lockWallet(db, accountId);
      await crystals.lockCrystals(db, accountId);
      const week = weekStartOf(ctx.now);
      if (good.weekly > 0) {
        const n = (await boughtThisWeek(db, accountId, week)).get(good.id) ?? 0;
        if (n >= good.weekly) throw new AppError(422, '이번 주 교환소 재고를 모두 받았습니다.', 'GOOD_SOLD_OUT', { weekly: good.weekly });
      }
      let itemId: string | null = good.itemId ?? null;
      if (good.kind === 'cosmetic') {
        const owned = await repo.ownedOf(db, accountId);
        const want = good.itemId ?? body.item_id;
        if (!want) throw new AppError(400, '받을 외형을 골라 주세요.', 'VALIDATION', { fields: [{ path: 'item_id', message: '필수 값' }] });
        if (owned.has(want)) throw new AppError(409, '이미 가진 외형입니다.', 'COSMETIC_OWNED');
        if (!pickChoices(good, owned).some((c) => c.id === want)) throw new AppError(422, '고를 수 없는 외형입니다.', 'CLAIM_NOT_ALLOWED');
        itemId = want;
      }
      const w = await crystals.debitCrystals(db, { accountId, price: good.price, ref: good.id, requestId });
      if (good.kind === 'cosmetic') await repo.addCosmetic(db, accountId, itemId as string, 'crystal_shop');
      else if (good.kind === 'skin_gauge') await wallets.setPity(db, accountId, Math.min(SKIN_GAUGE_MAX, star.skinPity + (good.count ?? 0)), true);
      else if (good.kind === 'gear_gauge') {
        const c = await ceilings.lockCeilings(db, accountId);
        await ceilings.setGauge(db, accountId, 'gear', c.gear + (good.count ?? 0));
      } else await ctx.addItem('bag', good.itemId as string, good.count ?? 1, 'exchange_buy', good.id, 'account');
      await db.query(
        'INSERT INTO exchange_purchases (account_id, character_id, good_id, item_id, price, week_start, request_id) VALUES ($1, $2, $3, $4, $5, $6, $7)',
        [accountId, ctx.char.id, good.id, itemId, good.price, week, requestId],
      );
      return { status: 200, data: { good_id: good.id, item_id: itemId, price: good.price, crystal_balance: w.balance, delta: ctx.delta() } };
    },
  });
}

/** POST /characters/:uuid/starshop/codex: 가진 유니크·레전더리(모든 위치)를 도감에 기록한다 */
export function registerCodex(accountId: number, characterUuid: string, body: { request_id: string }): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/codex',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const r = await db.query<{ item_key: string }>("SELECT DISTINCT item_key FROM character_items WHERE character_id = $1 AND item_key LIKE 'eq\\_%'", [ctx.char.id]);
      const added = await recordCodex(db, accountId, r.rows.map((x) => parseItemKey(x.item_key)?.base ?? x.item_key));
      const codex = await codexOf(db, accountId);
      return { status: 200, data: { added, entries: codex, health: codexHealth(codex.length) } };
    },
  });
}
