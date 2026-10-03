// 관리자 계정·세션 SQL. 게임 계정(accounts)과 완전히 분리된 표(admin_users, admin_sessions)만 다룬다.
import type { Queryable } from '../../db/pool';
import type { AdminRole, AdminScope } from '../common/adminTypes';

export interface AdminRow {
  id: number;
  uuid: string;
  loginId: string;
  displayName: string;
  role: AdminRole;
  passwordHash: string;
  mustChangePassword: boolean;
  totpSecretEnc: Buffer | null;
  totpConfirmedAt: Date | null;
  totpLastStep: number | null;
  failedCount: number;
  lockedUntil: Date | null;
  disabledAt: Date | null;
  lastLoginAt: Date | null;
  createdAt: Date;
}

interface RawAdmin {
  id: string;
  uuid: string;
  login_id: string;
  display_name: string;
  role: AdminRole;
  password_hash: string;
  must_change_password: boolean;
  totp_secret_enc: Buffer | null;
  totp_confirmed_at: Date | null;
  totp_last_step: string | null;
  failed_count: number;
  locked_until: Date | null;
  disabled_at: Date | null;
  last_login_at: Date | null;
  created_at: Date;
}

export const ADMIN_COLS = `id, uuid, login_id, display_name, role, password_hash, must_change_password, totp_secret_enc,
  totp_confirmed_at, totp_last_step, failed_count, locked_until, disabled_at, last_login_at, created_at`;

export const toAdmin = (r: RawAdmin): AdminRow => ({
  id: Number(r.id),
  uuid: r.uuid,
  loginId: r.login_id,
  displayName: r.display_name,
  role: r.role,
  passwordHash: r.password_hash,
  mustChangePassword: r.must_change_password,
  totpSecretEnc: r.totp_secret_enc,
  totpConfirmedAt: r.totp_confirmed_at,
  totpLastStep: r.totp_last_step === null ? null : Number(r.totp_last_step),
  failedCount: r.failed_count,
  lockedUntil: r.locked_until,
  disabledAt: r.disabled_at,
  lastLoginAt: r.last_login_at,
  createdAt: r.created_at,
});

export async function findByLoginId(db: Queryable, loginId: string, forUpdate = false): Promise<AdminRow | null> {
  const r = await db.query<RawAdmin>(`SELECT ${ADMIN_COLS} FROM admin_users WHERE login_id = $1${forUpdate ? ' FOR UPDATE' : ''}`, [loginId]);
  return r.rows[0] ? toAdmin(r.rows[0]) : null;
}

export async function findById(db: Queryable, id: number, forUpdate = false): Promise<AdminRow | null> {
  const r = await db.query<RawAdmin>(`SELECT ${ADMIN_COLS} FROM admin_users WHERE id = $1${forUpdate ? ' FOR UPDATE' : ''}`, [id]);
  return r.rows[0] ? toAdmin(r.rows[0]) : null;
}

/** 연속 실패 +1. 한도에 닿으면 잠그고 횟수를 0으로 되돌린다. 잠금 시각을 돌려준다(없으면 null) */
export async function registerFailure(db: Queryable, id: number, max: number, lockUntil: Date): Promise<Date | null> {
  const r = await db.query<{ locked_until: Date | null }>(
    `UPDATE admin_users
        SET failed_count = CASE WHEN failed_count + 1 >= $2 THEN 0 ELSE failed_count + 1 END,
            locked_until = CASE WHEN failed_count + 1 >= $2 THEN $3::timestamptz ELSE locked_until END
      WHERE id = $1 RETURNING locked_until`,
    [id, max, lockUntil],
  );
  return r.rows[0]?.locked_until ?? null;
}

export async function markLoginOk(db: Queryable, id: number, at: Date, totpStep: number | null): Promise<void> {
  await db.query(
    `UPDATE admin_users SET failed_count = 0, locked_until = NULL, last_login_at = $2,
            totp_last_step = COALESCE($3::bigint, totp_last_step) WHERE id = $1`,
    [id, at, totpStep],
  );
}

export interface SessionRow {
  sessionId: number;
  scope: AdminScope;
  createdAt: Date;
  lastSeenAt: Date;
  expiresAt: Date;
  revokedAt: Date | null;
  admin: AdminRow;
}

export async function findSession(db: Queryable, tokenHash: string): Promise<SessionRow | null> {
  const r = await db.query<RawAdmin & { sid: string; scope: AdminScope; s_created: Date; s_seen: Date; s_expires: Date; s_revoked: Date | null }>(
    `SELECT s.id AS sid, s.scope, s.created_at AS s_created, s.last_seen_at AS s_seen, s.expires_at AS s_expires,
            s.revoked_at AS s_revoked, ${ADMIN_COLS.split(',').map((c) => `u.${c.trim()}`).join(', ')}
       FROM admin_sessions s JOIN admin_users u ON u.id = s.admin_id WHERE s.token_hash = $1`,
    [tokenHash],
  );
  const x = r.rows[0];
  if (!x) return null;
  return {
    sessionId: Number(x.sid),
    scope: x.scope,
    createdAt: x.s_created,
    lastSeenAt: x.s_seen,
    expiresAt: x.s_expires,
    revokedAt: x.s_revoked,
    admin: toAdmin(x),
  };
}

export async function touchSession(db: Queryable, sessionId: number, at: Date): Promise<void> {
  await db.query('UPDATE admin_sessions SET last_seen_at = $2 WHERE id = $1', [sessionId, at]);
}

export async function insertSession(
  db: Queryable,
  adminId: number,
  tokenHash: string,
  scope: AdminScope,
  createdAt: Date,
  expiresAt: Date,
  note: string | null,
): Promise<number> {
  const r = await db.query<{ id: string }>(
    `INSERT INTO admin_sessions (admin_id, token_hash, scope, created_at, last_seen_at, expires_at, client_note)
     VALUES ($1, $2, $3, $4, $4, $5, $6) RETURNING id`,
    [adminId, tokenHash, scope, createdAt, expiresAt, note],
  );
  return Number((r.rows[0] as { id: string }).id);
}

export async function revokeSession(db: Queryable, sessionId: number, at: Date): Promise<void> {
  await db.query('UPDATE admin_sessions SET revoked_at = $2 WHERE id = $1 AND revoked_at IS NULL', [sessionId, at]);
}

/** 그 관리자의 살아 있는 세션을 모두 폐기한다(exceptSessionId는 남긴다) */
export async function revokeAllSessions(db: Queryable, adminId: number, at: Date, exceptSessionId: number | null = null): Promise<void> {
  await db.query(
    `UPDATE admin_sessions SET revoked_at = $2 WHERE admin_id = $1 AND revoked_at IS NULL AND ($3::bigint IS NULL OR id <> $3)`,
    [adminId, at, exceptSessionId],
  );
}

export async function setPassword(db: Queryable, id: number, hash: string, mustChange: boolean, at: Date): Promise<void> {
  await db.query(
    `UPDATE admin_users SET password_hash = $2, must_change_password = $3, password_changed_at = $4 WHERE id = $1`,
    [id, hash, mustChange, at],
  );
}

export async function setTotpPending(db: Queryable, id: number, enc: Buffer): Promise<void> {
  await db.query(`UPDATE admin_users SET totp_secret_enc = $2, totp_confirmed_at = NULL, totp_last_step = NULL WHERE id = $1`, [id, enc]);
}

export async function confirmTotp(db: Queryable, id: number, step: number, at: Date): Promise<void> {
  await db.query(`UPDATE admin_users SET totp_confirmed_at = $3, totp_last_step = $2 WHERE id = $1`, [id, step, at]);
}
