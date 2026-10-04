import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import * as charRepo from '../characters/characterRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import {
  DUPLICATE_REFUND,
  EXCHANGE_PRICE,
  PITY_MAX,
  PULL_PRICE,
  RARITY_NAME,
  RARITY_WEIGHT,
  RATES_VERSION,
  STAR_COSMETICS,
  STAR_COSMETIC_BY_ID,
  TEN_COUNT,
  TEN_PRICE,
  cosmeticsOf,
  type Rarity,
} from './starshopDefs';
import * as repo from './starshopRepository';
import type { ExchangeBody, PullBody } from './starshopValidation';

const RARITIES: Rarity[] = ['legend', 'rare', 'common'];

/** 화면에 그대로 보여 줄 확률표. 외형 하나의 확률 = 등급 확률 / 그 등급의 외형 수(중복 방지 전 기준) */
export function rateTable() {
  return RARITIES.map((rarity) => {
    const items = cosmeticsOf(rarity);
    return {
      rarity,
      name: RARITY_NAME[rarity],
      rate: RARITY_WEIGHT[rarity] / 100,
      items: items.map((c) => ({ id: c.id, name: c.name, rate: RARITY_WEIGHT[rarity] / 100 / items.length })),
    };
  });
}

async function myCharacter(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

/** 지갑·천장·보유 외형·확률표·가격 */
export async function summary(accountId: number, characterUuid: string) {
  await myCharacter(accountId, characterUuid);
  const db = getPool();
  const wallet = await repo.readWallet(db, accountId);
  const owned = await repo.ownedOf(db, accountId);
  const recent = await repo.recentPulls(db, accountId, 22);
  return {
    balance: wallet.balance,
    pity: wallet.pity,
    pity_max: PITY_MAX,
    rates_version: RATES_VERSION,
    price_one: PULL_PRICE,
    price_ten: TEN_PRICE,
    ten_count: TEN_COUNT,
    refund: DUPLICATE_REFUND,
    rates: rateTable(),
    items: STAR_COSMETICS.map((c) => ({
      id: c.id,
      name: c.name,
      rarity: c.rarity,
      owned: owned.has(c.id),
      exchange_price: EXCHANGE_PRICE[c.rarity],
    })),
    recent: recent.map((p) => ({ item_id: p.item_id, rarity: p.rarity, by_pity: p.by_pity, duplicate: p.duplicate, refund: p.refund })),
  };
}

function rollRarity(): Rarity {
  const roll = getRng().int(0, 10000);
  if (roll < RARITY_WEIGHT.legend) return 'legend';
  if (roll < RARITY_WEIGHT.legend + RARITY_WEIGHT.rare) return 'rare';
  return 'common';
}

/**
 * 뽑기. 등급을 먼저 정하고(천장이면 전설 확정), 그 등급에서 아직 없는 외형 중 하나를 같은 확률로 고른다.
 * 그 등급을 모두 가졌으면 아무 외형이 중복으로 나오고 별조각을 돌려준다. 전설이 나오면 천장은 0으로 돌아간다.
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
      const price = body.count === 10 ? TEN_PRICE : PULL_PRICE;
      const times = body.count === 10 ? TEN_COUNT : 1;
      const wallet = await repo.lockWallet(db, accountId);
      if (wallet.balance < price) {
        throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      }
      let balance = await repo.changeBalance(db, accountId, -price, 'gacha', `pull_${body.count}`, requestId);
      const owned = await repo.ownedOf(db, accountId);
      let pity = wallet.pity;
      const rows: repo.PullRow[] = [];
      let refundTotal = 0;
      for (let i = 0; i < times; i++) {
        const pityBefore = pity;
        const byPity = pity + 1 >= PITY_MAX;
        const rarity: Rarity = byPity ? 'legend' : rollRarity();
        const all = cosmeticsOf(rarity);
        const fresh = all.filter((c) => !owned.has(c.id));
        const duplicate = fresh.length === 0;
        const from = duplicate ? all : fresh;
        const item = from[getRng().int(0, from.length)]!;
        const refund = duplicate ? DUPLICATE_REFUND[rarity] : 0;
        if (duplicate) refundTotal += refund;
        else {
          owned.add(item.id);
          await repo.addCosmetic(db, accountId, item.id, 'gacha');
        }
        pity = rarity === 'legend' ? 0 : pity + 1;
        rows.push({ seq: i, rarity, itemId: item.id, pityBefore, pityAfter: pity, byPity, duplicate, refund });
      }
      await repo.setPity(db, accountId, pity);
      await repo.insertPulls(db, accountId, requestId, RATES_VERSION, rows);
      if (refundTotal > 0) balance = await repo.changeBalance(db, accountId, refundTotal, 'gacha_refund', `pull_${body.count}`, requestId);
      return {
        status: 200,
        data: {
          results: rows.map((r) => ({ item_id: r.itemId, rarity: r.rarity, by_pity: r.byPity, duplicate: r.duplicate, refund: r.refund })),
          balance,
          pity,
          pity_max: PITY_MAX,
          rates_version: RATES_VERSION,
        },
      };
    },
  });
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
      const price = EXCHANGE_PRICE[def.rarity];
      if (wallet.balance < price) {
        throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      }
      const balance = await repo.changeBalance(db, accountId, -price, 'exchange', def.id, requestId);
      await repo.addCosmetic(db, accountId, def.id, 'exchange');
      return { status: 200, data: { item_id: def.id, price, balance } };
    },
  });
}
