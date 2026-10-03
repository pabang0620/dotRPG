import type { Queryable } from '../../db/pool';

export interface DayWindow {
  start: Date;
  end: Date;
}

export async function goldByReason(db: Queryable, w: DayWindow) {
  const r = await db.query<{ reason: string; inflow: string; outflow: string; n: string }>(
    `SELECT reason, coalesce(sum(delta) FILTER (WHERE delta > 0), 0) AS inflow, coalesce(-sum(delta) FILTER (WHERE delta < 0), 0) AS outflow, count(*) AS n
       FROM gold_ledger WHERE created_at >= $1 AND created_at < $2 GROUP BY reason ORDER BY reason`,
    [w.start, w.end],
  );
  return r.rows;
}

export async function xpByReason(db: Queryable, w: DayWindow) {
  const r = await db.query<{ reason: string; total: string; n: string }>(
    `SELECT reason, sum(delta) AS total, count(*) AS n FROM xp_ledger WHERE created_at >= $1 AND created_at < $2 GROUP BY reason ORDER BY reason`,
    [w.start, w.end],
  );
  return r.rows;
}

export async function itemByReason(db: Queryable, w: DayWindow) {
  const r = await db.query<{ reason: string; plus: string; minus: string; n: string }>(
    `SELECT reason, coalesce(sum(delta) FILTER (WHERE delta > 0), 0) AS plus, coalesce(-sum(delta) FILTER (WHERE delta < 0), 0) AS minus, count(*) AS n
       FROM item_ledger WHERE created_at >= $1 AND created_at < $2 GROUP BY reason ORDER BY count(*) DESC, reason LIMIT 10`,
    [w.start, w.end],
  );
  return r.rows;
}

export async function auctionDay(db: Queryable, w: DayWindow) {
  const trades = await db.query<{ n: string; gold: string; fee: string }>(
    `SELECT count(*) AS n, coalesce(sum(price), 0) AS gold, coalesce(sum(fee), 0) AS fee FROM auction_trades WHERE traded_at >= $1 AND traded_at < $2`,
    [w.start, w.end],
  );
  const sinks = await db.query<{ kind: string; amount: string }>(
    `SELECT kind, sum(amount) AS amount FROM auction_sinks WHERE created_at >= $1 AND created_at < $2 GROUP BY kind ORDER BY kind`,
    [w.start, w.end],
  );
  const pair = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM auction_flags WHERE kind = 'pair_limit' AND created_at >= $1 AND created_at < $2`,
    [w.start, w.end],
  );
  return { trades: trades.rows[0] as { n: string; gold: string; fee: string }, sinks: sinks.rows, pairLimit: Number((pair.rows[0] as { n: string }).n) };
}

export async function accountsDay(db: Queryable, w: DayWindow) {
  const r = await db.query<{ created: string; active: string }>(
    `SELECT count(*) FILTER (WHERE created_at >= $1 AND created_at < $2) AS created,
            count(*) FILTER (WHERE last_login_at >= $1 AND last_login_at < $2) AS active FROM accounts`,
    [w.start, w.end],
  );
  return r.rows[0] as { created: string; active: string };
}

/** 직전 7일(하루 단위)의 골드 유입 합. d = 0..6 (0이 가장 오래된 날) */
export async function goldInflowPrev7(db: Queryable, start: Date): Promise<number[]> {
  const r = await db.query<{ d: number; s: string }>(
    `SELECT floor(extract(epoch FROM ($1::timestamptz - created_at)) / 86400)::int AS d, sum(delta) AS s
       FROM gold_ledger WHERE delta > 0 AND created_at >= $1::timestamptz - interval '7 days' AND created_at < $1 GROUP BY 1`,
    [start],
  );
  const out = Array<number>(7).fill(0);
  for (const row of r.rows) if (row.d >= 0 && row.d < 7) out[row.d] = Number(row.s);
  return out;
}

export async function lockAdmin(db: Queryable, id: number): Promise<void> {
  await db.query('SELECT 1 FROM admin_users WHERE id = $1 FOR UPDATE', [id]);
}

export async function lockCharacterByUuid(db: Queryable, uuid: string): Promise<{ id: number; uuid: string; name: string } | null> {
  const r = await db.query<{ id: string; uuid: string; name: string }>(
    'SELECT id, uuid, name FROM characters WHERE uuid = $1 AND deleted_at IS NULL FOR UPDATE',
    [uuid],
  );
  return r.rows[0] ? { id: Number(r.rows[0].id), uuid: r.rows[0].uuid, name: r.rows[0].name } : null;
}

export async function grantedGoldSince(db: Queryable, adminId: number, since: Date): Promise<number> {
  const r = await db.query<{ s: string }>('SELECT coalesce(sum(gold), 0) AS s FROM admin_grants WHERE admin_id = $1 AND created_at >= $2', [adminId, since]);
  return Number((r.rows[0] as { s: string }).s);
}

export async function insertGrant(
  db: Queryable,
  g: { adminId: number; characterId: number; mailId: number; requestId: string; systemCode: string; gold: number; itemKey: string | null; count: number | null; memo: string },
): Promise<{ uuid: string }> {
  const r = await db.query<{ uuid: string }>(
    `INSERT INTO admin_grants (admin_id, character_id, mail_id, request_id, system_code, gold, item_key, count, memo)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9) RETURNING uuid`,
    [g.adminId, g.characterId, g.mailId, g.requestId, g.systemCode, g.gold, g.itemKey, g.count, g.memo],
  );
  return r.rows[0] as { uuid: string };
}

export async function listGrants(db: Queryable, characterId: number | null, before: number | null, limit: number) {
  const r = await db.query<{
    id: string; uuid: string; admin: string; character_uuid: string; character_name: string; system_code: string; gold: string;
    item_key: string | null; count: number | null; memo: string; created_at: Date; claimed_at: Date | null; expired_at: Date | null;
  }>(
    `SELECT g.id, g.uuid, u.login_id AS admin, c.uuid AS character_uuid, c.name AS character_name, g.system_code, g.gold,
            g.item_key, g.count, g.memo, g.created_at, m.claimed_at, m.expired_at
       FROM admin_grants g JOIN admin_users u ON u.id = g.admin_id JOIN characters c ON c.id = g.character_id JOIN mails m ON m.id = g.mail_id
      WHERE ($1::bigint IS NULL OR g.character_id = $1) AND ($2::bigint IS NULL OR g.id < $2)
      ORDER BY g.id DESC LIMIT $3`,
    [characterId, before, limit + 1],
  );
  return r.rows;
}

export async function characterIdByUuid(db: Queryable, uuid: string): Promise<number | null> {
  const r = await db.query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [uuid]);
  return r.rows[0] ? Number(r.rows[0].id) : null;
}
