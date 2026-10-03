// 경매 조회 SQL(검색, 내 등록, 내 입찰, 시세). 값은 전부 $n 파라미터이고 정렬·열 이름만 고정 문자열이다.
import type { Queryable } from '../../db/pool';
import { LISTING_COLS, toListing, type ListingRow } from './auctionRepository';

const L_COLS = LISTING_COLS.split(',').map((c) => `l.${c.trim()}`).join(', ');

export interface ListingWithSeller extends ListingRow {
  sellerName: string;
}

interface RawWithSeller {
  seller_name: string;
}

function withSeller(r: Parameters<typeof toListing>[0] & RawWithSeller): ListingWithSeller {
  return { ...toListing(r), sellerName: r.seller_name };
}

export interface OwnedChar {
  id: number;
  accountId: number;
  class: 'warrior' | 'mage';
  level: number;
  name: string;
}

export async function ownedChar(db: Queryable, accountId: number, uuid: string): Promise<OwnedChar | null> {
  const r = await db.query<{ id: string; account_id: string; class: 'warrior' | 'mage'; level: number; name: string }>(
    'SELECT id, account_id, class, level, name FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL',
    [uuid, accountId],
  );
  const row = r.rows[0];
  return row ? { id: Number(row.id), accountId: Number(row.account_id), class: row.class, level: row.level, name: row.name } : null;
}

export interface SearchParams {
  now: Date;
  myAccountId: number;
  myClass: 'warrior' | 'mage';
  bases: string[] | null;
  category: string;
  rarities: number[] | null;
  enhMin: number;
  enhMax: number;
  priceMin: number | null;
  priceMax: number | null;
  classMine: boolean;
  sort: 'price_asc' | 'price_desc' | 'unit_price_asc' | 'time_left' | 'enhance_desc' | 'newest';
  limit: number;
  offset: number;
}

const ORDER: Record<SearchParams['sort'], string> = {
  price_asc: 'l.buyout_price ASC, l.id ASC',
  price_desc: 'l.buyout_price DESC, l.id DESC',
  unit_price_asc: '(l.buyout_price::numeric / l.count) ASC, l.id ASC',
  time_left: 'l.ends_at ASC, l.id ASC',
  enhance_desc: 'l.enhance DESC, l.id DESC',
  newest: 'l.id DESC',
};

export async function searchListings(db: Queryable, p: SearchParams): Promise<{ rows: ListingWithSeller[]; total: number }> {
  const args: unknown[] = [p.now, p.myAccountId];
  const where = ["l.status = 'active'", 'l.ends_at > $1', 'l.seller_account_id <> $2'];
  const add = (cond: string, value: unknown): void => {
    args.push(value);
    where.push(cond.replace('?', `$${args.length}`));
  };
  if (p.bases !== null) add('l.item_base = ANY(?::text[])', p.bases);
  if (p.category !== 'all') add('l.category = ?', p.category);
  if (p.rarities !== null) add('l.rarity = ANY(?::int[])', p.rarities);
  add('l.enhance >= ?', p.enhMin);
  add('l.enhance <= ?', p.enhMax);
  if (p.priceMin !== null) add('l.buyout_price >= ?', p.priceMin);
  if (p.priceMax !== null) add('l.buyout_price <= ?', p.priceMax);
  if (p.classMine) add('(l.class_only IS NULL OR l.class_only = ?)', p.myClass);
  const w = where.join(' AND ');
  const total = await db.query<{ n: string }>(`SELECT count(*) AS n FROM auction_listings l WHERE ${w}`, args);
  args.push(p.limit, p.offset);
  const rows = await db.query<Parameters<typeof toListing>[0] & RawWithSeller>(
    `SELECT ${L_COLS}, c.name AS seller_name
       FROM auction_listings l JOIN characters c ON c.id = l.seller_character_id
      WHERE ${w} ORDER BY ${ORDER[p.sort]} LIMIT $${args.length - 1} OFFSET $${args.length}`,
    args,
  );
  return { rows: rows.rows.map(withSeller), total: Number((total.rows[0] as { n: string }).n) };
}

export async function myListings(db: Queryable, characterId: number, limit: number): Promise<ListingWithSeller[]> {
  const r = await db.query<Parameters<typeof toListing>[0] & RawWithSeller>(
    `SELECT ${L_COLS}, c.name AS seller_name
       FROM auction_listings l JOIN characters c ON c.id = l.seller_character_id
      WHERE l.seller_character_id = $1 AND l.status = 'active' ORDER BY l.id DESC LIMIT $2`,
    [characterId, limit],
  );
  return r.rows.map(withSeller);
}

export async function myTopBids(db: Queryable, characterId: number, limit: number): Promise<ListingWithSeller[]> {
  const r = await db.query<Parameters<typeof toListing>[0] & RawWithSeller>(
    `SELECT ${L_COLS}, c.name AS seller_name
       FROM auction_listings l JOIN characters c ON c.id = l.seller_character_id
      WHERE l.current_bidder_character_id = $1 AND l.status = 'active' ORDER BY l.ends_at LIMIT $2`,
    [characterId, limit],
  );
  return r.rows.map(withSeller);
}

export async function activeCountOfBidder(db: Queryable, characterId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    "SELECT count(*) AS n FROM auction_listings WHERE current_bidder_character_id = $1 AND status = 'active'",
    [characterId],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 캐릭터 삭제 거절 판정: 진행 중 등록 또는 최고 입찰이 있는가 */
export async function hasActiveAuction(db: Queryable, characterId: number): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM auction_listings
      WHERE status = 'active' AND (seller_character_id = $1 OR current_bidder_character_id = $1) LIMIT 1`,
    [characterId],
  );
  return (r.rowCount ?? 0) > 0;
}

export interface DailyRow {
  day: Date;
  tradeCount: number;
  volume: number;
  sumPrice: number;
  minUnit: number;
  maxUnit: number;
}

/** 시세 일별 행(최근 days 게임 일). dayStart는 오늘 게임 일의 시작 */
export async function priceDaily(db: Queryable, itemKey: string, dayStart: string, days: number): Promise<DailyRow[]> {
  const r = await db.query<{
    day_start: Date;
    trade_count: number;
    volume: number;
    sum_price: string;
    min_unit_price: string;
    max_unit_price: string;
  }>(
    `SELECT day_start, trade_count, volume, sum_price, min_unit_price, max_unit_price FROM auction_price_daily
      WHERE item_key = $1 AND day_start > $2::timestamptz - make_interval(days => $3) ORDER BY day_start`,
    [itemKey, dayStart, days],
  );
  return r.rows.map((x) => ({
    day: x.day_start,
    tradeCount: x.trade_count,
    volume: x.volume,
    sumPrice: Number(x.sum_price),
    minUnit: Number(x.min_unit_price),
    maxUnit: Number(x.max_unit_price),
  }));
}

export interface BagRow {
  itemKey: string;
  bind: 'none' | 'account' | 'character';
  count: number;
}

export async function bagRows(db: Queryable, characterId: number): Promise<BagRow[]> {
  const r = await db.query<{ item_key: string; bind: BagRow['bind']; count: number }>(
    "SELECT item_key, bind, count FROM character_items WHERE character_id = $1 AND location = 'bag' ORDER BY item_key, bind",
    [characterId],
  );
  return r.rows.map((x) => ({ itemKey: x.item_key, bind: x.bind, count: x.count }));
}
