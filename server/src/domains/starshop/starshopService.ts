import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { assertNoHold } from '../antiabuse/holds';
import { CHAPTER2_TIER_COUNT, TIER_LEVELS, tierOfLevel } from '../../utils/gearTier';
import { getGameData } from '../../gamedata/loader';
import { keyAt } from '../../utils/itemKey';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import * as charRepo from '../characters/characterRepository';
import * as ceilings from '../ceilings/ceilingRepository';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import {
  AURA_GAUGE_MAX,
  BANNER_ROWS,
  DUPLICATE_REFUND,
  GAUGES,
  HIDDEN_GAUGE_MAX,
  RARITIES,
  damageOf,
  dismantleCrystalsOf,
  dismantleStarsOf,
  gaugeCap,
  skinsOf,
  slotName,
  type GaugeId,
  GEAR_BANNERS,
  GEAR_CEILING,
  GEAR_RARITY_NAME,
  GEAR_RARITY_PERMILLE,
  GEAR_RATE_SCALE,
  PITY_MAX,
  PULL_PRICE,
  RATE_SCALE,
  RATES_VERSION,
  SKIN_GAUGE_MAX,
  STAR_COSMETICS,
  STAR_COSMETIC_BY_ID,
  TEN_COUNT,
  TEN_PRICE,
  COLLECTIONS,
  DISMANTLE_CRYSTALS,
  DISMANTLE_STARS,
  SYNTH,
  SYNTH_COUNT,
  cosmeticsOf,
  exchangePriceOf,
  type SynthFrom,
  type SynthTier,
  type CosmeticDef,
  type Banner,
  type CosmeticBanner,
  type Rarity,
} from './starshopDefs';
import * as repo from './starshopRepository';
import { codexHealth, codexOf, noteGachaGear } from './cashValueService';
import { readCrystals } from './crystalWallet';
import * as wallets from './starWallet';
import type { ClaimBody, ExchangeBody, PullBody } from './starshopValidation';
import { usableBy } from '../../utils/characterClass';

type GearBanner = Exclude<Banner, CosmeticBanner>;

/** 한 칸의 뽑기 풀: 오라 뽑기는 오라만(히든 없음), 스킨 뽑기는 모든 칸이 내 직업 스킨(retired·교환소 전용 제외) */
function poolOf(banner: CosmeticBanner, rarity: Rarity, cls: string, hidden = false) {
  if (banner === 'skin') return skinsOf(cls, rarity, hidden);
  return hidden ? [] : cosmeticsOf(rarity);
}

export { poolOf };

export interface SlotRow {
  rarity: Rarity;
  hidden: boolean;
  /** 이 직업에서 실제로 쓰는 몫(십만분율) */
  weight: number;
  items: CosmeticDef[];
}

/**
 * 이 직업의 실제 뽑기 칸 표(높은 칸부터, 외형이 있는 칸만). 히든 풀이 비면 그 몫은 같은 등급 보통으로, 보통 풀이
 * 비면 그 몫은 한 등급 아래 보통으로 간다(히든 몫은 그대로). 뽑기·화면 확률표·공시가 모두 이 표를 쓴다
 */
export function slotTable(banner: CosmeticBanner, cls: string): SlotRow[] {
  const w = new Map<string, number>();
  const key = (r: Rarity, h: boolean) => `${r}:${h}`;
  for (const row of BANNER_ROWS[banner]) w.set(key(row.rarity, row.hidden), (w.get(key(row.rarity, row.hidden)) ?? 0) + row.weight);
  const out: SlotRow[] = [];
  let carry = 0;
  for (let i = 0; i < RARITIES.length; i++) {
    const r = RARITIES[i]!;
    const hiddenItems = poolOf(banner, r, cls, true);
    const normalItems = poolOf(banner, r, cls, false);
    let hw = w.get(key(r, true)) ?? 0;
    let nw = (w.get(key(r, false)) ?? 0) + carry;
    carry = 0;
    if (hiddenItems.length === 0) {
      nw += hw;
      hw = 0;
    }
    if (normalItems.length === 0) {
      carry = nw;
      nw = 0;
    }
    if (hw > 0) out.push({ rarity: r, hidden: true, weight: hw, items: hiddenItems });
    if (nw > 0) out.push({ rarity: r, hidden: false, weight: nw, items: normalItems });
  }
  return out;
}

/** 10+1의 보너스 1회에서 빠지는 칸: 일반(보통) */
const isBonusExcluded = (r: SlotRow): boolean => r.rarity === 'common' && !r.hidden;

/** 뽑기 본문의 rates_version 확인(11.4). STAR_RATES_ACK_REQUIRED 이면 없는 요청을 거절한다 */
function assertRatesAck(seen: string | undefined): void {
  if (seen === undefined) {
    if (getConfig().pay.ratesAckRequired) throw new AppError(400, '확률표 버전(rates_version)을 함께 보내야 합니다.', 'VALIDATION', { fields: [{ path: 'rates_version', message: '필수 값' }] });
    return;
  }
  if (seen !== RATES_VERSION) throw new AppError(409, '확률표가 바뀌었습니다. 다시 확인해 주세요.', 'RATES_CHANGED', { rates_version: RATES_VERSION });
}

/** 오라·스킨 뽑기 확률표(%). 칸마다: 칸 확률, 보너스 1회 칸 확률, 외형별 확률(중복 방지 전 기준) */
export function rateTable(banner: CosmeticBanner = 'aura', cls = 'warrior') {
  const rows = slotTable(banner, cls);
  const bonusTotal = rows.filter((r) => !isBonusExcluded(r)).reduce((a, r) => a + r.weight, 0);
  return rows.map((row) => {
    const rate = (row.weight * 100) / RATE_SCALE;
    const bonus = isBonusExcluded(row) || bonusTotal <= 0 ? 0 : (row.weight * 100) / bonusTotal;
    const n = Math.max(1, row.items.length);
    return {
      rarity: row.rarity,
      hidden: row.hidden,
      name: slotName(row.rarity, row.hidden),
      rate,
      bonus_rate: bonus,
      items: row.items.map((c) => ({ id: c.id, rate: rate / n, bonus_rate: bonus / n })),
    };
  });
}

/** 게이지 하나의 목표와 고를 수 있는 외형(이 직업) */
export function gaugeView(g: GaugeId, cls: string, value: number) {
  const banner: CosmeticBanner = g === 'aura' ? 'aura' : 'skin';
  return {
    value,
    targets: GAUGES[g].map((t) => ({
      rarity: t.rarity,
      hidden: t.hidden,
      name: slotName(t.rarity, t.hidden),
      cost: t.cost,
      ready: value >= t.cost,
      items: poolOf(banner, t.rarity, cls, t.hidden).map((c) => c.id),
    })).filter((t) => t.items.length > 0),
  };
}

/**
 * 장비 뽑기 등급표(이 직업이 쓸 수 있는 장비, +0, 시작 장비 제외). 그 뽑기에 없는 등급의 몫은 있는 등급 중
 * 가장 낮은 등급이 가진다. 장비 하나 = 등급 확률 / 그 등급 장비 수
 */
export function gearTable(banner: GearBanner, cls: string, tier = 0) {
  const cats = GEAR_BANNERS[banner].categories;
  const items = getGameData().economy.shop.equipmentList.filter(
    (e) =>
      !e.starter && !e.bossOnly && e.levelTier === tier && e.rarity !== 'Common' && cats.includes(e.category) && usableBy(e, cls),
  );
  const present = GEAR_RARITY_PERMILLE.filter(([r]) => items.some((e) => e.rarity === r));
  if (present.length === 0) return [];
  const lowest = present[0]![0];
  const has = (r: string) => present.some(([p]) => p === r);
  // 레전더리가 없는 단계는 그 몫을 유니크가, 나머지 빈 등급의 몫은 가장 낮은 등급이 가진다
  const legendToUnique = !has('Legendary') && has('Unique');
  const missing = GEAR_RARITY_PERMILLE.filter(([r]) => !has(r) && !(legendToUnique && r === 'Legendary')).reduce((a, [, w]) => a + w, 0);
  const legendShare = legendToUnique ? (GEAR_RARITY_PERMILLE.find(([r]) => r === 'Legendary')?.[1] ?? 0) : 0;
  return present
    .map(([r, w]) => {
      const permille = w + (r === lowest ? missing : 0) + (r === 'Unique' ? legendShare : 0);
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
  const wallet = await wallets.readWallet(db, accountId);
  const owned = await repo.ownedOf(db, accountId);
  const copies = await repo.copiesOf(db, accountId);
  const registered = await repo.collectionsOf(db, accountId);
  const crystalWallet = await readCrystals(db, accountId);
  const codex = await codexOf(db, accountId);
  return {
    balance: wallet.balance,
    // 0044: 별의 결정(교환소 포인트)과 장비 도감 체력(%)
    crystals: crystalWallet.balance,
    crystal_debt: crystalWallet.debt,
    codex_health: codexHealth(codex.length),
    codex_count: codex.length,
    dismantle_crystals: DISMANTLE_CRYSTALS,
    // 11단계: 유료·무료 구분, 부채, 결제 기능 표시(기존 필드는 그대로)
    paid_balance: wallet.paidBalance,
    free_balance: wallet.balance - wallet.paidBalance,
    debt: wallet.debt,
    payments: { enabled: getConfig().pay.enabled },
    pity: wallet.pity,
    skin_pity: wallet.skinPity,
    hidden_pity: wallet.hiddenPity,
    pity_max: PITY_MAX,
    aura_gauge_max: AURA_GAUGE_MAX,
    skin_gauge_max: SKIN_GAUGE_MAX,
    hidden_gauge_max: HIDDEN_GAUGE_MAX,
    // 2026-10-24: 게이지 목표(스킨: 유니크·전설, 히든: 에픽·유니크·전설 히든). 이 직업에 외형이 없는 목표는 싣지 않는다
    gauges: {
      aura: gaugeView('aura', ch.class, wallet.pity),
      skin: gaugeView('skin', ch.class, wallet.skinPity),
      hidden: gaugeView('hidden', ch.class, wallet.hiddenPity),
    },
    rates_version: RATES_VERSION,
    gear_ceiling: gearCeilingView((await ceilings.readCeilings(db, accountId)).gear),
    price_one: PULL_PRICE,
    price_ten: TEN_PRICE,
    ten_count: TEN_COUNT,
    refund: DUPLICATE_REFUND,
    dismantle: DISMANTLE_STARS,
    synth: (Object.keys(SYNTH) as SynthFrom[]).map((from) => ({
      from,
      to: SYNTH[from].to,
      count: SYNTH_COUNT,
      rate: SYNTH[from].permille / 10,
      pity: SYNTH[from].pity,
      fails: wallet.synthFail[SYNTH[from].to as SynthTier],
    })),
    collections: COLLECTIONS.map((c) => ({
      id: c.id,
      name: c.name,
      members: c.members,
      requires: c.requires ?? [],
      attack: c.attack,
      health: c.health,
      hidden: c.hidden === true,
      registered: registered.has(c.id),
    })),
    rates: rateTable('aura', ch.class),
    skin_rates: rateTable('skin', ch.class),
    // 장비 뽑기는 레벨 단계를 골라 뽑는다: 모든 단계를 고를 수 있다(2026-10-14, 내 레벨 위 단계도). 기본 선택은 my_tier
    my_tier: tierOfLevel(ch.level),
    tier_levels: TIER_LEVELS.slice(0, CHAPTER2_TIER_COUNT), // 3장 단계(45·50)는 3장 별빛 상점 단계에서 연다
    banners: (Object.keys(GEAR_BANNERS) as GearBanner[]).map((b) => ({
      id: b,
      name: GEAR_BANNERS[b].name,
      rates: gearTable(b, ch.class, tierOfLevel(ch.level)).map(({ permille: _p, ...t }) => t),
      tiers: Array.from({ length: CHAPTER2_TIER_COUNT }, (_, t) => ({
        tier: t,
        level: TIER_LEVELS[t],
        rates: gearTable(b, ch.class, t).map(({ permille: _p, ...x }) => x),
      })),
    })),
    items: STAR_COSMETICS.map((c) => ({
      id: c.id,
      name: c.name,
      rarity: c.rarity,
      owned: owned.has(c.id),
      copies: copies.get(c.id) ?? 0,
      exchange_price: exchangePriceOf(c),
      skin: c.skin ?? null,
      exchange_only: c.exchangeOnly === true,
      hidden: c.hidden === true,
      retired: c.retired === true,
      damage: damageOf(c),
      dismantle_stars: dismantleStarsOf(c),
      dismantle_crystals: dismantleCrystalsOf(c),
    })),
  };
}

/** 장비 뽑기 천장 보기(0033) */
export function gearCeilingView(gauge: number) {
  return { gauge, max: GEAR_CEILING.max, ready: gauge >= GEAR_CEILING.max, rarity: GEAR_CEILING.rarity };
}

/** 칸 굴림(이 직업의 칸 표). bonus: 10+1의 보너스 1회(일반 보통 제외, 나머지는 같은 비율) */
export function rollSlot(banner: CosmeticBanner, cls: string, bonus = false): SlotRow {
  const order = slotTable(banner, cls).filter((r) => !(bonus && isBonusExcluded(r)));
  const total = order.reduce((a, r) => a + r.weight, 0);
  let roll = getRng().int(0, total);
  for (const r of order) {
    if (roll < r.weight) return r;
    roll -= r.weight;
  }
  return order[order.length - 1]!;
}

/** 등급만 필요할 때(예전 호출) */
export function rollRarity(banner: CosmeticBanner, rareOrBetter = false, cls = 'warrior'): Rarity {
  return rollSlot(banner, cls, rareOrBetter).rarity;
}

/**
 * 뽑기. 오라 뽑기는 등급을 먼저 정하고(천장이면 유니크 확정), 그 등급에서 아직 없는 오라 중 하나를 같은 확률로
 * 고른다. 그 등급을 모두 가졌으면 중복으로 나오고 별조각을 돌려준다. 유니크가 나오면 천장은 0으로 돌아간다.
 * 장비 뽑기는 등급표대로 하나씩 가방에 넣고 천장 게이지를 올린다(교환은 gearCeiling.ts, 중복 처리 없음).
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
      // 11단계 11.4: 클라이언트가 화면에서 본 확률표 버전이 지금과 다르면 거절한다(지급 근거가 아니라 "이 표를 보고 뽑겠다"는 확인)
      assertRatesAck(body.rates_version);
      // 9단계 12.5: 경제 정지 중에는 별조각을 쓸 수 없다
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      const db = ctx.client;
      const banner: Banner = body.banner ?? 'aura';
      const price = body.count === 10 ? TEN_PRICE : PULL_PRICE;
      const times = body.count === 10 ? TEN_COUNT : 1;
      const wallet = await wallets.lockWallet(db, accountId);
      wallets.assertNoStarDebt(wallet);
      if (wallet.balance < price) {
        throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      }
      let balance = (await wallets.debit(db, { accountId, reason: 'gacha', price, ref: `${banner}_${body.count}`, requestId })).balance;
      const rows: repo.PullRow[] = [];
      let gearCeiling: number | null = null;
      let pity = banner === 'skin' ? wallet.skinPity : wallet.pity;
      let hiddenPity: number | null = null;
      if (banner === 'aura' || banner === 'skin') {
        const r = await rollAuras(ctx, accountId, banner, times, pity, rows);
        pity = r.pity;
        await wallets.setPity(db, accountId, pity, banner);
        // 스킨 뽑기 1회에 히든 게이지도 1칸
        if (banner === 'skin') {
          hiddenPity = wallet.hiddenPity + times;
          await wallets.setPity(db, accountId, hiddenPity, 'hidden');
        }
        // 중복 환급은 하지 않는다(refund는 항상 0, 여분으로 쌓인다). gacha_refund 원장은 새로 쓰지 않는다
      } else {
        // 내 레벨 위 단계도 뽑을 수 있다. 장비는 가방에 두고 착용 레벨이 되면 낀다
        const tier = body.tier ?? tierOfLevel(ctx.char.level);
        await rollGear(ctx, banner, times, rows, body.count, tier);
        // 천장 게이지(장비 뽑기 세 종류 합산). 락 순서상 마지막
        gearCeiling = await ceilings.addGauge(db, accountId, 'gear', times);
      }
      await repo.insertPulls(db, accountId, requestId, RATES_VERSION, banner, rows);
      return {
        status: 200,
        data: {
          banner,
          results: rows.map((r) => ({
            item_id: r.itemId,
            rarity: r.rarity,
            hidden: r.kind === 'cosmetic' && STAR_COSMETIC_BY_ID.get(r.itemId)?.hidden === true,
            by_pity: r.byPity,
            duplicate: r.duplicate,
            refund: r.refund,
            gear: r.kind === 'gear',
          })),
          delta: ctx.delta(),
          balance,
          pity,
          pity_max: banner === 'skin' ? SKIN_GAUGE_MAX : AURA_GAUGE_MAX,
          ...(hiddenPity === null ? {} : { hidden_pity: hiddenPity }),
          ...(gearCeiling === null ? {} : { gear_ceiling: gearCeilingView(gearCeiling) }),
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
    const slot = rollSlot(banner, ctx.char.class, bonus);
    const rarity: Rarity = slot.rarity;
    const all = slot.items;
    const fresh = all.filter((c) => !owned.has(c.id));
    const duplicate = fresh.length === 0;
    const from = duplicate ? all : fresh;
    const item = from[getRng().int(0, from.length)]!;
    // 이미 가진 외형은 여분으로 쌓는다(합성 재료, 원하면 같은 값으로 분해). 별조각 환급은 하지 않는다
    const back = 0;
    if (duplicate) await repo.addCopies(ctx.client, accountId, item.id, 1);
    else {
      owned.add(item.id);
      await repo.addCosmetic(ctx.client, accountId, item.id, 'gacha');
    }
    pity = pity + 1;
    rows.push({ seq: i, rarity, itemId: item.id, pityBefore, pityAfter: pity, byPity, duplicate, refund: back, kind: 'cosmetic' });
  }
  return { pity, refund };
}

/** 장비 뽑기 등급 굴림(누적 permille). 마지막 칸이 나머지를 받는다 */
export function pickGearTier<T extends { permille: number }>(pool: T[]): T {
  const total = pool.reduce((a, t) => a + t.permille, 0);
  let roll = getRng().int(0, total);
  for (const t of pool) {
    if (roll < t.permille) return t;
    roll -= t.permille;
  }
  return pool[pool.length - 1]!;
}

async function rollGear(ctx: EconCtx, banner: GearBanner, times: number, rows: repo.PullRow[], count: number, tier: number) {
  const table = gearTable(banner, ctx.char.class, tier);
  if (table.length === 0) throw new AppError(422, '뽑을 수 있는 장비가 없습니다.', 'NO_GEAR');
  for (let i = 0; i < times; i++) {
    // 장비 뽑기는 10+1의 보너스 1회도 같은 확률로 굴린다. 뽑기마다 등급이 2~3개뿐이라 "최하 등급 제외"를 하면
    // 장신구는 유니크, 방어구는 에픽이 확정되어 버린다(외형 뽑기의 보너스만 희귀 이상)
    const tier = pickGearTier(table);
    const item = tier.items[getRng().int(0, tier.items.length)]!;
    await ctx.addItem('bag', item.id, 1, 'gacha', `${banner}_${count}`);
    await noteGachaGear(ctx.client, ctx.char.id, ctx.char.accountId, item.id, 1); // 0044: 상점 판매 금지·분해 몫, 도감
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
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      const db = ctx.client;
      const def = STAR_COSMETIC_BY_ID.get(body.item_id);
      if (!def) throw new AppError(404, '교환할 수 없는 외형입니다.', 'COSMETIC_UNKNOWN');
      const wallet = await wallets.lockWallet(db, accountId);
      wallets.assertNoStarDebt(wallet);
      const owned = await repo.ownedOf(db, accountId);
      if (owned.has(def.id)) throw new AppError(409, '이미 가진 외형입니다.', 'COSMETIC_OWNED');
      const price = exchangePriceOf(def);
      if (price <= 0) throw new AppError(422, '선택 게이지로만 얻을 수 있는 외형입니다.', 'GAUGE_ONLY');
      if (wallet.balance < price) {
        throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      }
      const balance = (await wallets.debit(db, { accountId, reason: 'exchange', price, ref: def.id, requestId })).balance;
      await repo.addCosmetic(db, accountId, def.id, 'exchange');
      return { status: 200, data: { item_id: def.id, price, balance } };
    },
  });
}

/**
 * 선택 게이지로 고르기. 오라: 에픽 오라(50칸), 스킨: 내 직업 유니크(100칸)·전설(1,000칸), 히든: 내 직업 에픽 히든(350)·
 * 유니크 히든(2,000)·전설 히든(3,000). 고른 외형의 칸만큼 쓰고 남은 칸은 이월한다. 가진 것은 고를 수 없다
 */
export function claim(accountId: number, characterUuid: string, body: ClaimBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/claim',
    requestId,
    payload,
    handler: async (ctx) => {
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      const db = ctx.client;
      const g: GaugeId = body.banner;
      const wallet = await wallets.lockWallet(db, accountId);
      wallets.assertNoStarDebt(wallet);
      const gauge = g === 'skin' ? wallet.skinPity : g === 'hidden' ? wallet.hiddenPity : wallet.pity;
      const pool: CosmeticBanner = g === 'aura' ? 'aura' : 'skin';
      const target = GAUGES[g].find((t) => poolOf(pool, t.rarity, ctx.char.class, t.hidden).some((c) => c.id === body.item_id));
      if (!target) throw new AppError(422, '고를 수 없는 항목입니다.', 'CLAIM_NOT_ALLOWED');
      if (gauge < target.cost) throw new AppError(422, '선택 게이지가 아직 다 차지 않았습니다.', 'GAUGE_NOT_FULL', { need: target.cost, have: gauge });
      const def = STAR_COSMETIC_BY_ID.get(body.item_id)!;
      const owned = await repo.ownedOf(db, accountId);
      if (owned.has(def.id)) throw new AppError(409, '이미 가진 외형입니다.', 'COSMETIC_OWNED');
      await repo.addCosmetic(db, accountId, def.id, 'gacha');
      const left = gauge - target.cost;
      await wallets.setPity(db, accountId, left, g);
      await repo.insertPulls(db, accountId, requestId, RATES_VERSION, pool, [
        { seq: 0, rarity: target.rarity, itemId: def.id, pityBefore: gauge, pityAfter: left, byPity: true, duplicate: false, refund: 0, kind: 'cosmetic' },
      ]);
      return { status: 200, data: { item_id: def.id, banner: g, pity: left, pity_max: target.cost, cost: target.cost, max_cost: gaugeCap(g) } };
    },
  });
}
