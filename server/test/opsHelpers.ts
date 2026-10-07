import { createHash, randomBytes, randomUUID } from 'node:crypto';
import argon2 from 'argon2';
import type { Express } from 'express';
import request from 'supertest';
import { createAdminApp } from '../src/admin/adminApp';
import { encryptSecret } from '../src/admin/common/secretBox';
import { newTotpSecret, stepOf, totpCode } from '../src/admin/common/totp';
import { getPool } from '../src/db/pool';

export const adminApp = (): Express => createAdminApp();
export const rid = (): string => randomUUID();
export const bearer = (t: string): Record<string, string> => ({ Authorization: `Bearer ${t}` });

export interface TestAdmin {
  id: number;
  uuid: string;
  loginId: string;
  password: string;
  secret: Buffer | null;
  token: string;
}

/** 관리자를 DB에 직접 만들고(2FA 등록 완료 상태) 세션 토큰을 하나 발급해 둔다. 로그인 API는 쓰지 않는다 */
export async function makeAdmin(
  role: 'viewer' | 'operator' | 'owner',
  o: { totp?: boolean; mustChange?: boolean; scope?: 'full' | 'setup' } = {},
): Promise<TestAdmin> {
  const loginId = `a${randomBytes(5).toString('hex')}`;
  const password = 'correct-horse-battery-1';
  const secret = o.totp === false ? null : newTotpSecret();
  const r = await getPool().query<{ id: string; uuid: string }>(
    `INSERT INTO admin_users (login_id, display_name, role, password_hash, must_change_password, totp_secret_enc, totp_confirmed_at)
     VALUES ($1, $1, $2, $3, $4, $5, $6) RETURNING id, uuid`,
    [loginId, role, await argon2.hash(password, { type: argon2.argon2id }), o.mustChange ?? false, secret ? encryptSecret(secret) : null, secret ? new Date() : null],
  );
  const id = Number((r.rows[0] as { id: string }).id);
  const token = await sessionFor(id, o.scope ?? 'full');
  return { id, uuid: (r.rows[0] as { uuid: string }).uuid, loginId, password, secret, token };
}

export async function sessionFor(adminId: number, scope: 'full' | 'setup' = 'full'): Promise<string> {
  const token = randomBytes(32).toString('base64url');
  await getPool().query(
    `INSERT INTO admin_sessions (admin_id, token_hash, scope, expires_at) VALUES ($1, $2, $3, now() + interval '8 hours')`,
    [adminId, createHash('sha256').update(token).digest('hex'), scope],
  );
  return token;
}

/** 지금 구간(offset 0) 또는 앞뒤 구간의 TOTP 코드 */
export const codeFor = (secret: Buffer, offset = 0): string => totpCode(secret, stepOf(new Date()) + offset);

export const adminGet = (app: Express, t: TestAdmin | string, path: string) =>
  request(app).get(path).set(bearer(typeof t === 'string' ? t : t.token));

export const adminPost = (app: Express, t: TestAdmin | string, path: string, body: Record<string, unknown> = {}, requestId: string = rid()) =>
  request(app)
    .post(path)
    .set(bearer(typeof t === 'string' ? t : t.token))
    .send({ request_id: requestId, ...body });

export async function auditRows(where = '1=1', params: unknown[] = []): Promise<Record<string, unknown>[]> {
  const r = await getPool().query(`SELECT * FROM admin_audit_log WHERE ${where} ORDER BY id`, params);
  return r.rows as Record<string, unknown>[];
}
