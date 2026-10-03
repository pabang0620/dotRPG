import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export async function lockAccount(db: Queryable, uuid: string): Promise<{ id: number; uuid: string } | null> {
  const r = await db.query<{ id: string; uuid: string }>('SELECT id, uuid FROM accounts WHERE uuid = $1 FOR UPDATE', [uuid]);
  return r.rows[0] ? { id: Number(r.rows[0].id), uuid: r.rows[0].uuid } : null;
}

export interface ReportRow {
  id: number;
  uuid: string;
  state: 'open' | 'reviewing' | 'actioned' | 'dismissed';
  target_account_id: number;
  reason: string;
}

export async function lockReport(db: Queryable, uuid: string): Promise<ReportRow | null> {
  const r = await db.query<{ id: string; uuid: string; state: ReportRow['state']; target_account_id: string; reason: string }>(
    'SELECT id, uuid, state, target_account_id, reason FROM reports WHERE uuid = $1 FOR UPDATE',
    [uuid],
  );
  const x = r.rows[0];
  return x ? { ...x, id: Number(x.id), target_account_id: Number(x.target_account_id) } : null;
}

/** 같은 종류가 더 긴(또는 같은) 기간으로 이미 활성인가(warning은 겹침 검사가 없다) */
export async function hasLongerActive(db: Queryable, accountId: number, kind: string, endsAt: Date): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM account_sanctions WHERE account_id = $1 AND kind = $2 AND revoked_at IS NULL AND ends_at IS NOT NULL
        AND ends_at > now() AND ends_at >= $3 LIMIT 1`,
    [accountId, kind, endsAt],
  );
  return r.rows.length > 0;
}

export async function closeReport(client: PoolClient, reportId: number, state: 'actioned' | 'dismissed', by: string, note: string | null): Promise<void> {
  await client.query(
    `UPDATE reports SET state = $2, handled_at = now(), handled_by = $3, note = COALESCE($4, note) WHERE id = $1`,
    [reportId, state, by, note],
  );
}

export interface SanctionRowFull {
  id: number;
  uuid: string;
  account_id: number;
  kind: string;
  revoked_at: Date | null;
  note: string | null;
}

export async function lockSanction(db: Queryable, uuid: string): Promise<SanctionRowFull | null> {
  const r = await db.query<{ id: string; uuid: string; account_id: string; kind: string; revoked_at: Date | null; note: string | null }>(
    'SELECT id, uuid, account_id, kind, revoked_at, note FROM account_sanctions WHERE uuid = $1',
    [uuid],
  );
  const x = r.rows[0];
  return x ? { ...x, id: Number(x.id), account_id: Number(x.account_id) } : null;
}

export async function revoke(db: Queryable, id: number, by: string, note: string): Promise<void> {
  await db.query(
    `UPDATE account_sanctions SET revoked_at = now(), revoked_by = $2,
            note = left(coalesce(note || ' / ', '') || '해제: ' || $3, 500) WHERE id = $1`,
    [id, by, note],
  );
}
