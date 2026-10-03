// 관리자 로그인·세션·비밀번호·TOTP(phase7_ops.md 5.3). 게임 계정과 분리되어 서로의 토큰·비밀번호가 통하지 않는다.
import { randomBytes } from 'node:crypto';
import argon2 from 'argon2';
import { getConfig } from '../../config/env';
import { getPool, withTransaction } from '../../db/pool';
import { consumeOrThrow, MINUTE } from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { sha256Hex } from '../common/adminAuth';
import { writeAudit } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import { decryptSecret, encryptSecret } from '../common/secretBox';
import { base32Encode, newTotpSecret, otpauthUri, verifyTotp } from '../common/totp';
import * as repo from './adminAuthRepository';
import type { LoginBody, PasswordBody, TotpConfirmBody } from './adminAuthValidation';

const ARGON = { type: argon2.argon2id } as const;
export const hashPassword = (pw: string): Promise<string> => argon2.hash(pw, ARGON);
/** 임시 비밀번호(난수 16자). 응답에 한 번만 보여 주고 어디에도 저장하지 않는다 */
export const newTempPassword = (): string => randomBytes(12).toString('base64url');

let dummy: string | null = null;
async function dummyHash(): Promise<string> {
  if (!dummy) dummy = await argon2.hash(randomBytes(16).toString('hex'), ARGON);
  return dummy;
}

const authFailed = (): AppError => new AppError(401, '아이디, 비밀번호 또는 인증 코드가 올바르지 않습니다.', 'AUTH_FAILED');

export interface SessionOut {
  token: string;
  expires_at: string;
  idle_timeout_minutes: number;
  scope: 'full' | 'setup';
}

async function newSession(
  db: Parameters<typeof repo.insertSession>[0],
  adminId: number,
  scope: 'full' | 'setup',
  note: string | null,
): Promise<SessionOut> {
  const cfg = getConfig().admin;
  const now = getNow();
  const token = randomBytes(32).toString('base64url');
  const expires = new Date(now.getTime() + cfg.sessionMaxHours * 3_600_000);
  await repo.insertSession(db, adminId, sha256Hex(token), scope, now, expires, note);
  return { token, expires_at: expires.toISOString(), idle_timeout_minutes: cfg.sessionIdleMinutes, scope };
}

const adminView = (a: repo.AdminRow) => ({ id: a.uuid, login_id: a.loginId, display_name: a.displayName, role: a.role });

/** AU1. 실패는 감사(auth.login, denied)에 오류 처리기가 남긴다. 연속 실패 횟수는 롤백과 무관하게 바로 반영한다 */
export async function login(body: LoginBody, ip: string, note: string | null) {
  const cfg = getConfig().admin;
  consumeOrThrow('admin-login', 20, MINUTE);
  const now = getNow();
  const admin = await repo.findByLoginId(getPool(), body.login_id);
  if (!admin || admin.disabledAt) {
    await argon2.verify(await dummyHash(), body.password).catch(() => false);
    throw authFailed();
  }
  if (admin.lockedUntil && admin.lockedUntil.getTime() > now.getTime()) {
    throw new AppError(423, '로그인 실패가 많아 잠시 잠겼습니다.', 'ADMIN_LOCKED', { locked_until: admin.lockedUntil.toISOString() });
  }
  const fail = async (): Promise<never> => {
    const locked = await repo.registerFailure(getPool(), admin.id, cfg.loginFailMax, new Date(now.getTime() + cfg.lockMinutes * 60_000));
    if (locked && locked.getTime() > now.getTime()) {
      throw new AppError(423, '로그인 실패가 많아 잠시 잠겼습니다.', 'ADMIN_LOCKED', { locked_until: locked.toISOString() });
    }
    throw authFailed();
  };
  if (!(await argon2.verify(admin.passwordHash, body.password))) return fail();

  const needsSetup = admin.mustChangePassword || admin.totpConfirmedAt === null;
  let step: number | null = null;
  if (!needsSetup) {
    if (!body.totp) throw new AppError(401, '2단계 인증 코드가 필요합니다.', 'TOTP_REQUIRED');
    const secret = decryptSecret(admin.totpSecretEnc as Buffer);
    step = verifyTotp(secret, body.totp, now, admin.totpLastStep);
    if (step === null) return fail();
  }
  return withTransaction(async (client) => {
    await repo.markLoginOk(client, admin.id, now, step);
    const session = await newSession(client, admin.id, needsSetup ? 'setup' : 'full', note);
    await writeAudit(client, { adminId: admin.id, action: 'auth.login', targetType: 'admin', targetUuid: admin.uuid, result: 'ok', params: { scope: session.scope }, ip });
    return { ...session, must_change_password: admin.mustChangePassword, totp_enrolled: admin.totpConfirmedAt !== null, admin: adminView(admin) };
  });
}

/** AU2 */
export async function logout(admin: AdminCtx, ip: string): Promise<{ logged_out: true }> {
  await withTransaction(async (client) => {
    await repo.revokeSession(client, admin.sessionId, getNow());
    await writeAudit(client, { adminId: admin.id, action: 'auth.logout', targetType: 'admin', targetUuid: admin.uuid, result: 'ok', ip });
  });
  return { logged_out: true };
}

/** AU3 */
export function me(admin: AdminCtx) {
  return {
    id: admin.uuid,
    login_id: admin.loginId,
    display_name: admin.displayName,
    role: admin.role,
    scope: admin.scope,
    must_change_password: admin.mustChangePassword,
    totp_enrolled: admin.totpConfirmed,
    session_expires_at: admin.sessionExpiresAt.toISOString(),
    idle_timeout_minutes: getConfig().admin.sessionIdleMinutes,
  };
}

/** 비밀번호 변경과 2FA 등록이 모두 끝났으면 setup 세션을 full 세션으로 바꿔 준다 */
async function upgradeIfReady(client: Parameters<typeof repo.insertSession>[0], admin: AdminCtx, ready: boolean): Promise<SessionOut | null> {
  if (admin.scope !== 'setup' || !ready) return null;
  const s = await newSession(client, admin.id, 'full', 'upgraded');
  await repo.revokeSession(client, admin.sessionId, getNow());
  return s;
}

/** AU4 */
export async function changePassword(admin: AdminCtx, body: PasswordBody, ip: string) {
  const cfg = getConfig().admin;
  return withTransaction(async (client) => {
    const row = await repo.findById(client, admin.id, true);
    if (!row) throw new AppError(401, '관리자 토큰이 올바르지 않습니다.', 'ADMIN_TOKEN_INVALID');
    if (!(await argon2.verify(row.passwordHash, body.current_password))) {
      // 비밀번호 추측 방어: 이 경로도 연속 실패에 센다(롤백되지 않게 풀로 쓴다)
      await repo.registerFailure(getPool(), row.id, cfg.loginFailMax, new Date(getNow().getTime() + cfg.lockMinutes * 60_000));
      throw new AppError(401, '현재 비밀번호가 올바르지 않습니다.', 'AUTH_FAILED');
    }
    if (body.current_password === body.new_password) throw new AppError(422, '새 비밀번호가 현재 비밀번호와 같습니다.', 'SAME_PASSWORD');
    const now = getNow();
    await repo.setPassword(client, row.id, await hashPassword(body.new_password), false, now);
    await repo.revokeAllSessions(client, row.id, now, admin.sessionId);
    const session = await upgradeIfReady(client, admin, row.totpConfirmedAt !== null);
    await writeAudit(client, { adminId: row.id, action: 'auth.password', targetType: 'admin', targetUuid: row.uuid, result: 'ok', ip });
    return { changed: true, session };
  });
}

/** AU5: TOTP 비밀키를 만든다(미확인 상태). 비밀키 원문은 응답에 한 번만 나가고 감사에는 남기지 않는다 */
export async function totpStart(admin: AdminCtx, ip: string) {
  return withTransaction(async (client) => {
    const row = await repo.findById(client, admin.id, true);
    if (!row) throw new AppError(401, '관리자 토큰이 올바르지 않습니다.', 'ADMIN_TOKEN_INVALID');
    if (row.totpConfirmedAt) throw new AppError(409, '이미 2단계 인증이 등록되어 있습니다.', 'TOTP_ALREADY_SET');
    const secret = newTotpSecret();
    await repo.setTotpPending(client, row.id, encryptSecret(secret));
    await writeAudit(client, { adminId: row.id, action: 'auth.totp_start', targetType: 'admin', targetUuid: row.uuid, result: 'ok', ip });
    return { secret: base32Encode(secret), otpauth_uri: otpauthUri('dotRPG admin', row.loginId, secret) };
  });
}

/** AU6 */
export async function totpConfirm(admin: AdminCtx, body: TotpConfirmBody, ip: string) {
  return withTransaction(async (client) => {
    const row = await repo.findById(client, admin.id, true);
    if (!row) throw new AppError(401, '관리자 토큰이 올바르지 않습니다.', 'ADMIN_TOKEN_INVALID');
    if (row.totpConfirmedAt) throw new AppError(409, '이미 2단계 인증이 등록되어 있습니다.', 'TOTP_ALREADY_SET');
    if (!row.totpSecretEnc) throw new AppError(409, '먼저 2단계 인증 시작을 해 주세요.', 'TOTP_NOT_STARTED');
    const now = getNow();
    const step = verifyTotp(decryptSecret(row.totpSecretEnc), body.code, now, null);
    if (step === null) throw new AppError(422, '인증 코드가 올바르지 않습니다.', 'TOTP_INVALID');
    await repo.confirmTotp(client, row.id, step, now);
    const session = await upgradeIfReady(client, admin, !row.mustChangePassword);
    await writeAudit(client, { adminId: row.id, action: 'auth.totp_confirm', targetType: 'admin', targetUuid: row.uuid, result: 'ok', ip });
    return { enrolled: true, session };
  });
}
