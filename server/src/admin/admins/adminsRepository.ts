import type { Queryable } from '../../db/pool';
import { ADMIN_COLS, toAdmin, type AdminRow } from '../auth/adminAuthRepository';
import type { AdminRole } from '../common/adminTypes';

export async function listAdmins(db: Queryable): Promise<AdminRow[]> {
  const r = await db.query(`SELECT ${ADMIN_COLS} FROM admin_users ORDER BY id`);
  return r.rows.map((x) => toAdmin(x as never));
}

export async function findByUuid(db: Queryable, uuid: string, forUpdate = false): Promise<AdminRow | null> {
  const r = await db.query(`SELECT ${ADMIN_COLS} FROM admin_users WHERE uuid = $1${forUpdate ? ' FOR UPDATE' : ''}`, [uuid]);
  return r.rows[0] ? toAdmin(r.rows[0] as never) : null;
}

export async function insertAdmin(db: Queryable, a: { loginId: string; displayName: string; role: AdminRole; passwordHash: string; createdBy: number | null }): Promise<AdminRow> {
  const r = await db.query(
    `INSERT INTO admin_users (login_id, display_name, role, password_hash, must_change_password, created_by)
     VALUES ($1, $2, $3, $4, true, $5) RETURNING ${ADMIN_COLS}`,
    [a.loginId, a.displayName, a.role, a.passwordHash, a.createdBy],
  );
  return toAdmin(r.rows[0] as never);
}

export async function setRole(db: Queryable, id: number, role: AdminRole): Promise<void> {
  await db.query('UPDATE admin_users SET role = $2 WHERE id = $1', [id, role]);
}

export async function setDisabled(db: Queryable, id: number, at: Date | null): Promise<void> {
  await db.query('UPDATE admin_users SET disabled_at = $2 WHERE id = $1', [id, at]);
}

export async function resetTotp(db: Queryable, id: number): Promise<void> {
  await db.query('UPDATE admin_users SET totp_secret_enc = NULL, totp_confirmed_at = NULL, totp_last_step = NULL WHERE id = $1', [id]);
}

export async function unlock(db: Queryable, id: number): Promise<void> {
  await db.query('UPDATE admin_users SET failed_count = 0, locked_until = NULL WHERE id = $1', [id]);
}

/** 활성 owner 수(마지막 owner 보호) */
export async function activeOwnerCount(db: Queryable): Promise<number> {
  const r = await db.query<{ n: string }>("SELECT count(*) AS n FROM admin_users WHERE role = 'owner' AND disabled_at IS NULL");
  return Number((r.rows[0] as { n: string }).n);
}

export interface AuditFilter {
  adminId: number | null;
  targetUuid: string | null;
  action: string | null;
  result: string | null;
  since: string | null;
  until: string | null;
  before: number | null;
  limit: number;
}

export async function queryAudit(db: Queryable, f: AuditFilter) {
  const r = await db.query<{
    id: string; uuid: string; admin_uuid: string | null; login_id: string | null; login_id_tried: string | null; action: string; target_type: string | null;
    target_uuid: string | null; result: string; error_code: string | null; params: unknown; ip: string | null; created_at: Date;
  }>(
    `SELECT l.id, l.uuid, u.uuid AS admin_uuid, u.login_id, l.login_id_tried, l.action, l.target_type, l.target_uuid, l.result,
            l.error_code, l.params, l.ip, l.created_at
       FROM admin_audit_log l LEFT JOIN admin_users u ON u.id = l.admin_id
      WHERE ($1::bigint IS NULL OR l.admin_id = $1)
        AND ($2::uuid IS NULL OR l.target_uuid = $2)
        AND ($3::text IS NULL OR l.action = $3 OR l.action LIKE $3 || '.%')
        AND ($4::text IS NULL OR l.result = $4)
        AND ($5::timestamptz IS NULL OR l.created_at >= $5)
        AND ($6::timestamptz IS NULL OR l.created_at < $6)
        AND ($7::bigint IS NULL OR l.id < $7)
      ORDER BY l.id DESC LIMIT $8`,
    [f.adminId, f.targetUuid, f.action, f.result, f.since, f.until, f.before, f.limit + 1],
  );
  return r.rows;
}
