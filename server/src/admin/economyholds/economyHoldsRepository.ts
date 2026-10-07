// 관리자 경제 정지 조회 SQL(H1, H2, H6, H7, H9). 읽기 전용 쿼리와 회수(H4)가 쓰는 원장 조회. 변경은 service가 트랜잭션 안에서 부른다.
import type { Queryable } from '../../db/pool';
import { GOLD_REASONS } from '../../domains/antiabuse/incomeMeter';
import { HOLD_COLS, toHold, type HoldRow } from '../../domains/antiabuse/holdsRepository';

export interface HoldListRow extends HoldRow {
  account_uuid: string;
  character_uuid: string | null;
  character_name: string | null;
  character_level: number | null;
  linked_count: number;
}

interface RawList extends Omit<HoldListRow, 'id' | 'account_id' | 'character_id' | 'origin_hold_id' | 'linked_count'> {
  id: string;
  account_id: string;
  character_id: string | null;
  origin_hold_id: string | null;
  linked_count: string;
}

export const encodeCursor = (id: number): string => Buffer.from(String(id)).toString('base64url');
export function decodeCursor(c: string | undefined): number | null {
  if (!c) return null;
  const n = Number(Buffer.from(c, 'base64url').toString('utf8'));
  return Number.isInteger(n) && n > 0 ? n : null;
}

const LIST_SELECT = `SELECT ${HOLD_COLS.split(',').map((c) => `h.${c.trim()}`).join(', ')},
        a.uuid AS account_uuid, c.uuid AS character_uuid, c.name AS character_name, c.level AS character_level,
        (SELECT count(*) FROM economy_holds k WHERE k.origin_hold_id = h.id) AS linked_count
   FROM economy_holds h JOIN accounts a ON a.id = h.account_id LEFT JOIN characters c ON c.id = h.character_id`;

const toList = (r: RawList): HoldListRow => ({ ...toHold(r), account_uuid: r.account_uuid, character_uuid: r.character_uuid, character_name: r.character_name, character_level: r.character_level, linked_count: Number(r.linked_count) });

export async function listHolds(db: Queryable, f: { state?: string | undefined; kind?: string | undefined; q?: string | undefined; cursor: number | null; limit: number }): Promise<HoldListRow[]> {
  const r = await db.query<RawList>(
    `${LIST_SELECT}
      WHERE ($1::text IS NULL OR h.state = $1) AND ($2::text IS NULL OR h.kind = $2)
        AND ($3::text IS NULL OR c.name ILIKE $3 OR a.uuid::text = $4)
        AND ($5::bigint IS NULL OR h.id < $5)
      ORDER BY h.created_at DESC, h.id DESC LIMIT $6`,
    [f.state ?? null, f.kind ?? null, f.q ? `%${f.q.replace(/[%_\\]/g, '\\$&')}%` : null, f.q ?? '', f.cursor, f.limit + 1],
  );
  return r.rows.map(toList);
}

export async function holdListRow(db: Queryable, uuid: string): Promise<HoldListRow | null> {
  const r = await db.query<RawList>(`${LIST_SELECT} WHERE h.uuid = $1`, [uuid]);
  return r.rows[0] ? toList(r.rows[0]) : null;
}

export async function linkedHolds(db: Queryable, originId: number): Promise<{ uuid: string; account_uuid: string; state: string; evidence: Record<string, unknown> }[]> {
  const r = await db.query<{ uuid: string; account_uuid: string; state: string; evidence: Record<string, unknown> }>(
    'SELECT h.uuid, a.uuid AS account_uuid, h.state, h.evidence FROM economy_holds h JOIN accounts a ON a.id = h.account_id WHERE h.origin_hold_id = $1 ORDER BY h.id',
    [originId],
  );
  return r.rows;
}

export async function accountUuid(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1', [accountId]);
  return r.rows[0]?.uuid ?? null;
}

export async function accountIdByUuid(db: Queryable, uuid: string): Promise<number | null> {
  const r = await db.query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1 AND deleted_at IS NULL', [uuid]);
  return r.rows[0] ? Number(r.rows[0].id) : null;
}

export async function characterByUuid(db: Queryable, uuid: string): Promise<{ id: number; account_id: number; level: number; name: string } | null> {
  const r = await db.query<{ id: string; account_id: string; level: number; name: string }>('SELECT id, account_id, level, name FROM characters WHERE uuid = $1 AND deleted_at IS NULL', [uuid]);
  const x = r.rows[0];
  return x ? { id: Number(x.id), account_id: Number(x.account_id), level: x.level, name: x.name } : null;
}

export async function aliveCharacterIds(db: Queryable, accountId: number): Promise<number[]> {
  const r = await db.query<{ id: string }>('SELECT id FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY id', [accountId]);
  return r.rows.map((x) => Number(x.id));
}

/** 가장 최근에 소득이 기록된 캐릭터(없으면 id가 가장 큰 캐릭터): 계정 단위 정지의 창별 수치를 보여 줄 대표 */
export async function latestCharacter(db: Queryable, accountId: number): Promise<{ id: number; level: number } | null> {
  const r = await db.query<{ id: string; level: number }>(
    `SELECT c.id, c.level FROM characters c WHERE c.account_id = $1 AND c.deleted_at IS NULL
      ORDER BY (SELECT max(i.updated_at) FROM income_hourly i WHERE i.character_id = c.id) DESC NULLS LAST, c.id DESC LIMIT 1`,
    [accountId],
  );
  return r.rows[0] ? { id: Number(r.rows[0].id), level: r.rows[0].level } : null;
}

export async function characterIdentity(db: Queryable, characterId: number): Promise<{ uuid: string; name: string; level: number; account_id: number } | null> {
  const r = await db.query<{ uuid: string; name: string; level: number; account_id: string }>('SELECT uuid, name, level, account_id FROM characters WHERE id = $1', [characterId]);
  return r.rows[0] ? { ...r.rows[0], account_id: Number(r.rows[0].account_id) } : null;
}

// ---------- 근거 확인용 원장 요약(H2) ----------

export interface ReasonSum {
  reason: string;
  lines: number;
  total: number;
}

export async function ledgerByReason(db: Queryable, table: 'gold_ledger' | 'item_ledger' | 'xp_ledger', characterIds: number[], from: Date, to: Date): Promise<ReasonSum[]> {
  const r = await db.query<{ reason: string; n: string; s: string }>(
    `SELECT reason, count(*) AS n, sum(delta) AS s FROM ${table} WHERE character_id = ANY($1::bigint[]) AND created_at >= $2 AND created_at <= $3 GROUP BY reason ORDER BY reason`,
    [characterIds, from, to],
  );
  return r.rows.map((x) => ({ reason: x.reason, lines: Number(x.n), total: Number(x.s) }));
}

export async function flaggedTradesOf(db: Queryable, accountId: number, since: Date): Promise<{ trade_uuid: string; item_key: string; price: number; flags: string[]; traded_at: Date }[]> {
  const r = await db.query<{ uuid: string; item_key: string; price: string; flags: string[]; traded_at: Date }>(
    `SELECT t.uuid, t.item_key, t.price, array_agg(f.flag ORDER BY f.flag) AS flags, t.traded_at
       FROM auction_trades t JOIN auction_trade_flags f ON f.trade_id = t.id
      WHERE (t.seller_account_id = $1 OR t.buyer_account_id = $1) AND t.traded_at > $2
      GROUP BY t.id ORDER BY t.traded_at DESC LIMIT 50`,
    [accountId, since],
  );
  return r.rows.map((x) => ({ trade_uuid: x.uuid, item_key: x.item_key, price: Number(x.price), flags: x.flags, traded_at: x.traded_at }));
}

// ---------- 연결 조회(H6) ----------

export async function deviceRows(db: Queryable, accountId: number): Promise<{ device_hash: string; first_seen_at: Date; last_seen_at: Date; accounts: number }[]> {
  const r = await db.query<{ device_hash: string; first_seen_at: Date; last_seen_at: Date; n: string }>(
    `SELECT d.device_hash, d.first_seen_at, d.last_seen_at, (SELECT count(DISTINCT x.account_id) FROM account_devices x WHERE x.device_hash = d.device_hash) AS n
       FROM account_devices d WHERE d.account_id = $1 ORDER BY d.last_seen_at DESC`,
    [accountId],
  );
  return r.rows.map((x) => ({ device_hash: x.device_hash, first_seen_at: x.first_seen_at, last_seen_at: x.last_seen_at, accounts: Number(x.n) }));
}

export async function ipRows(db: Queryable, accountId: number): Promise<{ ip: string; last_seen_at: Date; accounts: number }[]> {
  const r = await db.query<{ ip: string; last_seen_at: Date; n: string }>(
    `SELECT host(d.ip) AS ip, d.last_seen_at, (SELECT count(DISTINCT x.account_id) FROM account_ips x WHERE x.ip = d.ip) AS n
       FROM account_ips d WHERE d.account_id = $1 ORDER BY d.last_seen_at DESC`,
    [accountId],
  );
  return r.rows.map((x) => ({ ip: x.ip, last_seen_at: x.last_seen_at, accounts: Number(x.n) }));
}

export async function linkedAccounts(db: Queryable, accountId: number): Promise<{ account_uuid: string; via: string; last_seen_at: Date | null }[]> {
  const r = await db.query<{ account_uuid: string; via: string; last_seen_at: Date | null }>(
    `SELECT a.uuid AS account_uuid, 'device' AS via, max(y.last_seen_at) AS last_seen_at
       FROM account_devices x JOIN account_devices y ON y.device_hash = x.device_hash AND y.account_id <> x.account_id
       JOIN accounts a ON a.id = y.account_id WHERE x.account_id = $1 GROUP BY a.uuid
     UNION ALL
     SELECT a.uuid, 'ip', max(y.last_seen_at)
       FROM account_ips x JOIN account_ips y ON y.ip = x.ip AND y.account_id <> x.account_id
       JOIN accounts a ON a.id = y.account_id WHERE x.account_id = $1 GROUP BY a.uuid
     UNION ALL
     SELECT a.uuid, 'steam', NULL::timestamptz
       FROM auth_identities x JOIN auth_identities y ON COALESCE(y.steam_owner_id, y.subject) = COALESCE(x.steam_owner_id, x.subject) AND y.account_id <> x.account_id AND y.provider = 'steam'
       JOIN accounts a ON a.id = y.account_id WHERE x.account_id = $1 AND x.provider = 'steam'`,
    [accountId],
  );
  return r.rows;
}

export async function steamKeyAndCount(db: Queryable, accountId: number): Promise<{ key: string; accounts: number } | null> {
  const r = await db.query<{ k: string; n: string }>(
    `SELECT COALESCE(i.steam_owner_id, i.subject) AS k,
            (SELECT count(*) FROM auth_identities y WHERE y.provider = 'steam' AND COALESCE(y.steam_owner_id, y.subject) = COALESCE(i.steam_owner_id, i.subject)) AS n
       FROM auth_identities i WHERE i.account_id = $1 AND i.provider = 'steam'`,
    [accountId],
  );
  return r.rows[0] ? { key: r.rows[0].k, accounts: Number(r.rows[0].n) } : null;
}

// ---------- 의심 거래 목록(H7) ----------

export interface TradeFlagRow {
  trade_id: number;
  trade_uuid: string;
  item_key: string;
  price: number;
  traded_at: Date;
  seller_account_uuid: string;
  seller_character_uuid: string;
  seller_name: string;
  buyer_account_uuid: string;
  buyer_character_uuid: string;
  buyer_name: string;
  flags: { flag: string; detail: Record<string, unknown> }[];
}

export async function listTradeFlags(db: Queryable, f: { flag?: string | undefined; since?: Date | undefined; minPrice?: number | undefined; cursor: number | null; limit: number }): Promise<TradeFlagRow[]> {
  const r = await db.query<{
    trade_id: string; trade_uuid: string; item_key: string; price: string; traded_at: Date;
    sa: string; sc: string; sn: string; ba: string; bc: string; bn: string; flags: { flag: string; detail: Record<string, unknown> }[];
  }>(
    `SELECT t.id AS trade_id, t.uuid AS trade_uuid, t.item_key, t.price, t.traded_at,
            sa.uuid AS sa, sc.uuid AS sc, sc.name AS sn, ba.uuid AS ba, bc.uuid AS bc, bc.name AS bn,
            (SELECT json_agg(json_build_object('flag', x.flag, 'detail', x.detail) ORDER BY x.flag) FROM auction_trade_flags x WHERE x.trade_id = t.id) AS flags
       FROM auction_trades t
       JOIN accounts sa ON sa.id = t.seller_account_id JOIN characters sc ON sc.id = t.seller_character_id
       JOIN accounts ba ON ba.id = t.buyer_account_id JOIN characters bc ON bc.id = t.buyer_character_id
      WHERE EXISTS (SELECT 1 FROM auction_trade_flags x WHERE x.trade_id = t.id AND ($1::text IS NULL OR x.flag = $1))
        AND ($2::timestamptz IS NULL OR t.traded_at >= $2) AND ($3::bigint IS NULL OR t.price >= $3) AND ($4::bigint IS NULL OR t.id < $4)
      ORDER BY t.traded_at DESC, t.id DESC LIMIT $5`,
    [f.flag ?? null, f.since ?? null, f.minPrice ?? null, f.cursor, f.limit + 1],
  );
  return r.rows.map((x) => ({
    trade_id: Number(x.trade_id), trade_uuid: x.trade_uuid, item_key: x.item_key, price: Number(x.price), traded_at: x.traded_at,
    seller_account_uuid: x.sa, seller_character_uuid: x.sc, seller_name: x.sn, buyer_account_uuid: x.ba, buyer_character_uuid: x.bc, buyer_name: x.bn, flags: x.flags ?? [],
  }));
}

// ---------- 회수(H4) ----------

/**
 * 근거 구간 [from, to]에서 캐릭터가 얻은 골드: 원장의 획득 사유 양수 합(created_at 정확히) + 경매 순유입(판매 대금 - 구매 대금, traded_at 정확히).
 * 시간 단위 집계 버킷은 구간 밖 수입까지 포함하므로 쓰지 않는다.
 */
export async function goldGainedInWindow(db: Queryable, characterId: number, from: Date, to: Date): Promise<number> {
  const g = await db.query<{ g: string }>(
    `SELECT coalesce(sum(delta), 0) AS g FROM gold_ledger
      WHERE character_id = $1 AND created_at >= $2 AND created_at <= $3 AND delta > 0 AND reason = ANY($4::text[])`,
    [characterId, from, to, [...GOLD_REASONS]],
  );
  const a = await db.query<{ i: string; o: string }>(
    `SELECT coalesce(sum(seller_payout) FILTER (WHERE seller_character_id = $1), 0) AS i,
            coalesce(sum(price) FILTER (WHERE buyer_character_id = $1), 0) AS o
       FROM auction_trades WHERE (seller_character_id = $1 OR buyer_character_id = $1) AND traded_at >= $2 AND traded_at <= $3`,
    [characterId, from, to],
  );
  const x = a.rows[0] as { i: string; o: string };
  return Number((g.rows[0] as { g: string }).g) + Math.max(0, Number(x.i) - Number(x.o));
}

/** 근거 구간에서 아이템별 획득량, 같은 구간의 사용·제거량(음수 합의 절대값). 구간 안에서 순증가한 만큼만 회수 대상이다.
 * item_key는 강화 단계를 포함한 정확한 키("id+N")다. 키별로 따로 집계하므로 구간 안에서 얻지 않은 다른 강화 단계 개체는 건드리지 않는다. 가방·창고 사이 이동(storage_move)은 합계가 변하지 않으므로 사용으로 세지 않는다 */
export async function itemsNetGainedInWindow(db: Queryable, characterId: number, from: Date, to: Date, reasons: string[]): Promise<{ item_key: string; n: number }[]> {
  const r = await db.query<{ item_key: string; n: string }>(
    `SELECT item_key,
            sum(delta) FILTER (WHERE delta > 0 AND reason = ANY($4::text[])) AS gained,
            sum(delta) FILTER (WHERE delta < 0 AND reason NOT IN ('admin_clawback', 'storage_move')) AS spent
       FROM item_ledger
      WHERE character_id = $1 AND created_at >= $2 AND created_at <= $3 AND location IN ('bag', 'storage')
      GROUP BY item_key HAVING coalesce(sum(delta) FILTER (WHERE delta > 0 AND reason = ANY($4::text[])), 0) > 0 ORDER BY item_key`,
    [characterId, from, to, reasons],
  );
  return r.rows
    .map((x) => {
      const row = x as unknown as { item_key: string; gained: string; spent: string | null };
      return { item_key: row.item_key, n: Math.max(0, Number(row.gained) + Number(row.spent ?? 0)) };
    })
    .filter((x) => x.n > 0);
}

/**
 * 근거 구간의 미수령 거래 우편(판매 대금, 구매 아이템)을 지금 만료시킨다. 기존 우편 만료 정리가 소각 기록을 남긴다.
 * 돌려주는 sold_gold는 구간([from, to]) 안에서 만든 판매 대금 우편의 골드 합이다(경매 순유입에 이미 들어 있으므로 지갑 회수에서 뺀다).
 */
export async function voidMails(db: Queryable, characterId: number, from: Date, to: Date, now: Date): Promise<{ count: number; soldGold: number }> {
  const r = await db.query<{ kind: string; gold: string; created_at: Date }>(
    `UPDATE mails SET expires_at = $3
      WHERE character_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND kind IN ('sold', 'bought') AND created_at >= $2
      RETURNING kind, gold, created_at`,
    [characterId, from, now],
  );
  const soldGold = r.rows.filter((m) => m.kind === 'sold' && m.created_at <= to).reduce((a, m) => a + Number(m.gold), 0);
  return { count: r.rowCount ?? 0, soldGold };
}

export async function markClawedBack(db: Queryable, id: number, by: string, note: string, summary: Record<string, unknown>, now: Date): Promise<void> {
  await db.query(
    `UPDATE economy_holds SET state = 'clawed_back', reviewed_by = $2, reviewed_at = $4, note = $3, clawback = $5::jsonb WHERE id = $1`,
    [id, by, note, now, JSON.stringify(summary)],
  );
}

/** 이 캐릭터의 정지와 계정 전체 정지(최근순) */
export async function recentHoldsOfAccount(db: Queryable, accountId: number, characterId: number, limit: number): Promise<{ uuid: string; state: string; kind: string; created_at: Date }[]> {
  const r = await db.query<{ uuid: string; state: string; kind: string; created_at: Date }>(
    'SELECT uuid, state, kind, created_at FROM economy_holds WHERE account_id = $1 AND (character_id IS NULL OR character_id = $2) ORDER BY created_at DESC, id DESC LIMIT $3',
    [accountId, characterId, limit],
  );
  return r.rows;
}
