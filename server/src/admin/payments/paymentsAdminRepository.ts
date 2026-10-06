// 결제 관리자 API의 SQL. 주문·플래그·운영 지급 조회와 운영 지급 행 쓰기(별조각 잔액·원장은 starWallet.ts 한 곳)
import type { Queryable } from '../../db/pool';

export const encodeCursor = (id: number): string => Buffer.from(String(id)).toString('base64url');
export function decodeCursor(c: string | undefined): number | null {
  if (!c) return null;
  const n = Number(Buffer.from(c, 'base64url').toString('utf8'));
  return Number.isInteger(n) && n > 0 ? n : null;
}

export interface AdminOrderRow {
  id: number;
  uuid: string;
  account_uuid: string;
  state: string;
  fail_reason: string | null;
  needs_review: boolean;
  review_reason: string | null;
  product_id: string;
  stars: number;
  currency: string;
  amount_minor: number;
  steam_order_id: string;
  steam_trans_id: string | null;
  steam_id: string;
  steam_status: string | null;
  steam_country: string | null;
  steam_amount_minor: number | null;
  steam_currency: string | null;
  tier: string;
  app_id: number;
  catalog_version: string;
  attempts: number;
  created_at: Date;
  expires_at: Date;
  init_at: Date | null;
  authorized_at: Date | null;
  finalized_at: Date | null;
  granted_at: Date | null;
  reversed_at: Date | null;
  closed_at: Date | null;
  next_check_at: Date | null;
  last_checked_at: Date | null;
}

type Raw = Omit<AdminOrderRow, 'id' | 'amount_minor' | 'steam_amount_minor' | 'app_id'> & { id: string; amount_minor: string; steam_amount_minor: string | null; app_id: string };

const COLS = `o.id, o.uuid, a.uuid AS account_uuid, o.state, o.fail_reason, o.needs_review, o.review_reason, o.product_id, o.stars, o.currency, o.amount_minor,
  o.steam_order_id::text AS steam_order_id, o.steam_trans_id::text AS steam_trans_id, o.steam_id, o.steam_status, o.steam_country, o.steam_amount_minor, o.steam_currency,
  o.tier, o.app_id, o.catalog_version, o.attempts, o.created_at, o.expires_at, o.init_at, o.authorized_at, o.finalized_at, o.granted_at, o.reversed_at, o.closed_at,
  o.next_check_at, o.last_checked_at`;

const toRow = (r: Raw): AdminOrderRow => ({ ...r, id: Number(r.id), amount_minor: Number(r.amount_minor), steam_amount_minor: r.steam_amount_minor === null ? null : Number(r.steam_amount_minor), app_id: Number(r.app_id) });

export async function listOrders(
  db: Queryable,
  f: { state?: string | undefined; account?: string | undefined; from?: string | undefined; to?: string | undefined; needsReview?: boolean | undefined; cursor: number | null; limit: number },
): Promise<AdminOrderRow[]> {
  const r = await db.query<Raw>(
    `SELECT ${COLS} FROM star_orders o JOIN accounts a ON a.id = o.account_id
      WHERE ($1::text IS NULL OR o.state = $1) AND ($2::uuid IS NULL OR a.uuid = $2) AND ($3::timestamptz IS NULL OR o.created_at >= $3)
        AND ($4::timestamptz IS NULL OR o.created_at < $4) AND ($5::boolean IS NULL OR o.needs_review = $5) AND ($6::bigint IS NULL OR o.id < $6)
      ORDER BY o.id DESC LIMIT $7`,
    [f.state ?? null, f.account ?? null, f.from ?? null, f.to ?? null, f.needsReview ?? null, f.cursor, f.limit + 1],
  );
  return r.rows.map(toRow);
}

export async function orderDetail(db: Queryable, uuid: string): Promise<AdminOrderRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM star_orders o JOIN accounts a ON a.id = o.account_id WHERE o.uuid = $1`, [uuid]);
  return r.rows[0] ? toRow(r.rows[0]) : null;
}

export async function allocsOfOrder(db: Queryable, orderId: number): Promise<{ ledger_id: number; reason: string; ref: string | null; stars: number; created_at: Date }[]> {
  const r = await db.query<{ ledger_id: string; reason: string; ref: string | null; stars: number; created_at: Date }>(
    `SELECT a.ledger_id, l.reason, l.ref, a.stars, l.created_at FROM star_spend_allocs a JOIN star_ledger l ON l.id = a.ledger_id WHERE a.order_id = $1 ORDER BY a.ledger_id`,
    [orderId],
  );
  return r.rows.map((x) => ({ ...x, ledger_id: Number(x.ledger_id) }));
}

export async function ledgerOfOrder(db: Queryable, orderUuid: string): Promise<{ reason: string; delta: number; paid_delta: number; debt_delta: number; created_at: Date }[]> {
  const r = await db.query<{ reason: string; delta: string; paid_delta: string; debt_delta: string; created_at: Date }>(
    'SELECT reason, delta, paid_delta, debt_delta, created_at FROM star_ledger WHERE ref = $1 AND reason IN (\'purchase\', \'refund_revoke\', \'chargeback_revoke\', \'debt_settle\') ORDER BY id',
    [orderUuid],
  );
  return r.rows.map((x) => ({ reason: x.reason, delta: Number(x.delta), paid_delta: Number(x.paid_delta), debt_delta: Number(x.debt_delta), created_at: x.created_at }));
}

export interface FlagRow {
  id: number;
  uuid: string;
  account_uuid: string;
  order_uuid: string | null;
  kind: string;
  severity: number;
  detail: Record<string, unknown>;
  state: string;
  reviewed_by: string | null;
  reviewed_at: Date | null;
  note: string | null;
  created_at: Date;
}

const FLAG_SELECT = `SELECT f.id, f.uuid, a.uuid AS account_uuid, o.uuid AS order_uuid, f.kind, f.severity, f.detail, f.state, f.reviewed_by, f.reviewed_at, f.note, f.created_at
  FROM payment_flags f JOIN accounts a ON a.id = f.account_id LEFT JOIN star_orders o ON o.id = f.order_id`;
type RawFlag = Omit<FlagRow, 'id'> & { id: string };
const toFlag = (r: RawFlag): FlagRow => ({ ...r, id: Number(r.id) });

export async function listFlags(db: Queryable, f: { state: string; account?: string | undefined; cursor: number | null; limit: number }): Promise<FlagRow[]> {
  const r = await db.query<RawFlag>(
    `${FLAG_SELECT} WHERE f.state = $1 AND ($2::uuid IS NULL OR a.uuid = $2) AND ($3::bigint IS NULL OR f.id < $3) ORDER BY f.severity DESC, f.id DESC LIMIT $4`,
    [f.state, f.account ?? null, f.cursor, f.limit + 1],
  );
  return r.rows.map(toFlag);
}

export async function flagsOfOrder(db: Queryable, orderId: number): Promise<FlagRow[]> {
  const r = await db.query<RawFlag>(`${FLAG_SELECT} WHERE f.order_id = $1 ORDER BY f.id`, [orderId]);
  return r.rows.map(toFlag);
}

export async function flagsOfAccount(db: Queryable, accountId: number, limit: number): Promise<FlagRow[]> {
  const r = await db.query<RawFlag>(`${FLAG_SELECT} WHERE f.account_id = $1 ORDER BY f.id DESC LIMIT $2`, [accountId, limit]);
  return r.rows.map(toFlag);
}

export async function resolveFlag(db: Queryable, uuid: string, resolution: string, by: string, note: string): Promise<{ uuid: string; account_uuid: string } | null> {
  const r = await db.query<{ uuid: string; account_uuid: string }>(
    `UPDATE payment_flags f SET state = $2, reviewed_by = $3, reviewed_at = now(), note = $4
      WHERE f.uuid = $1 AND f.state = 'open' RETURNING f.uuid, (SELECT uuid FROM accounts WHERE id = f.account_id) AS account_uuid`,
    [uuid, resolution, by, note],
  );
  return r.rows[0] ?? null;
}

export async function flagExists(db: Queryable, uuid: string): Promise<{ state: string } | null> {
  const r = await db.query<{ state: string }>('SELECT state FROM payment_flags WHERE uuid = $1', [uuid]);
  return r.rows[0] ?? null;
}

export async function activeOwnerCount(db: Queryable): Promise<number> {
  const r = await db.query<{ n: string }>("SELECT count(*) AS n FROM admin_users WHERE role = 'owner' AND disabled_at IS NULL");
  return Number(r.rows[0]?.n ?? 0);
}

// ---------------- 운영 지급(star_admin_grants) ----------------

export interface GrantRow {
  id: number;
  uuid: string;
  kind: 'grant' | 'debt_forgive';
  account_id: number;
  account_uuid: string;
  stars: number | null;
  related_order_uuid: string | null;
  memo: string;
  state: 'pending' | 'applied' | 'cancelled' | 'expired';
  created_by: number;
  created_by_name: string;
  approved_by_name: string | null;
  created_at: Date;
  approved_at: Date | null;
  expires_at: Date;
  closed_at: Date | null;
  applied_stars: number | null;
}

type RawGrant = Omit<GrantRow, 'id' | 'account_id' | 'stars' | 'created_by' | 'applied_stars'> & { id: string; account_id: string; stars: string | null; created_by: string; applied_stars: string | null };

const GRANT_SELECT = `SELECT g.id, g.uuid, g.kind, g.account_id, a.uuid AS account_uuid, g.stars, ro.uuid AS related_order_uuid, g.memo, g.state, g.created_by,
    cu.login_id AS created_by_name, au.login_id AS approved_by_name, g.created_at, g.approved_at, g.expires_at, g.closed_at, g.applied_stars
  FROM star_admin_grants g JOIN accounts a ON a.id = g.account_id JOIN admin_users cu ON cu.id = g.created_by
  LEFT JOIN admin_users au ON au.id = g.approved_by LEFT JOIN star_orders ro ON ro.id = g.related_order_id`;

const toGrant = (r: RawGrant): GrantRow => ({
  ...r,
  id: Number(r.id),
  account_id: Number(r.account_id),
  stars: r.stars === null ? null : Number(r.stars),
  created_by: Number(r.created_by),
  applied_stars: r.applied_stars === null ? null : Number(r.applied_stars),
});

export async function grantByUuid(db: Queryable, uuid: string, lock = false): Promise<GrantRow | null> {
  // FOR UPDATE는 조인 결과 전체를 잠그지 않도록 grant 행만(OF g)
  const r = await db.query<RawGrant>(`${GRANT_SELECT} WHERE g.uuid = $1${lock ? ' FOR NO KEY UPDATE OF g' : ''}`, [uuid]);
  return r.rows[0] ? toGrant(r.rows[0]) : null;
}

export async function listGrants(db: Queryable, f: { state?: string | undefined; account?: string | undefined; cursor: number | null; limit: number }): Promise<GrantRow[]> {
  const r = await db.query<RawGrant>(
    `${GRANT_SELECT} WHERE ($1::text IS NULL OR g.state = $1) AND ($2::uuid IS NULL OR a.uuid = $2) AND ($3::bigint IS NULL OR g.id < $3) ORDER BY g.id DESC LIMIT $4`,
    [f.state ?? null, f.account ?? null, f.cursor, f.limit + 1],
  );
  return r.rows.map(toGrant);
}

/** 오늘(since 이후) 별조각 지급 합계. createdBy 가 있으면 그 관리자 것만. state: 작성 때는 pending+applied, 승인 때는 applied */
export async function grantedStarsSince(db: Queryable, since: Date, o: { createdBy?: number; states: string[]; by: 'created' | 'approved' }): Promise<number> {
  const col = o.by === 'created' ? 'created_at' : 'approved_at';
  const r = await db.query<{ s: string }>(
    `SELECT coalesce(sum(stars), 0) AS s FROM star_admin_grants WHERE kind = 'grant' AND state = ANY($1::text[]) AND ${col} >= $2 AND ($3::bigint IS NULL OR created_by = $3)`,
    [o.states, since, o.createdBy ?? null],
  );
  return Number(r.rows[0]?.s ?? 0);
}

export async function insertGrant(
  db: Queryable,
  g: { kind: string; accountId: number; stars: number | null; relatedOrderId: number | null; memo: string; requestId: string; createdBy: number; expiresAt: Date },
): Promise<string> {
  const r = await db.query<{ uuid: string }>(
    `INSERT INTO star_admin_grants (kind, account_id, stars, related_order_id, memo, request_id, created_by, expires_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8) RETURNING uuid`,
    [g.kind, g.accountId, g.stars, g.relatedOrderId, g.memo, g.requestId, g.createdBy, g.expiresAt],
  );
  return (r.rows[0] as { uuid: string }).uuid;
}

export async function markGrantApplied(db: Queryable, id: number, approvedBy: number, appliedStars: number): Promise<void> {
  await db.query("UPDATE star_admin_grants SET state = 'applied', approved_by = $2, approved_at = now(), closed_at = now(), applied_stars = $3 WHERE id = $1 AND state = 'pending'", [id, approvedBy, appliedStars]);
}

export async function markGrantCancelled(db: Queryable, id: number): Promise<boolean> {
  const r = await db.query("UPDATE star_admin_grants SET state = 'cancelled', closed_at = now() WHERE id = $1 AND state = 'pending'", [id]);
  return (r.rowCount ?? 0) > 0;
}

// ---------------- 대사 현황(PA14) ----------------

export async function stateCounts(db: Queryable): Promise<Record<string, number>> {
  const r = await db.query<{ state: string; n: string }>('SELECT state, count(*) AS n FROM star_orders GROUP BY state');
  return Object.fromEntries(r.rows.map((x) => [x.state, Number(x.n)]));
}

export async function stuckOrders(db: Queryable): Promise<{ uuid: string; state: string; created_at: Date }[]> {
  const r = await db.query<{ uuid: string; state: string; created_at: Date }>(
    `SELECT uuid, state, created_at FROM star_orders
      WHERE (state = 'pending_init' AND created_at < now() - interval '10 minutes')
         OR (state IN ('created', 'authorized') AND expires_at < now() - interval '10 minutes')
         OR (state = 'finalized' AND finalized_at < now() - interval '5 minutes') ORDER BY id LIMIT 50`,
  );
  return r.rows;
}

export async function lastReportRun(db: Queryable): Promise<{ started_at: Date; detail: Record<string, unknown> } | null> {
  const r = await db.query<{ started_at: Date; detail: Record<string, unknown> }>("SELECT started_at, detail FROM job_runs WHERE job = 'payment-report' AND status = 'ok' ORDER BY started_at DESC LIMIT 1");
  return r.rows[0] ?? null;
}

export async function grantedSums(db: Queryable): Promise<{ currency: string; orders: number; amount: number }[]> {
  const r = await db.query<{ currency: string; n: string; s: string }>(
    "SELECT currency, count(*) AS n, coalesce(sum(amount_minor), 0) AS s FROM star_orders WHERE granted_at IS NOT NULL AND state = 'granted' GROUP BY currency ORDER BY currency",
  );
  return r.rows.map((x) => ({ currency: x.currency, orders: Number(x.n), amount: Number(x.s) }));
}

export async function recentOrdersOfAccount(db: Queryable, accountId: number, limit: number): Promise<AdminOrderRow[]> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM star_orders o JOIN accounts a ON a.id = o.account_id WHERE o.account_id = $1 ORDER BY o.id DESC LIMIT $2`, [accountId, limit]);
  return r.rows.map(toRow);
}
