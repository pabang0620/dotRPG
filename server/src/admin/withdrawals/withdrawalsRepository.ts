// 관리자 WD1~WD8 SQL. 외부에는 uuid 만 내보낸다.
import type { Queryable } from '../../db/pool';

export interface ListRow {
  id: string;
  uuid: string;
  account_uuid: string;
  state: string;
  source: string;
  requested_at: Date;
  due_at: Date;
  defer_reasons: string[];
  defer_checked_at: Date | null;
  manual_hold: boolean;
  anonymized_at: Date | null;
}

export async function list(
  db: Queryable,
  q: { state?: string | undefined; deferred?: boolean | undefined; dueBefore?: Date | undefined; cursor?: string | undefined; limit: number },
): Promise<ListRow[]> {
  const r = await db.query<ListRow>(
    `SELECT w.id, w.uuid, a.uuid AS account_uuid, w.state, w.source, w.requested_at, w.due_at, w.defer_reasons, w.defer_checked_at, w.manual_hold, w.anonymized_at
       FROM account_withdrawals w JOIN accounts a ON a.id = w.account_id
      WHERE ($1::text IS NULL OR w.state = $1)
        AND ($2::boolean IS NULL OR (cardinality(w.defer_reasons) > 0) = $2)
        AND ($3::timestamptz IS NULL OR w.due_at < $3)
        AND ($4::uuid IS NULL OR w.id < (SELECT c.id FROM account_withdrawals c WHERE c.uuid = $4))
      ORDER BY w.id DESC LIMIT $5`,
    [q.state ?? null, q.deferred ?? null, q.dueBefore ?? null, q.cursor ?? null, q.limit + 1],
  );
  return r.rows;
}

export async function evidenceCounts(db: Queryable, accountId: number, now: Date): Promise<Record<string, number>> {
  const one = async (sql: string, params: unknown[]): Promise<number> => Number(((await db.query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);
  return {
    active_sanctions: await one("SELECT count(*) AS n FROM account_sanctions WHERE account_id = $1 AND revoked_at IS NULL AND (ends_at IS NULL OR ends_at > $2) AND kind IN ('ban', 'chat_mute')", [accountId, now]),
    active_economy_holds: await one("SELECT count(*) AS n FROM economy_holds WHERE account_id = $1 AND state IN ('active', 'clawed_back')", [accountId]),
    open_reports_against: await one("SELECT count(*) AS n FROM reports WHERE target_account_id = $1 AND state IN ('open', 'reviewing')", [accountId]),
    open_orders: await one("SELECT count(*) AS n FROM star_orders WHERE account_id = $1 AND state IN ('pending_init', 'created', 'authorized', 'finalized')", [accountId]),
    open_payment_flags: await one("SELECT count(*) AS n FROM payment_flags WHERE account_id = $1 AND state = 'open' AND severity >= 2", [accountId]),
  };
}

export async function accountBrief(db: Queryable, uuid: string): Promise<{ id: number; uuid: string; deleted_at: Date | null; anonymized_at: Date | null } | null> {
  const r = await db.query<{ id: string; uuid: string; deleted_at: Date | null; anonymized_at: Date | null }>('SELECT id, uuid, deleted_at, anonymized_at FROM accounts WHERE uuid = $1', [uuid]);
  return r.rows[0] ? { ...r.rows[0], id: Number(r.rows[0].id) } : null;
}

export async function accountUuidOf(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1', [accountId]);
  return r.rows[0]?.uuid ?? null;
}

/** 운영 메모는 계정 메모(admin_account_notes)에 남긴다. 감사 params 에는 길이만 넣는다 */
export async function addNote(db: Queryable, accountId: number, adminId: number, text: string): Promise<void> {
  await db.query("INSERT INTO admin_account_notes (account_id, admin_id, kind, note) VALUES ($1, $2, 'note', $3)", [accountId, adminId, text.slice(0, 500)]);
}

export async function setHoldNote(db: Queryable, id: number, note: string | null): Promise<void> {
  await db.query('UPDATE account_withdrawals SET manual_hold_note = $2 WHERE id = $1', [id, note]);
}
