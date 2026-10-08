// 탈퇴 SQL(account_withdrawals 중심). 서비스가 락 순서(캐릭터 -> 계정 -> 요청 행)를 지킨다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export type WithdrawalState = 'requested' | 'cancelled' | 'completed';
export type DeferReason = 'sanction' | 'economy_hold' | 'open_report' | 'payment_open' | 'manual';

export interface WithdrawalRow {
  id: number;
  uuid: string;
  account_id: number;
  state: WithdrawalState;
  source: 'self' | 'admin';
  requested_by_admin_id: number | null;
  request_id: string;
  requested_at: Date;
  due_at: Date;
  cancel_allowed: boolean;
  ack_paid_loss: boolean;
  loss_snapshot: Record<string, unknown>;
  saved_character_names: Record<string, string> | null;
  defer_reasons: DeferReason[];
  defer_checked_at: Date | null;
  manual_hold: boolean;
  manual_hold_note: string | null;
  cancelled_at: Date | null;
  cancelled_via: 'self' | 'admin' | null;
  cancel_request_id: string | null;
  anonymized_at: Date | null;
  retain_until: Date | null;
}

interface Raw extends Omit<WithdrawalRow, 'id' | 'account_id' | 'requested_by_admin_id'> {
  id: string;
  account_id: string;
  requested_by_admin_id: string | null;
}
const COLS = `id, uuid, account_id, state, source, requested_by_admin_id, request_id, requested_at, due_at, cancel_allowed, ack_paid_loss,
  loss_snapshot, saved_character_names, defer_reasons, defer_checked_at, manual_hold, manual_hold_note, cancelled_at, cancelled_via,
  cancel_request_id, anonymized_at, retain_until`;
const to = (r: Raw): WithdrawalRow => ({
  ...r,
  id: Number(r.id),
  account_id: Number(r.account_id),
  requested_by_admin_id: r.requested_by_admin_id === null ? null : Number(r.requested_by_admin_id),
});

export interface LockedAccount {
  id: number;
  uuid: string;
  created_at: Date;
  banned_until: Date | null;
  deleted_at: Date | null;
  anonymized_at: Date | null;
}

// ---------- 잠금 (순서: 캐릭터 -> 계정 -> 요청 행) ----------

/** 살아 있는 캐릭터를 id 오름차순으로 잠근다 */
export async function lockAliveCharacters(client: PoolClient, accountId: number): Promise<{ id: number; uuid: string; name: string }[]> {
  const r = await client.query<{ id: string; uuid: string; name: string }>(
    'SELECT id, uuid, name FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY id FOR UPDATE',
    [accountId],
  );
  return r.rows.map((x) => ({ id: Number(x.id), uuid: x.uuid, name: x.name }));
}

/** 삭제된 것까지 모든 캐릭터를 id 오름차순으로 잠근다(익명화 작업) */
export async function lockAllCharacters(client: PoolClient, accountId: number): Promise<{ id: number; uuid: string; name: string; deleted_at: Date | null }[]> {
  const r = await client.query<{ id: string; uuid: string; name: string; deleted_at: Date | null }>(
    'SELECT id, uuid, name, deleted_at FROM characters WHERE account_id = $1 ORDER BY id FOR UPDATE',
    [accountId],
  );
  return r.rows.map((x) => ({ id: Number(x.id), uuid: x.uuid, name: x.name, deleted_at: x.deleted_at }));
}

export async function lockAccount(client: PoolClient, accountId: number): Promise<LockedAccount | null> {
  const r = await client.query<Omit<LockedAccount, 'id'> & { id: string }>(
    'SELECT id, uuid, created_at, banned_until, deleted_at, anonymized_at FROM accounts WHERE id = $1 FOR UPDATE',
    [accountId],
  );
  return r.rows[0] ? { ...r.rows[0], id: Number(r.rows[0].id) } : null;
}

export async function accountIdByUuid(db: Queryable, uuid: string): Promise<number | null> {
  const r = await db.query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [uuid]);
  return r.rows[0] ? Number(r.rows[0].id) : null;
}

// ---------- 요청 행 ----------

export async function lockOpen(client: PoolClient, accountId: number): Promise<WithdrawalRow | null> {
  const r = await client.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE account_id = $1 AND state = 'requested' FOR UPDATE`, [accountId]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function openOf(db: Queryable, accountId: number): Promise<WithdrawalRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE account_id = $1 AND state = 'requested'`, [accountId]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function lockById(client: PoolClient, id: number): Promise<WithdrawalRow | null> {
  const r = await client.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE id = $1 FOR UPDATE`, [id]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function byUuid(db: Queryable, uuid: string): Promise<WithdrawalRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function byRequestId(db: Queryable, accountId: number, requestId: string): Promise<WithdrawalRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE account_id = $1 AND request_id = $2`, [accountId, requestId]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

/** 철회 멱등: 이 request_id 로 이미 철회된 요청 */
export async function byCancelRequestId(db: Queryable, accountId: number, requestId: string): Promise<WithdrawalRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE account_id = $1 AND cancel_request_id = $2`, [accountId, requestId]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function historyOf(db: Queryable, accountId: number, limit = 20): Promise<WithdrawalRow[]> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM account_withdrawals WHERE account_id = $1 ORDER BY id DESC LIMIT $2`, [accountId, limit]);
  return r.rows.map(to);
}

export async function countRequestedSince(db: Queryable, accountId: number, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM account_withdrawals WHERE account_id = $1 AND requested_at > $2', [accountId, since]);
  return Number((r.rows[0] as { n: string }).n);
}

export interface NewWithdrawal {
  accountId: number;
  source: 'self' | 'admin';
  adminId: number | null;
  requestId: string;
  requestedAt: Date;
  dueAt: Date;
  cancelAllowed: boolean;
  ackPaidLoss: boolean;
  lossSnapshot: Record<string, unknown>;
  savedNames: Record<string, string>;
}

export async function insert(client: PoolClient, n: NewWithdrawal): Promise<WithdrawalRow> {
  const r = await client.query<Raw>(
    `INSERT INTO account_withdrawals (account_id, source, requested_by_admin_id, request_id, requested_at, due_at, cancel_allowed,
                                      ack_paid_loss, loss_snapshot, saved_character_names)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9::jsonb, $10::jsonb) RETURNING ${COLS}`,
    [n.accountId, n.source, n.adminId, n.requestId, n.requestedAt, n.dueAt, n.cancelAllowed, n.ackPaidLoss, JSON.stringify(n.lossSnapshot), JSON.stringify(n.savedNames)],
  );
  return to(r.rows[0] as Raw);
}

export async function markCancelled(client: PoolClient, id: number, via: 'self' | 'admin', cancelRequestId: string, now: Date): Promise<void> {
  await client.query(
    `UPDATE account_withdrawals SET state = 'cancelled', cancelled_at = $2, cancelled_via = $3, cancel_request_id = $4,
            saved_character_names = NULL, defer_reasons = '{}' WHERE id = $1`,
    [id, now, via, cancelRequestId],
  );
}

export async function setDefer(client: Queryable, id: number, reasons: DeferReason[], now: Date): Promise<void> {
  await client.query('UPDATE account_withdrawals SET defer_reasons = $2::text[], defer_checked_at = $3 WHERE id = $1', [id, reasons, now]);
}

export async function setManualHold(client: Queryable, id: number, on: boolean): Promise<void> {
  await client.query(
    `UPDATE account_withdrawals SET manual_hold = $2,
            defer_reasons = CASE WHEN $2 THEN (SELECT array_agg(DISTINCT x) FROM unnest(array_append(defer_reasons, 'manual')) x)
                                 ELSE array_remove(defer_reasons, 'manual') END
      WHERE id = $1`,
    [id, on],
  );
}

export async function markCompleted(client: Queryable, id: number, anonymizedAt: Date, retainUntil: Date): Promise<void> {
  await client.query(
    `UPDATE account_withdrawals SET state = 'completed', anonymized_at = $2, retain_until = $3, saved_character_names = NULL, cancel_allowed = false
      WHERE id = $1`,
    [id, anonymizedAt, retainUntil],
  );
}

// ---------- 계정 쓰기 (T0) ----------

export async function blockLogin(client: Queryable, accountId: number, now: Date): Promise<void> {
  await client.query(
    `UPDATE accounts SET deleted_at = $2, active_family_id = NULL, active_install_id = NULL, active_device_hash = NULL, active_session_at = NULL
      WHERE id = $1`,
    [accountId, now],
  );
}

export async function restoreLogin(client: Queryable, accountId: number): Promise<void> {
  await client.query('UPDATE accounts SET deleted_at = NULL WHERE id = $1', [accountId]);
}

export async function endSessions(client: Queryable, accountId: number): Promise<void> {
  await client.query("UPDATE online_sessions SET ended_at = now(), end_reason = 'withdrawal' WHERE account_id = $1 AND ended_at IS NULL", [accountId]);
}

export async function renameCharacter(client: Queryable, characterId: number, name: string): Promise<void> {
  await client.query('UPDATE characters SET name = $2 WHERE id = $1', [characterId, name]);
}

// ---------- 읽기 (W1, 검사) ----------

export async function steamSubjectOf(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ subject: string }>("SELECT subject FROM auth_identities WHERE account_id = $1 AND provider = 'steam'", [accountId]);
  return r.rows[0]?.subject ?? null;
}

export async function devIdentityOf(db: Queryable, accountId: number): Promise<{ subject: string; secret_hash: string } | null> {
  const r = await db.query<{ subject: string; secret_hash: string }>("SELECT subject, secret_hash FROM auth_identities WHERE account_id = $1 AND provider = 'dev'", [accountId]);
  return r.rows[0] ?? null;
}

export async function accountByIdentity(db: Queryable, provider: 'steam' | 'dev', subject: string): Promise<{ id: number; secret_hash: string | null } | null> {
  const r = await db.query<{ account_id: string; secret_hash: string | null }>(
    'SELECT account_id, secret_hash FROM auth_identities WHERE provider = $1 AND subject = $2',
    [provider, subject],
  );
  return r.rows[0] ? { id: Number(r.rows[0].account_id), secret_hash: r.rows[0].secret_hash } : null;
}

export async function hasOpenOrder(db: Queryable, accountId: number): Promise<boolean> {
  const r = await db.query("SELECT 1 FROM star_orders WHERE account_id = $1 AND state IN ('pending_init', 'created', 'authorized', 'finalized') LIMIT 1", [accountId]);
  return r.rows.length > 0;
}

export interface LossSummary {
  characters: { id: string; name: string; class: string; level: number }[];
  gold_total: number;
  paid_stars: number;
  free_stars: number;
  star_debt: number;
  unclaimed_mails: number;
  active_listings: number;
  top_bids: number;
}

export async function lossSummary(db: Queryable, accountId: number): Promise<LossSummary> {
  const chars = await db.query<{ uuid: string; name: string; class: string; level: number; gold: string }>(
    'SELECT uuid, name, class, level, gold FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY id',
    [accountId],
  );
  const wallet = await db.query<{ balance: string; paid_balance: string; debt: string }>('SELECT balance, paid_balance, debt FROM star_wallets WHERE account_id = $1', [accountId]);
  const mails = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM mails m JOIN characters c ON c.id = m.character_id
      WHERE c.account_id = $1 AND m.claimed_at IS NULL AND m.expired_at IS NULL AND m.expires_at > now()`,
    [accountId],
  );
  const listings = await db.query<{ n: string }>("SELECT count(*) AS n FROM auction_listings WHERE seller_account_id = $1 AND status = 'active'", [accountId]);
  const bids = await db.query<{ n: string }>("SELECT count(*) AS n FROM auction_listings WHERE current_bidder_account_id = $1 AND status = 'active'", [accountId]);
  const w = wallet.rows[0];
  const balance = w ? Number(w.balance) : 0;
  const paid = w ? Number(w.paid_balance) : 0;
  return {
    characters: chars.rows.map((c) => ({ id: c.uuid, name: c.name, class: c.class, level: c.level })),
    gold_total: chars.rows.reduce((a, c) => a + Number(c.gold), 0),
    paid_stars: paid,
    free_stars: balance - paid,
    star_debt: w ? Number(w.debt) : 0,
    unclaimed_mails: Number((mails.rows[0] as { n: string }).n),
    active_listings: Number((listings.rows[0] as { n: string }).n),
    top_bids: Number((bids.rows[0] as { n: string }).n),
  };
}
