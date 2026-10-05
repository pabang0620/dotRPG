import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { keyAt } from '../../utils/itemKey';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import * as charRepo from '../characters/characterRepository';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import {
  DUPLICATE_REFUND,
  SKIN_DUPLICATE_REFUND,
  GEAR_BANNERS,
  GEAR_RARITY_NAME,
  GEAR_RARITY_PERMILLE,
  GEAR_RATE_SCALE,
  AURA_GAUGE_MAX,
  PITY_MAX,
  SKIN_GAUGE_MAX,
  PULL_PRICE,
  RARITY_NAME,
  RARITY_WEIGHT,
  RATES_VERSION,
  STAR_COSMETICS,
  STAR_COSMETIC_BY_ID,
  TEN_COUNT,
  TEN_PRICE,
  cosmeticsOf,
  exchangePriceOf,
  type Banner,
  type Rarity,
} from './starshopDefs';
import * as repo from './starshopRepository';
import type { ClaimBody, ExchangeBody, PullBody } from './starshopValidation';

const RARITIES: Rarity[] = ['unique', 'rare', 'common'];
type GearBanner = Exclude<Banner, 'aura' | 'skin'>;

/** 등급별 뽑기 풀: 오라 뽑기는 오라, 스킨 뽑기는 유니크 자리에 내 직업 스킨 */
function poolOf(banner: 'aura' | 'skin', rarity: Rarity, cls: string) {
  if (banner === 'skin' && rarity === 'unique') return STAR_COSMETICS.filter((c) => c.skin === cls);
  return cosmeticsOf(rarity);
}

/** 오라·스킨 뽑기 확률표. 하나 = 등급 확률 / 그 등급 수(중복 방지 전 기준) */
function auraTable(banner: 'aura' | 'skin' = 'aura', cls = 'warrior') {
  return RARITIES.map((rarity) => {
    const items = poolOf(banner, rarity, cls);
    const tier = RARITY_WEIGHT[rarity] / 100;
    return { rarity, name: RARITY_NAME[rarity], rate: tier, items: items.map((c) => ({ id: c.id, rate: tier / items.length })) };
  });
}

/**
 * 장비 뽑기 등급표(이 직업이 쓸 수 있는 장비, +0, 시작 장비 제외). 그 뽑기에 없는 등급의 몫은 있는 등급 중
 * 가장 낮은 등급이 가진다. 장비 하나 = 등급 확률 / 그 등급 장비 수
 */
export function gearTable(banner: GearBanner, cls: string) {
  const cats = GEAR_BANNERS[banner].categories;
  const items = getGameData().economy.shop.equipmentList.filter(
    (e) => !e.starter && e.rarity !== 'Common' && cats.includes(e.category) && (e.classOnly === null || e.classOnly === cls),
  );
  const present = GEAR_RARITY_PERMILLE.filter(([r]) => items.some((e) => e.rarity === r));
  if (present.length === 0) return [];
  const lowest = present[0]![0];
  const missing = GEAR_RARITY_PERMILLE.filter(([r]) => !present.some(([p]) => p === r)).reduce((a, [, w]) => a + w, 0);
  return present
    .map(([r, w]) => {
      const permille = w + (r === lowest ? missing : 0);
      const of = items.filter((e) => e.rarity === r);
      return {
        rarity: r.toLowerCase(),
        name: GEAR_RARITY_NAME[r] ?? r,
        permille,
        rate: (permille * 100) / GEAR_RATE_SCALE,
        items: of.map((e) => ({ id: keyAt(e.id, 0), rate: (permille * 100) / GEAR_RATE_SCALE / of.length })),
      };
    })
    .reverse();
}

async function myCharacter(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

/** 지갑·천장·보유 외형·확률표·가격 */
export async function summary(accountId: number, characterUuid: string) {
  const ch = await myCharacter(accountId, characterUuid);
  const db = getPool();
  const wallet = await repo.readWallet(db, accountId);
  const owned = await repo.ownedOf(db, accountId);
  return {
    balance: wallet.balance,
    pity: wallet.pity,
    skin_pity: wallet.skinPity,
    pity_max: PITY_MAX,
    aura_gauge_max: AURA_GAUGE_MAX,
    skin_gauge_max: SKIN_GAUGE_MAX,
    rates_version: RATES_VERSION,
    price_one: PULL_PRICE,
    price_ten: TEN_PRICE,
    ten_count: TEN_COUNT,
    refund: DUPLICATE_REFUND,
    rates: auraTable(),
    skin_rates: auraTable('skin', ch.class),
    banners: (Object.keys(GEAR_BANNERS) as GearBanner[]).map((b) => ({
      id: b,
      name: GEAR_BANNERS[b].name,
      rates: gearTable(b, ch.class).map(({ permille: _p, ...t }) => t),
    })),
    items: STAR_COSMETICS.map((c) => ({
      id: c.id,
      name: c.name,
      rarity: c.rarity,
      owned: owned.has(c.id),
      exchange_price: exchangePriceOf(c),
      skin: c.skin ?? null,
    })),
  };
}

/** rareOrBetter: the 10+1 bonus draw (일반 removed, 희귀·유니크 keep their ratio) */
function rollRarity(rareOrBetter = false): Rarity {
  const roll = rareOrBetter ? getRng().int(0, RARITY_WEIGHT.unique + RARITY_WEIGHT.rare) : getRng().int(0, 10000);
  if (roll < RARITY_WEIGHT.unique) return 'unique';
  if (roll < RARITY_WEIGHT.unique + RARITY_WEIGHT.rare) return 'rare';
  return 'common';
}

/**
 * 뽑기. 오라 뽑기는 등급을 먼저 정하고(천장이면 유니크 확정), 그 등급에서 아직 없는 오라 중 하나를 같은 확률로
 * 고른다. 그 등급을 모두 가졌으면 중복으로 나오고 별조각을 돌려준다. 유니크가 나오면 천장은 0으로 돌아간다.
 * 장비 뽑기는 등급표대로 하나씩 가방에 넣는다(천장·중복 처리 없음).
 */
export function pull(accountId: number, characterUuid: string, body: PullBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/pull',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const banner: Banner = body.banner ?? 'aura';
      const price = body.count === 10 ? TEN_PRICE : PULL_PRICE;
      const times = body.count === 10 ? TEN_COUNT : 1;
      const wallet = await repo.lockWallet(db, accountId);
      if (wallet.balance < price) {
        throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      }
      let balance = await repo.changeBalance(db, accountId, -price, 'gacha', `${banner}_${body.count}`, requestId);
      const rows: repo.PullRow[] = [];
      let pity = banner === 'skin' ? wallet.skinPity : wallet.pity;
      if (banner === 'aura' || banner === 'skin') {
        const r = await rollAuras(ctx, accountId, banner, times, pity, rows);
        pity = r.pity;
        await repo.setPity(db, accountId, pity, banner === 'skin');
        if (r.refund > 0) balance = await repo.changeBalance(db, accountId, r.refund, 'gacha_refund', `${banner}_${body.count}`, requestId);
      } else {
        await rollGear(ctx, banner, times, rows, body.count);
      }
      await repo.insertPulls(db, accountId, requestId, RATES_VERSION, banner, rows);
      return {
        status: 200,
        data: {
          banner,
          results: rows.map((r) => ({ item_id: r.itemId, rarity: r.rarity, by_pity: r.byPity, duplicate: r.duplicate, refund: r.refund, gear: r.kind === 'gear' })),
          delta: ctx.delta(),
          balance,
          pity,
          pity_max: banner === 'skin' ? SKIN_GAUGE_MAX : AURA_GAUGE_MAX,
          rates_version: RATES_VERSION,
        },
      };
    },
  });
}

async function rollAuras(ctx: EconCtx, accountId: number, banner: 'aura' | 'skin', times: number, pityStart: number, rows: repo.PullRow[]) {
  const owned = await repo.ownedOf(ctx.client, accountId);
  let pity = pityStart;
  let refund = 0;
  for (let i = 0; i < times; i++) {
    const pityBefore = pity;
    const byPity = false; // no automatic pity: the gauge fills and the player chooses (claim)
    const bonus = times > 1 && i === times - 1;
    const rarity: Rarity = rollRarity(bonus);
    const all = poolOf(banner, rarity, ctx.char.class);
    const fresh = all.filter((c) => !owned.has(c.id));
    const duplicate = fresh.length === 0;
    const from = duplicate ? all : fresh;
    const item = from[getRng().int(0, from.length)]!;
    const back = duplicate ? (item.skin ? SKIN_DUPLICATE_REFUND : DUPLICATE_REFUND[rarity]) : 0;
    if (duplicate) refund += back;
    else {
      owned.add(item.id);
      await repo.addCosmetic(ctx.client, accountId, item.id, 'gacha');
    }
    pity = pity + 1;
    rows.push({ seq: i, rarity, itemId: item.id, pityBefore, pityAfter: pity, byPity, duplicate, refund: back, kind: 'cosmetic' });
  }
  return { pity, refund };
}

async function rollGear(ctx: EconCtx, banner: GearBanner, times: number, rows: repo.PullRow[], count: number) {
  const table = gearTable(banner, ctx.char.class);
  if (table.length === 0) throw new AppError(422, '뽑을 수 있는 장비가 없습니다.', 'NO_GEAR');
  for (let i = 0; i < times; i++) {
    // 10+1의 보너스(마지막) 1회는 최하 등급을 빼고 굴린다: 남은 등급끼리 같은 비율
    const bonus = times > 1 && i === times - 1 && table.length > 1;
    const pool = bonus ? table.slice(0, -1) : table;
    const total = pool.reduce((a, t) => a + t.permille, 0);
    let roll = getRng().int(0, total);
    let tier = pool[pool.length - 1]!;
    for (const t of pool) {
      if (roll < t.permille) { tier = t; break; }
      roll -= t.permille;
    }
    const item = tier.items[getRng().int(0, tier.items.length)]!;
    await ctx.addItem('bag', item.id, 1, 'gacha', `${banner}_${count}`);
    rows.push({ seq: i, rarity: tier.rarity, itemId: item.id, pityBefore: 0, pityAfter: 0, byPity: false, duplicate: false, refund: 0, kind: 'gear' });
  }
}

/** 확정 교환: 원하는 외형을 정해진 별조각으로 바로 얻는다(이미 가진 외형은 거절) */
export function exchange(accountId: number, characterUuid: string, body: ExchangeBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/exchange',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const def = STAR_COSMETIC_BY_ID.get(body.item_id);
      if (!def) throw new AppError(404, '교환할 수 없는 외형입니다.', 'COSMETIC_UNKNOWN');
      const wallet = await repo.lockWallet(db, accountId);
      const owned = await repo.ownedOf(db, accountId);
      if (owned.has(def.id)) throw new AppError(409, '이미 가진 외형입니다.', 'COSMETIC_OWNED');
      const price = exchangePriceOf(def);
      if (wallet.balance < price) {
        throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      }
      const balance = await repo.changeBalance(db, accountId, -price, 'exchange', def.id, requestId);
      await repo.addCosmetic(db, accountId, def.id, 'exchange');
      return { status: 200, data: { item_id: def.id, price, balance } };
    },
  });
}

/** 선택 게이지가 가득 찼을 때 최상위 하나를 고른다(오라: 유니크 오라, 스킨: 내 직업 스킨). 가진 것은 고를 수 없다 */
export function claim(accountId: number, characterUuid: string, body: ClaimBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/claim',
    requestId,
    payload,
    handler: async (ctx) => {
      const db = ctx.client;
      const max = body.banner === 'skin' ? SKIN_GAUGE_MAX : AURA_GAUGE_MAX;
      const wallet = await repo.lockWallet(db, accountId);
      const gauge = body.banner === 'skin' ? wallet.skinPity : wallet.pity;
      if (gauge < max) throw new AppError(422, '선택 게이지가 아직 다 차지 않았습니다.', 'GAUGE_NOT_FULL', { need: max, have: gauge });
      const choices = poolOf(body.banner, 'unique', ctx.char.class);
      const def = choices.find((c) => c.id === body.item_id);
      if (!def) throw new AppError(422, '고를 수 없는 항목입니다.', 'CLAIM_NOT_ALLOWED');
      const owned = await repo.ownedOf(db, accountId);
      if (owned.has(def.id)) throw new AppError(409, '이미 가진 외형입니다.', 'COSMETIC_OWNED');
      await repo.addCosmetic(db, accountId, def.id, 'gacha');
      const left = gauge - max;
      await repo.setPity(db, accountId, left, body.banner === 'skin');
      await repo.insertPulls(db, accountId, requestId, RATES_VERSION, body.banner, [
        { seq: 0, rarity: 'unique', itemId: def.id, pityBefore: gauge, pityAfter: left, byPity: true, duplicate: false, refund: 0, kind: 'cosmetic' },
      ]);
      return { status: 200, data: { item_id: def.id, banner: body.banner, pity: left, pity_max: max } };
    },
  });
}
