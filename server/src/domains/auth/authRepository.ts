import type { PoolClient } from 'pg';
import { query, type Queryable } from '../../db/pool';

export interface AccountRow {
  id: number;
  uuid: string;
  created_at: Date;
  last_login_at: Date | null;
  banned_until: Date | null;
  deleted_at: Date | null;
  /** 9단계: 현재 유효한 세션(리프레시 가족). 한 번도 로그인하지 않은 옛 계정은 null */
  active_family_id?: string | null;
  /** 0039 게임시간 선택제(KST 분). 둘 다 null이면 제한 없음 */
  play_window_start?: number | null;
  play_window_end?: number | null;
}

export interface DevIdentityRow extends AccountRow {
  secret_hash: string;
}

export interface RefreshRow {
  id: number;
  account_id: number;
  family_id: string;
  used_at: Date | null;
  revoked_at: Date | null;
  expires_at: Date;
  revoke_reason: string | null;
  device_hash: string | null;
}

interface RawAccount extends Omit<AccountRow, 'id'> {
  id: string;
}
const toAccount = (r: RawAccount): AccountRow => ({ ...r, id: Number(r.id) });

export async function insertAccount(client: PoolClient): Promise<AccountRow> {
  const r = await client.query<RawAccount>(
    `INSERT INTO accounts DEFAULT VALUES
     RETURNING id, uuid, created_at, last_login_at, banned_until, deleted_at, play_window_start, play_window_end`,
  );
  return toAccount(r.rows[0] as RawAccount);
}

export async function insertDevIdentity(
  client: PoolClient,
  accountId: number,
  loginId: string,
  secretHash: string,
): Promise<void> {
  await client.query(
    `INSERT INTO auth_identities (account_id, provider, subject, secret_hash)
     VALUES ($1, 'dev', $2, $3)`,
    [accountId, loginId, secretHash],
  );
}

export async function findDevIdentity(loginId: string): Promise<DevIdentityRow | null> {
  const r = await query<RawAccount & { secret_hash: string }>(
    `SELECT a.id, a.uuid, a.created_at, a.last_login_at, a.banned_until, a.deleted_at, a.play_window_start, a.play_window_end, i.secret_hash
       FROM auth_identities i JOIN accounts a ON a.id = i.account_id
      WHERE i.provider = 'dev' AND i.subject = $1`,
    [loginId],
  );
  const row = r.rows[0];
  return row ? { ...toAccount(row), secret_hash: row.secret_hash } : null;
}

export async function touchLastLogin(client: PoolClient, accountId: number): Promise<Date> {
  const r = await client.query<{ last_login_at: Date }>(
    'UPDATE accounts SET prev_login_at = last_login_at, last_login_at = now() WHERE id = $1 RETURNING last_login_at', // 0022: keep the login before this one
    [accountId],
  );
  return (r.rows[0] as { last_login_at: Date }).last_login_at;
}

export async function insertRefreshToken(
  client: PoolClient,
  accountId: number,
  familyId: string,
  tokenHash: string,
  expiresAt: Date,
  installId: string | null = null,
  deviceHash: string | null = null,
): Promise<void> {
  await client.query(
    `INSERT INTO refresh_tokens (account_id, family_id, token_hash, expires_at, install_id, device_hash)
     VALUES ($1, $2, $3, $4, $5, $6)`,
    [accountId, familyId, tokenHash, expiresAt, installId, deviceHash],
  );
}

interface RawRefresh extends Omit<RefreshRow, 'id' | 'account_id'> {
  id: string;
  account_id: string;
}
const toRefresh = (r: RawRefresh): RefreshRow => ({
  ...r,
  id: Number(r.id),
  account_id: Number(r.account_id),
});

export async function findRefreshAccountId(tokenHash: string): Promise<number | null> {
  const r = await query<{ account_id: string }>(
    'SELECT account_id FROM refresh_tokens WHERE token_hash = $1',
    [tokenHash],
  );
  const row = r.rows[0];
  return row ? Number(row.account_id) : null;
}

export async function findRefreshForUpdate(
  client: PoolClient,
  tokenHash: string,
): Promise<RefreshRow | null> {
  const r = await client.query<RawRefresh>(
    `SELECT id, account_id, family_id, used_at, revoked_at, expires_at, revoke_reason, device_hash
       FROM refresh_tokens WHERE token_hash = $1 FOR UPDATE`,
    [tokenHash],
  );
  const row = r.rows[0];
  return row ? toRefresh(row) : null;
}

export async function markRefreshUsed(client: PoolClient, id: number): Promise<void> {
  await client.query('UPDATE refresh_tokens SET used_at = now() WHERE id = $1', [id]);
}

export type RevokeReason = 'logout' | 'reuse' | 'replaced' | 'device_mismatch' | 'admin' | 'withdrawal';

export async function revokeFamily(client: Queryable, familyId: string, reason: RevokeReason): Promise<void> {
  await client.query(
    'UPDATE refresh_tokens SET revoked_at = now(), revoke_reason = $2 WHERE family_id = $1 AND revoked_at IS NULL',
    [familyId, reason],
  );
}

/** 탈퇴 요청: 이 계정의 살아 있는 모든 갱신 토큰(가족 전체)을 폐기한다 */
export async function revokeAllForAccount(client: Queryable, accountId: number, reason: RevokeReason): Promise<number> {
  const r = await client.query('UPDATE refresh_tokens SET revoked_at = now(), revoke_reason = $2 WHERE account_id = $1 AND revoked_at IS NULL', [accountId, reason]);
  return r.rowCount ?? 0;
}

export async function findFamilyByHash(client: PoolClient, tokenHash: string): Promise<string | null> {
  const r = await client.query<{ family_id: string }>(
    'SELECT family_id FROM refresh_tokens WHERE token_hash = $1',
    [tokenHash],
  );
  return r.rows[0]?.family_id ?? null;
}

export async function findAccountById(client: PoolClient, id: number): Promise<AccountRow | null> {
  const r = await client.query<RawAccount>(
    'SELECT id, uuid, created_at, last_login_at, banned_until, deleted_at, active_family_id, play_window_start, play_window_end FROM accounts WHERE id = $1',
    [id],
  );
  const row = r.rows[0];
  return row ? toAccount(row) : null;
}

export interface MeRow {
  uuid: string;
  created_at: Date;
  provider: string;
  subject: string;
  character_count: number;
}

export async function findMe(accountId: number): Promise<MeRow | null> {
  const r = await query<{
    uuid: string;
    created_at: Date;
    provider: string;
    subject: string;
    character_count: string;
  }>(
    `SELECT a.uuid, a.created_at, i.provider, i.subject,
            (SELECT count(*) FROM characters c WHERE c.account_id = a.id AND c.deleted_at IS NULL) AS character_count
       FROM accounts a
       JOIN LATERAL (SELECT provider, subject FROM auth_identities WHERE account_id = a.id ORDER BY id LIMIT 1) i ON true
      WHERE a.id = $1`,
    [accountId],
  );
  const row = r.rows[0];
  return row ? { ...row, character_count: Number(row.character_count) } : null;
}

// ---------- Steam ----------

export async function findAccountBySteam(steamId: string): Promise<AccountRow | null> {
  const r = await query<RawAccount>(
    `SELECT a.id, a.uuid, a.created_at, a.last_login_at, a.banned_until, a.deleted_at, a.play_window_start, a.play_window_end
       FROM auth_identities i JOIN accounts a ON a.id = i.account_id
      WHERE i.provider = 'steam' AND i.subject = $1`,
    [steamId],
  );
  return r.rows[0] ? toAccount(r.rows[0]) : null;
}

export async function insertSteamIdentity(client: Queryable, accountId: number, steamId: string, ownerSteamId: string | null = null): Promise<void> {
  await client.query("INSERT INTO auth_identities (account_id, provider, subject, steam_owner_id) VALUES ($1, 'steam', $2, $3)", [accountId, steamId, ownerSteamId]);
}

/** 9단계: 패밀리 공유 로그인이면 앱 소유자의 SteamID를, 아니면 NULL을 기록한다(사람 키 = COALESCE(steam_owner_id, subject)) */
export async function setSteamOwner(client: Queryable, accountId: number, ownerSteamId: string | null): Promise<void> {
  await client.query("UPDATE auth_identities SET steam_owner_id = $2 WHERE account_id = $1 AND provider = 'steam' AND steam_owner_id IS DISTINCT FROM $2", [accountId, ownerSteamId]);
}

/** 한 번도 세션이 없던 옛 계정(active_family_id가 비어 있음)의 첫 리프레시가 그 가족을 현재 세션으로 삼는다 */
export async function adoptFamilyIfNone(client: Queryable, accountId: number, familyId: string): Promise<void> {
  await client.query('UPDATE accounts SET active_family_id = $2, active_session_at = now() WHERE id = $1 AND active_family_id IS NULL', [accountId, familyId]);
}

export async function hasSteam(accountId: number): Promise<boolean> {
  const r = await query("SELECT 1 FROM auth_identities WHERE account_id = $1 AND provider = 'steam'", [accountId]);
  return r.rows.length > 0;
}

/** 지금 효력이 있는 정지(ban)의 사유 코드. 로그인 거절 응답에 싣는다(운영정책: 통지에 사유 포함). 없으면 null */
export async function activeBanReason(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ reason_code: string }>(
    `SELECT reason_code FROM account_sanctions
      WHERE account_id = $1 AND kind = 'ban' AND revoked_at IS NULL AND (ends_at IS NULL OR ends_at > now())
      ORDER BY id DESC LIMIT 1`,
    [accountId],
  );
  return r.rows[0]?.reason_code ?? null;
}

// ---------- 카카오·Google (0045) ----------

export async function findAccountByIdentity(provider: 'kakao' | 'google', subject: string): Promise<AccountRow | null> {
  const r = await query<RawAccount>(
    `SELECT a.id, a.uuid, a.created_at, a.last_login_at, a.banned_until, a.deleted_at, a.play_window_start, a.play_window_end
       FROM auth_identities i JOIN accounts a ON a.id = i.account_id
      WHERE i.provider = $1 AND i.subject = $2`,
    [provider, subject],
  );
  return r.rows[0] ? toAccount(r.rows[0]) : null;
}

export async function insertOauthIdentity(client: Queryable, accountId: number, provider: 'kakao' | 'google', subject: string): Promise<void> {
  await client.query('INSERT INTO auth_identities (account_id, provider, subject) VALUES ($1, $2, $3)', [accountId, provider, subject]);
}

/** /me 의 연결된 로그인 수단 목록 */
export async function providersOf(accountId: number): Promise<string[]> {
  const r = await query<{ provider: string }>('SELECT provider FROM auth_identities WHERE account_id = $1 ORDER BY id', [accountId]);
  return r.rows.map((x) => x.provider);
}

export interface OauthSessionRow {
  id: number;
  provider: 'kakao' | 'google';
  mode: 'login' | 'link';
  account_id: number | null;
  status: 'pending' | 'ready' | 'error' | 'done';
  poll_secret_hash: string;
  subject: string | null;
  error_code: string | null;
  expires_at: Date;
}
type RawOauthSession = Omit<OauthSessionRow, 'id' | 'account_id'> & { id: string; account_id: string | null };
const toOauth = (r: RawOauthSession): OauthSessionRow => ({ ...r, id: Number(r.id), account_id: r.account_id === null ? null : Number(r.account_id) });
const OAUTH_COLS = 'id, provider, mode, account_id, status, poll_secret_hash, subject, error_code, expires_at';

export async function insertOauthSession(
  db: Queryable,
  s: { state: string; pollSecretHash: string; provider: 'kakao' | 'google'; mode: 'login' | 'link'; accountId: number | null; expiresAt: Date },
): Promise<void> {
  await db.query(
    `INSERT INTO oauth_sessions (state, poll_secret_hash, provider, mode, account_id, expires_at) VALUES ($1, $2, $3, $4, $5, $6)`,
    [s.state, s.pollSecretHash, s.provider, s.mode, s.accountId, s.expiresAt],
  );
}

export async function findOauthSession(db: Queryable, state: string, forUpdate = false): Promise<OauthSessionRow | null> {
  const r = await db.query<RawOauthSession>(`SELECT ${OAUTH_COLS} FROM oauth_sessions WHERE state = $1${forUpdate ? ' FOR UPDATE' : ''}`, [state]);
  return r.rows[0] ? toOauth(r.rows[0]) : null;
}

/** 콜백 결과 기록: pending 일 때만 바꾼다(같은 콜백이 두 번 와도 첫 결과가 남는다) */
export async function settleOauthSession(db: Queryable, id: number, result: { subject: string } | { error: string }): Promise<boolean> {
  const r = 'subject' in result
    ? await db.query("UPDATE oauth_sessions SET status = 'ready', subject = $2, completed_at = now() WHERE id = $1 AND status = 'pending'", [id, result.subject])
    : await db.query("UPDATE oauth_sessions SET status = 'error', error_code = $2, completed_at = now() WHERE id = $1 AND status = 'pending'", [id, result.error]);
  return (r.rowCount ?? 0) > 0;
}

/** 게임이 결과를 가져갈 때 한 번만: ready -> done */
export async function consumeOauthSession(db: Queryable, id: number): Promise<boolean> {
  const r = await db.query("UPDATE oauth_sessions SET status = 'done' WHERE id = $1 AND status = 'ready'", [id]);
  return (r.rowCount ?? 0) > 0;
}

/** 하루 지난 기록은 지운다(시작 때 가볍게 정리) */
export async function purgeOauthSessions(db: Queryable): Promise<void> {
  await db.query("DELETE FROM oauth_sessions WHERE expires_at < now() - interval '1 day'");
}
