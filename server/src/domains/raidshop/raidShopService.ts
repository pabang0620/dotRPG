// 13단계 레이드 상점: 전용 재료로 내 직업 무기 상자를 산 즉시 연다(Docs/server/phase13_raid_rewards.md 5, 6.4).
// 가격·확률·단계·직업은 모두 서버 데이터와 캐릭터 행에서 정한다. 요청은 상품 id와 request_id뿐이다.
import { randomUUID } from 'node:crypto';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import type { RaidShopData, RaidShopProduct, ShopRarity } from '../../gamedata/raidShop';
import { AppError } from '../../utils/AppError';
import { keyAt } from '../../utils/itemKey';
import { getRng, type Rng } from '../../utils/rng';
import { assertNoHold } from '../antiabuse/holds';
import * as charRepo from '../characters/characterRepository';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './raidShopRepository';
import type { BuyBody } from './raidShopValidation';

function shopData(): RaidShopData {
  const s = getGameData().raidShop;
  if (!s) throw new AppError(503, '레이드 상점을 준비 중입니다.', 'RAID_SHOP_UNAVAILABLE');
  return s;
}

/** 등급 가중치 굴림(서버 난수) */
export function rollShopRarity(p: RaidShopProduct, rng: Rng): ShopRarity {
  const total = p.rarityWeights.reduce((a, w) => a + w.weight, 0);
  let roll = rng.int(0, total);
  for (const w of p.rarityWeights) {
    if (roll < w.weight) return w.rarity;
    roll -= w.weight;
  }
  return (p.rarityWeights[0] as { rarity: ShopRarity }).rarity;
}

const resultOf = (p: RaidShopProduct, cls: string, rarity: ShopRarity): string => {
  const id = p.results[cls]?.[rarity];
  if (!id) throw new Error(`레이드 상점 결과 장비가 없습니다: ${p.id} ${cls} ${rarity}`);
  return keyAt(id, 0);
};

// ---------- GET /characters/:uuid/raid-shop ----------

export async function listShop(accountId: number, characterUuid: string) {
  const shop = shopData();
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const products = [];
  for (const p of shop.products.values()) {
    const have = await econRepo.stackCount(getPool(), c.id, 'bag', p.materialItem);
    const total = p.rarityWeights.reduce((a, w) => a + w.weight, 0);
    products.push({
      id: p.id,
      name: productName(p),
      raid_id: p.raidId,
      tier_level: p.tierLevel,
      material: { item_key: p.materialItem, name: p.materialName, price: p.price, have },
      outcomes: p.rarityWeights.map((w) => ({ rarity: w.rarity, percent: (w.weight * 100) / total, item_key: resultOf(p, c.class, w.rarity) })),
      affordable: have >= p.price,
    });
  }
  return { rates_version: shop.ratesVersion, products };
}

/** 화면 표시용 상품 이름(데이터에 이름 열이 없어 레이드 이름과 결과 등급으로 만든다) */
function productName(p: RaidShopProduct): string {
  const legend = p.rarityWeights.length === 1 && p.rarityWeights[0]?.rarity === 'Legendary';
  return `${p.raidName} ${legend ? '레전더리' : '에픽+'} 무기 상자`;
}

// ---------- POST /characters/:uuid/raid-shop/buy ----------

export function buy(accountId: number, characterUuid: string, body: BuyBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/raid-shop/buy',
    requestId,
    payload,
    handler: (ctx) => processBuy(ctx, body),
  });
}

async function processBuy(ctx: EconCtx, body: BuyBody) {
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
  const shop = shopData();
  const p = shop.products.get(body.product_id);
  if (!p) throw new AppError(422, '알 수 없는 상품입니다.', 'UNKNOWN_PRODUCT');

  const have = await ctx.stackCount('bag', p.materialItem);
  if (have < p.price) throw new AppError(422, '재료가 모자랍니다.', 'NOT_ENOUGH_MATERIAL', { need: p.price, have });
  const logId = randomUUID();
  const spent = await ctx.removeItem('bag', p.materialItem, p.price, 'raid_shop_cost', logId);
  if (spent === null) throw new AppError(422, '재료가 모자랍니다.', 'NOT_ENOUGH_MATERIAL', { need: p.price, have });

  const rarity = rollShopRarity(p, getRng());
  const resultKey = resultOf(p, ctx.char.class, rarity);
  // 재료를 거래 불가로 막았으므로 결과 장비도 캐릭터 귀속(간접 거래 차단)
  await ctx.addItem('bag', resultKey, 1, 'raid_shop_result', logId, 'character');
  await repo.insertPurchase(ctx.client, {
    accountId: ctx.char.accountId,
    characterId: ctx.char.id,
    requestId: ctx.requestId,
    productId: p.id,
    materialKey: p.materialItem,
    materialCost: p.price,
    resultKey,
    resultRarity: rarity,
    ratesVersion: shop.ratesVersion,
  });
  return {
    status: 200,
    data: {
      product_id: p.id,
      item_key: resultKey,
      rarity,
      cost: { item_key: p.materialItem, count: p.price },
      material_left: have - p.price,
      delta: ctx.delta(),
    },
  };
}
