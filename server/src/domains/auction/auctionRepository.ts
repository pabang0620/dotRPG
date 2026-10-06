// 경매 SQL. 변경은 호출하는 Service가 잠근 트랜잭션 안에서만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import type { Category } from './auctionRules';

export interface ListingRow {
  id: number;
  uuid: string;
  sellerCharacterId: number;
  sellerAccountId: number;
  itemKey: string;
  itemBase: string;
  enhance: number;
  count: number;
  category: Category;
  rarity: number | null;
  classOnly: 'warrior' | 'mage' | null;
  buyoutPrice: number;
  startBid: number | null;
  currentBid: number | null;
  currentBidderCharacterId: number | null;
  currentBidderAccountId: number | null;
  bidCount: number;
  deposit: number;
  feePct: number;
  durationHours: number;
  status: 'active' | 'sold' | 'expired' | 'cancelled';
  soldKind: 'buyout' | 'bid' | null;
  createdAt: Date;
  endsAt: Date;
  extendCount: number;
}

interface RawListing {
  id: string;
  uuid: string;
  seller_character_id: string;
  seller_account_id: string;
  item_key: string;
  item_base: string;
  enhance: number;
  count: number;
  category: Category;
  rarity: number | null;
  class_only: 'warrior' | 'mage' | null;
  buyout_price: string;
  start_bid: string | null;
  current_bid: string | null;
  current_bidder_character_id: string | null;
  current_bidder_account_id: string | null;
  bid_count: number;
  deposit: string;
  fee_pct: number;
  duration_hours: number;
  status: ListingRow['status'];
  sold_kind: 'buyout' | 'bid' | null;
  created_at: Date;
  ends_at: Date;
  extend_count: number;
}

export const LISTING_COLS = `id, uuid, seller_character_id, seller_account_id, item_key, item_base, enhance, count, category,
  rarity, class_only, buyout_price, start_bid, current_bid, current_bidder_character_id, current_bidder_account_id,
  bid_count, deposit, fee_pct, duration_hours, status, sold_kind, created_at, ends_at, extend_count`;

const num = (v: string | null): number | null => (v === null ? null : Number(v));

export function toListing(r: RawListing): ListingRow {
  return {
    id: Number(r.id),
    uuid: r.uuid,
    sellerCharacterId: Number(r.seller_character_id),
    sellerAccountId: Number(r.seller_account_id),
    itemKey: r.item_key,
    itemBase: r.item_base,
    enhance: r.enhance,
    count: r.count,
    category: r.category,
    rarity: r.rarity,
    classOnly: r.class_only,
    buyoutPrice: Number(r.buyout_price),
    startBid: num(r.start_bid),
    currentBid: num(r.current_bid),
    currentBidderCharacterId: num(r.current_bidder_character_id),
    currentBidderAccountId: num(r.current_bidder_account_id),
    bidCount: r.bid_count,
    deposit: Number(r.deposit),
    feePct: r.fee_pct,
    durationHours: r.duration_hours,
    status: r.status,
    soldKind: r.sold_kind,
    createdAt: r.created_at,
    endsAt: r.ends_at,
    extendCount: r.extend_count,
  };
}

/** 락 이후 재조회의 유일한 입구: 행을 잠그고 그 값을 돌려준다 */
export async function lockListingByUuid(client: PoolClient, uuid: string): Promise<ListingRow | null> {
  const r = await client.query<RawListing>(`SELECT ${LISTING_COLS} FROM auction_listings WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? toListing(r.rows[0]) : null;
}

/** 정산 틱·지연 정산: 사용 중인 행은 건너뛴다(SKIP LOCKED). 마감 지난 진행 중 등록만 */
export async function lockDueListing(client: PoolClient, id: number, now: Date): Promise<ListingRow | null> {
  const r = await client.query<RawListing>(
    `SELECT ${LISTING_COLS} FROM auction_listings
      WHERE id = $1 AND status = 'active' AND ends_at <= $2 FOR UPDATE SKIP LOCKED`,
    [id, now],
  );
  return r.rows[0] ? toListing(r.rows[0]) : null;
}

export async function dueListingIds(db: Queryable, now: Date, limit: number): Promise<number[]> {
  const r = await db.query<{ id: string }>(
    `SELECT id FROM auction_listings WHERE status = 'active' AND ends_at <= $1 ORDER BY ends_at LIMIT $2`,
    [now, limit],
  );
  return r.rows.map((x) => Number(x.id));
}

/** 내 캐릭터가 판매자이거나 최고 입찰자인 마감 지난 진행 중 등록(지연 정산 대상) */
export async function dueListingIdsOfCharacter(db: Queryable, characterId: number, now: Date): Promise<number[]> {
  const r = await db.query<{ id: string }>(
    `SELECT id FROM auction_listings
      WHERE status = 'active' AND ends_at <= $2
        AND (seller_character_id = $1 OR current_bidder_character_id = $1)
      ORDER BY ends_at LIMIT 100`,
    [characterId, now],
  );
  return r.rows.map((x) => Number(x.id));
}

export interface NewListing {
  uuid: string;
  sellerCharacterId: number;
  sellerAccountId: number;
  itemKey: string;
  itemBase: string;
  enhance: number;
  count: number;
  category: Category;
  rarity: number | null;
  classOnly: string | null;
  buyoutPrice: number;
  startBid: number | null;
  deposit: number;
  feePct: number;
  durationHours: number;
  createdAt: Date;
  endsAt: Date;
}

export async function insertListing(client: PoolClient, n: NewListing): Promise<ListingRow> {
  const r = await client.query<RawListing>(
    `INSERT INTO auction_listings (uuid, seller_character_id, seller_account_id, item_key, item_base, enhance, count,
        category, rarity, class_only, buyout_price, start_bid, deposit, fee_pct, duration_hours, created_at, ends_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)
     RETURNING ${LISTING_COLS}`,
    [
      n.uuid, n.sellerCharacterId, n.sellerAccountId, n.itemKey, n.itemBase, n.enhance, n.count, n.category, n.rarity,
      n.classOnly, n.buyoutPrice, n.startBid, n.deposit, n.feePct, n.durationHours, n.createdAt, n.endsAt,
    ],
  );
  return toListing(r.rows[0] as RawListing);
}

export async function countActiveOfSeller(db: Queryable, characterId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    "SELECT count(*) AS n FROM auction_listings WHERE seller_character_id = $1 AND status = 'active'",
    [characterId],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function closeListing(
  client: PoolClient,
  id: number,
  status: 'sold' | 'expired' | 'cancelled',
  soldKind: 'buyout' | 'bid' | null,
  clearBid: boolean,
  now: Date,
): Promise<void> {
  await client.query(
    `UPDATE auction_listings
        SET status = $2, sold_kind = $3, closed_at = $5, version = version + 1,
            current_bid = CASE WHEN $4 THEN NULL ELSE current_bid END,
            current_bidder_character_id = CASE WHEN $4 THEN NULL ELSE current_bidder_character_id END,
            current_bidder_account_id = CASE WHEN $4 THEN NULL ELSE current_bidder_account_id END
      WHERE id = $1`,
    [id, status, soldKind, clearBid, now],
  );
}

export async function updateBid(
  client: PoolClient,
  id: number,
  amount: number,
  characterId: number,
  accountId: number,
  endsAt: Date,
  extendCount: number,
): Promise<void> {
  await client.query(
    `UPDATE auction_listings
        SET current_bid = $2, current_bidder_character_id = $3, current_bidder_account_id = $4,
            bid_count = bid_count + 1, ends_at = $5, extend_count = $6, version = version + 1
      WHERE id = $1`,
    [id, amount, characterId, accountId, endsAt, extendCount],
  );
}

// ---------- 입찰 ----------

export interface TopBid {
  id: number;
  uuid: string;
  bidderCharacterId: number;
  amount: number;
}

export async function getTopBid(client: Queryable, listingId: number): Promise<TopBid | null> {
  const r = await client.query<{ id: string; uuid: string; bidder_character_id: string; amount: string }>(
    "SELECT id, uuid, bidder_character_id, amount FROM auction_bids WHERE listing_id = $1 AND state = 'top'",
    [listingId],
  );
  const row = r.rows[0];
  return row
    ? { id: Number(row.id), uuid: row.uuid, bidderCharacterId: Number(row.bidder_character_id), amount: Number(row.amount) }
    : null;
}

export async function closeBid(
  client: PoolClient,
  bidId: number,
  state: 'outbid' | 'won' | 'lost_to_buyout',
  now: Date,
): Promise<void> {
  await client.query('UPDATE auction_bids SET state = $2, closed_at = $3 WHERE id = $1', [bidId, state, now]);
}

export async function insertBid(
  client: PoolClient,
  uuid: string,
  listingId: number,
  characterId: number,
  accountId: number,
  amount: number,
  requestId: string,
  now: Date,
): Promise<number> {
  const r = await client.query<{ id: string }>(
    `INSERT INTO auction_bids (uuid, listing_id, bidder_character_id, bidder_account_id, amount, request_id, created_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id`,
    [uuid, listingId, characterId, accountId, amount, requestId, now],
  );
  return Number((r.rows[0] as { id: string }).id);
}

// ---------- 체결·시세·소각·의심 기록 ----------

export interface NewTrade {
  listingId: number;
  itemKey: string;
  itemBase: string;
  count: number;
  price: number;
  feePct: number;
  fee: number;
  depositReturned: number;
  sellerPayout: number;
  kind: 'buyout' | 'bid';
  buyerCharacterId: number;
  buyerAccountId: number;
  sellerCharacterId: number;
  sellerAccountId: number;
  tradedAt: Date;
}

export async function insertTrade(client: PoolClient, t: NewTrade): Promise<number> {
  const r = await client.query<{ id: string }>(
    `INSERT INTO auction_trades (listing_id, item_key, item_base, count, price, fee_pct, fee, deposit_returned,
        seller_payout, kind, buyer_character_id, buyer_account_id, seller_character_id, seller_account_id, traded_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15) RETURNING id`,
    [
      t.listingId, t.itemKey, t.itemBase, t.count, t.price, t.feePct, t.fee, t.depositReturned, t.sellerPayout, t.kind,
      t.buyerCharacterId, t.buyerAccountId, t.sellerCharacterId, t.sellerAccountId, t.tradedAt,
    ],
  );
  return Number((r.rows[0] as { id: string }).id);
}

export async function upsertPriceDaily(
  client: PoolClient,
  itemKey: string,
  dayStart: string,
  count: number,
  price: number,
  now: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO auction_price_daily (item_key, day_start, trade_count, volume, sum_price, min_unit_price, max_unit_price, updated_at)
     VALUES ($1, $2, 1, $3, $4, round($6::numeric / $7::numeric, 4), round($6::numeric / $7::numeric, 4), $5)
     ON CONFLICT (item_key, day_start) DO UPDATE SET
       trade_count = auction_price_daily.trade_count + 1,
       volume = auction_price_daily.volume + EXCLUDED.volume,
       sum_price = auction_price_daily.sum_price + EXCLUDED.sum_price,
       min_unit_price = LEAST(auction_price_daily.min_unit_price, EXCLUDED.min_unit_price),
       max_unit_price = GREATEST(auction_price_daily.max_unit_price, EXCLUDED.max_unit_price),
       updated_at = EXCLUDED.updated_at`,
    [itemKey, dayStart, count, price, now, price, count],
  );
}

export async function insertSink(
  client: PoolClient,
  kind: 'fee' | 'deposit_forfeit' | 'mail_expire',
  amount: number,
  listingId: number | null,
  mailId: number | null,
  characterId: number | null,
  now: Date,
): Promise<void> {
  if (amount <= 0) return;
  await client.query(
    'INSERT INTO auction_sinks (kind, amount, listing_id, mail_id, character_id, created_at) VALUES ($1, $2, $3, $4, $5, $6)',
    [kind, amount, listingId, mailId, characterId, now],
  );
}

export type FlagKind = 'self_account' | 'price_band' | 'pair_limit' | 'foreign_id' | 'gate';

/** 롤백되는 트랜잭션 밖(풀)에서 호출해서 거절돼도 남긴다 */
export async function insertFlag(
  db: Queryable,
  accountId: number,
  characterId: number | null,
  kind: FlagKind,
  severity: 1 | 2 | 3,
  detail: Record<string, unknown>,
): Promise<void> {
  await db.query(
    'INSERT INTO auction_flags (account_id, character_id, kind, severity, detail) VALUES ($1, $2, $3, $4, $5::jsonb)',
    [accountId, characterId, kind, severity, JSON.stringify(detail)],
  );
}

/**
 * 두 계정 사이의 오늘(게임 일) 체결 수와 금액 합 + 진행 중 등록에 걸린 최고 입찰(아직 체결 전이지만 마감에 낙찰된다).
 * 9단계: 양방향 합산이다(A->B 3건 뒤 B->A 구매도 같은 한도를 쓴다: 부계정 이전 통로를 양쪽에서 막는다).
 * excludeListingId는 지금 판정 중인 등록(이 등록의 내 입찰은 세지 않는다). 호출 전에 lockPair로 쌍을 직렬화해야 정확하다.
 * 기존 인덱스 auction_trades_pair(seller, buyer, traded_at)가 두 방향 범위 스캔에 그대로 쓰인다.
 */
export async function pairToday(
  db: Queryable,
  accountA: number,
  accountB: number,
  dayStart: string,
  excludeListingId: number,
): Promise<{ trades: number; gold: number }> {
  const r = await db.query<{ n: string; g: string }>(
    `SELECT (SELECT count(*) FROM auction_trades
              WHERE ((seller_account_id = $1 AND buyer_account_id = $2) OR (seller_account_id = $2 AND buyer_account_id = $1)) AND traded_at >= $3)
          + (SELECT count(*) FROM auction_listings
              WHERE ((seller_account_id = $1 AND current_bidder_account_id = $2) OR (seller_account_id = $2 AND current_bidder_account_id = $1))
                AND status = 'active' AND id <> $4) AS n,
            (SELECT coalesce(sum(price), 0) FROM auction_trades
              WHERE ((seller_account_id = $1 AND buyer_account_id = $2) OR (seller_account_id = $2 AND buyer_account_id = $1)) AND traded_at >= $3)
          + (SELECT coalesce(sum(current_bid), 0) FROM auction_listings
              WHERE ((seller_account_id = $1 AND current_bidder_account_id = $2) OR (seller_account_id = $2 AND current_bidder_account_id = $1))
                AND status = 'active' AND id <> $4) AS g`,
    [accountA, accountB, dayStart, excludeListingId],
  );
  const row = r.rows[0] as { n: string; g: string };
  return { trades: Number(row.n), gold: Number(row.g) };
}

/**
 * 같은 계정 쌍의 동시 요청(다른 캐릭터, 다른 등록, 반대 방향)을 직렬화한다. listing 락 뒤에 잡는다(8.3의 안전한 순서).
 * 9단계: 키가 대칭이다(작은 id:큰 id). 예전에는 seller:buyer 순서라 반대 방향 요청이 서로 직렬화되지 않았다.
 */
export async function lockPair(client: PoolClient, accountA: number, accountB: number): Promise<void> {
  const lo = Math.min(accountA, accountB);
  const hi = Math.max(accountA, accountB);
  await client.query('SELECT pg_advisory_xact_lock(hashtextextended($1, 0))', [`auction-pair:${lo}:${hi}`]);
}

export async function accountCreatedAt(db: Queryable, accountId: number): Promise<Date> {
  const r = await db.query<{ created_at: Date }>('SELECT created_at FROM accounts WHERE id = $1', [accountId]);
  return (r.rows[0] as { created_at: Date }).created_at;
}

export async function characterName(db: Queryable, characterId: number): Promise<string> {
  const r = await db.query<{ name: string }>('SELECT name FROM characters WHERE id = $1', [characterId]);
  return (r.rows[0] as { name: string }).name;
}
