// 기준가 reference(item_key, count)와 등록 가격 한도(phase6_api.md 6.1). 한도 계산은 BigInt 정수로 한다.
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { parseItemKey, roundHalfAway } from '../../utils/itemKey';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { sellPriceOf } from '../shop/shopService';

export interface Reference {
  source: 'history' | 'vendor' | 'none';
  /** history: 개당 중앙값을 1/10000 단위 정수로(unit_price가 numeric(16,4)) */
  medianMilli: number | null;
  /** vendor: 상점 기준 개당 가격 */
  vendorUnit: number | null;
}

/** 상점 기준가: 장비는 판매가 공식, 재료·소모품은 판매가와 구매가 중 큰 값. 없거나 0이면 null */
export function vendorUnitOf(itemKey: string): number | null {
  const eco = getGameData().economy;
  const p = parseItemKey(itemKey);
  if (!p) return null;
  let unit = sellPriceOf(itemKey);
  if (!eco.shop.equipment.has(p.base) && p.level === 0) unit = Math.max(unit, eco.shop.stock.get(p.base) ?? 0);
  return unit > 0 ? roundHalfAway(unit) : null;
}

/** windowEnd까지의 최근 N일 체결로 쌍 중복을 제한한 중앙값(1/10000 단위). 기록이 모자라면 null */
async function medianMilli(db: Queryable, itemKey: string, windowEnd: Date): Promise<number | null> {
  const a = getConfig().auction;
  const since = new Date(windowEnd.getTime() - a.refWindowDays * 86_400_000);
  const r = await db.query<{ n: string; buyers: string; med: number | null }>(
    `WITH recent AS (
       SELECT unit_price, buyer_account_id,
              ROW_NUMBER() OVER (PARTITION BY seller_account_id, buyer_account_id ORDER BY traded_at DESC, id DESC) AS rn
         FROM auction_trades WHERE item_key = $1 AND traded_at > $2 AND traded_at <= $3
     ), kept AS (SELECT * FROM recent WHERE rn <= $4)
     SELECT count(*) AS n, count(DISTINCT buyer_account_id) AS buyers,
            percentile_cont(0.5) WITHIN GROUP (ORDER BY unit_price) AS med
       FROM kept`,
    [itemKey, since, windowEnd, a.refPairMax],
  );
  const row = r.rows[0] as { n: string; buyers: string; med: number | null };
  if (row.med !== null && Number(row.n) >= a.refMinTrades && Number(row.buyers) >= a.refMinBuyers) {
    return Math.round(Number(row.med) * 10000);
  }
  return null;
}

/**
 * 체결 기록이 충분하면 쌍 중복을 제한한 중앙값, 아니면 상점 기준가, 둘 다 없으면 none.
 * 별도 계정 여러 개가 서로 거래해 기준을 끌어올리지 못하게, 오늘 중앙값은 직전 게임 일 값의 AUCTION_REF_DAILY_CAP_BPS 배를 넘지 못한다.
 */
export async function referenceOf(db: Queryable, itemKey: string, now: Date): Promise<Reference> {
  let med = await medianMilli(db, itemKey, now);
  if (med !== null) {
    const dayStart = new Date(resetBoundaries(now).dailyStartAt);
    const prev = await medianMilli(db, itemKey, dayStart);
    if (prev !== null) med = Math.min(med, Math.floor((prev * getConfig().auction.refDailyCapBps) / 10000));
    return { source: 'history', medianMilli: med, vendorUnit: null };
  }
  const vendor = vendorUnitOf(itemKey);
  return vendor === null
    ? { source: 'none', medianMilli: null, vendorUnit: null }
    : { source: 'vendor', medianMilli: null, vendorUnit: vendor };
}

const ceilDiv = (x: bigint, y: bigint): bigint => (x + y - 1n) / y;

/**
 * count개 묶음의 등록 가능한 즉시 구매가 범위. 하한은 올림, 상한은 내림. 기준이 없으면 null
 * itemKey를 주면 9단계 상점 품목 상한을 적용한다: 상점이 파는 기본 id의 +0이면 max = min(max, 상점가 x 개수 x AUCTION_SHOP_CEIL_MULT)
 * (체결 기록이 부풀려져도 상점가의 3배를 못 넘는다). 강화된 장비(+n)와 상점에서 팔지 않는 것은 해당 없음
 */
export function limitsOf(ref: Reference, count: number, itemKey?: string): { min: number; max: number } | null {
  const a = getConfig().auction;
  const data = getGameData().auction;
  const c = BigInt(count);
  let min: bigint;
  let max: bigint;
  if (ref.source === 'history' && ref.medianMilli !== null) {
    const base = BigInt(ref.medianMilli) * c; // 1/10000 단위
    min = ceilDiv(base * BigInt(data.priceFloorBps), 100_000_000n);
    max = (base * BigInt(data.priceCeilBps)) / 100_000_000n;
  } else if (ref.source === 'vendor' && ref.vendorUnit !== null) {
    min = BigInt(ref.vendorUnit) * c * BigInt(a.coldFloorMult);
    max = BigInt(ref.vendorUnit) * c * BigInt(a.coldCeilMult);
  } else {
    return null;
  }
  if (itemKey !== undefined) {
    const parsed = parseItemKey(itemKey);
    const shopPrice = parsed && parsed.level === 0 ? getGameData().economy.shop.stock.get(parsed.base) : undefined;
    if (shopPrice !== undefined) {
      const ceil = BigInt(shopPrice) * c * BigInt(getConfig().aa.auction.shopCeilMult);
      if (max > ceil) max = ceil;
    }
  }
  const cap = BigInt(a.maxPrice);
  if (min < 1n) min = 1n;
  if (max > cap) max = cap;
  if (min > max) min = max;
  return { min: Number(min), max: Number(max) };
}
