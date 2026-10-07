// 경매 읽기 요청: 검색, 시세, 등록 후보, 내 등록·입찰. 읽기 전에 내 마감분을 지연 정산한다(10.2).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { roundHalfAway } from '../../utils/itemKey';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { accountCreatedAt, countActiveOfSeller } from './auctionRepository';
import { limitsOf, referenceOf, type Reference } from './auctionPricing';
import { baseIdsMatching, factsOf, feePctOf } from './auctionRules';
import * as search from './auctionSearchRepository';
import { settleDueOfCharacter } from './auctionTicker';
import { myViewOf, viewOf } from './auctionView';
import type { PricesQuery, SearchQuery } from './auctionValidation';

const DAY_MS = 86_400_000;

async function mustOwn(accountId: number, characterUuid: string): Promise<search.OwnedChar> {
  const c = await search.ownedChar(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

/** A1 검색 */
export async function searchAuction(accountId: number, characterUuid: string, q: SearchQuery) {
  const c = await mustOwn(accountId, characterUuid);
  const now = getNow();
  const maxEnhance = getGameData().economy.enhance.maxEnhance;
  const meta = { total: 0, page: q.page, limit: q.limit };
  let bases: string[] | null = null;
  if (q.q !== undefined) {
    bases = baseIdsMatching(q.q);
    if (bases.length === 0) return { data: { listings: [] }, meta };
  }
  const r = await search.searchListings(getPool(), {
    now,
    myAccountId: c.accountId,
    myClass: c.class,
    bases,
    category: q.category,
    rarities: q.rarity ?? null,
    enhMin: q.enh_min ?? 0,
    enhMax: q.enh_max ?? maxEnhance,
    priceMin: q.price_min ?? null,
    priceMax: q.price_max ?? null,
    classMine: q.class === 'mine',
    sort: q.sort,
    limit: q.limit,
    offset: (q.page - 1) * q.limit,
  });
  return {
    data: { listings: r.rows.map((l) => viewOf(l, l.sellerName, c.id, now)) },
    meta: { ...meta, total: r.total },
  };
}

const roundTotal = (unit: number, count: number): number => roundHalfAway(unit * count);

/** A2 시세와 가격 한도 */
export async function priceOf(accountId: number, characterUuid: string, itemKey: string, q: PricesQuery) {
  await mustOwn(accountId, characterUuid);
  if (!factsOf(itemKey)) throw new AppError(404, '알 수 없는 아이템입니다.', 'ITEM_UNKNOWN');
  const now = getNow();
  const day = resetBoundaries(now).dailyStartAt;
  const ref = await referenceOf(getPool(), itemKey, now);
  const limits = limitsOf(ref, q.count, itemKey);
  const rows = await search.priceDaily(getPool(), itemKey, day, 14);
  const weekStart = Date.parse(day) - 6 * DAY_MS;
  const week = rows.filter((r) => r.day.getTime() >= weekStart);
  const volume = week.reduce((a, r) => a + r.volume, 0);
  const sum = week.reduce((a, r) => a + r.sumPrice, 0);
  const unitAvg = volume > 0 ? sum / volume : null;
  return {
    item_key: itemKey,
    count: q.count,
    source: ref.source,
    avg7d: unitAvg === null ? null : roundTotal(unitAvg, q.count),
    min: week.length ? roundTotal(Math.min(...week.map((r) => r.minUnit)), q.count) : null,
    max: week.length ? roundTotal(Math.max(...week.map((r) => r.maxUnit)), q.count) : null,
    unit_avg: unitAvg,
    volume,
    limits,
    daily: rows.map((r) => ({ day: r.day.toISOString(), avg: r.sumPrice / r.volume, volume: r.volume })),
  };
}

/** A3 내 가방의 등록 후보와 등록 자격 */
export async function sellable(accountId: number, characterUuid: string) {
  const c = await mustOwn(accountId, characterUuid);
  const cfg = getConfig().auction;
  const data = getGameData().auction;
  const eco = getGameData().economy;
  const now = getNow();
  const created = await accountCreatedAt(getPool(), accountId);
  const canList = c.level >= cfg.minLevel && created.getTime() <= now.getTime() - cfg.minAccountAgeDays * DAY_MS;
  const active = await countActiveOfSeller(getPool(), c.id);
  const refs = new Map<string, Reference>();
  const items = [];
  for (const row of await search.bagRows(getPool(), c.id)) {
    const facts = factsOf(row.itemKey);
    const base = eco.items.get(facts?.base ?? '');
    const maxCount = facts ? (facts.isEquipment ? 1 : Math.min(row.count, data.maxStack)) : 0;
    let reason: 'BOUND' | 'NOT_TRADABLE' | 'NO_PRICE' | null = null;
    let limits: { min: number; max: number } | null = null;
    if (!facts || !base || base.kind === 'currency' || base.bind !== 'none') reason = 'NOT_TRADABLE';
    else if (row.bind !== 'none') reason = 'BOUND';
    else {
      let ref = refs.get(row.itemKey);
      if (!ref) {
        ref = await referenceOf(getPool(), row.itemKey, now);
        refs.set(row.itemKey, ref);
      }
      limits = limitsOf(ref, maxCount, row.itemKey);
      if (!limits) reason = 'NO_PRICE';
    }
    items.push({
      item_key: row.itemKey,
      bind: row.bind,
      count: row.count,
      listable: reason === null,
      reason,
      max_count: maxCount,
      fee_pct: facts ? feePctOf(facts.isEquipment, data) : null,
      will_bind: facts?.isEquipment ? 'account' : null,
      limits,
    });
  }
  return {
    gate: { can_list: canList, need_level: cfg.minLevel, need_days: cfg.minAccountAgeDays, active, max_listings: data.maxListings },
    items,
  };
}

/** A8 내 등록과 내 최고 입찰 */
export async function mine(accountId: number, characterUuid: string) {
  const c = await mustOwn(accountId, characterUuid);
  await settleDueOfCharacter(c.id);
  const now = getNow();
  const max = getGameData().auction.maxListings;
  const [listings, bids] = await Promise.all([search.myListings(getPool(), c.id, max), search.myTopBids(getPool(), c.id, max)]);
  return {
    listings: listings.map((l) => myViewOf(l, l.sellerName, c.id, now)),
    bids: bids.map((l) => ({ ...viewOf(l, l.sellerName, c.id, now), my_amount: l.currentBid })),
  };
}
