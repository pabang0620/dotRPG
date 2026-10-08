// 회원 탈퇴 테스트 공용 도우미: Steam / 개발용 계정 + 캐릭터, W2/W3 호출, 시각 당기기
import { randomBytes, randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { anonymizeOne } from '../src/domains/withdrawal/anonymizeService';
import { withTransaction } from '../src/db/pool';
import { auth, createChar, randomName, registerAccount, ver, type Session } from './helpers';
import { newPayer, newSteamId } from './payHelpers';

export const PHRASE = '탈퇴합니다';

export interface WUser {
  access: string;
  accountUuid: string;
  accountId: number;
  steamId: string | null;
  loginId: string | null;
  password: string | null;
  charUuid: string;
  charId: number;
  name: string;
}

export const ticketFor = (steamId: string): string => `mock:${steamId}:${randomBytes(8).toString('hex')}`;
export const asSession = (u: Pick<WUser, 'access'>): Session => ({ access: u.access } as Session);
export const authU = (u: Pick<WUser, 'access'>): Record<string, string> => auth(asSession(u));

async function withChar(app: Express, base: Omit<WUser, 'charUuid' | 'charId' | 'name'>): Promise<WUser> {
  const name = randomName();
  const res = await createChar(app, asSession(base), name);
  if (res.status !== 201) throw new Error(`createChar failed ${res.status} ${JSON.stringify(res.body)}`);
  const charUuid = res.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [charUuid]);
  return { ...base, charUuid, charId: Number((r.rows[0] as { id: string }).id), name };
}

/** Steam(mock) 계정 + 캐릭터 한 명 */
export async function steamUser(app: Express): Promise<WUser> {
  const p = await newPayer(app);
  return withChar(app, { access: p.access, accountUuid: p.accountUuid, accountId: p.accountId, steamId: p.steamId, loginId: null, password: null });
}

/** 개발용 계정 + 캐릭터 한 명 */
export async function devUser(app: Express): Promise<WUser> {
  const s = await registerAccount(app);
  const r = await getPool().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [s.accountId]);
  return withChar(app, { access: s.access, accountUuid: s.accountId, accountId: Number((r.rows[0] as { id: string }).id), steamId: null, loginId: s.loginId, password: s.password });
}

export const reauthOf = (u: WUser): Record<string, unknown> =>
  u.steamId ? { provider: 'steam', ticket: ticketFor(u.steamId) } : { provider: 'dev', password: u.password };

export function withdrawBody(u: WUser, over: Record<string, unknown> = {}): Record<string, unknown> {
  return { request_id: randomUUID(), confirm: PHRASE, ack_progress_loss: true, reauth: reauthOf(u), ...over };
}

export const requestWithdrawal = (app: Express, u: WUser, over: Record<string, unknown> = {}) =>
  request(app).post('/me/withdrawal').set(authU(u)).send(withdrawBody(u, over));

export const withdrawalInfo = (app: Express, u: WUser) => request(app).get('/me/withdrawal').set(authU(u));

export function cancelBody(u: WUser, over: Record<string, unknown> = {}): Record<string, unknown> {
  const reauth = u.steamId ? { provider: 'steam', ticket: ticketFor(u.steamId) } : { provider: 'dev', login_id: u.loginId, password: u.password };
  return { request_id: randomUUID(), reauth, ...over };
}

export const cancelWithdrawalApi = (app: Express, u: WUser, over: Record<string, unknown> = {}) =>
  request(app).post('/auth/withdrawal/cancel').set(ver()).send(cancelBody(u, over));

export const steamLoginApi = (app: Express, steamId: string) =>
  request(app).post('/auth/steam').set(ver()).send({ ticket: ticketFor(steamId) });

export const devLoginApi = (app: Express, loginId: string, password: string) =>
  request(app).post('/auth/dev/login').set(ver()).send({ login_id: loginId, password });

export { newSteamId };

// ---------- DB 확인·조작 ----------

export async function accountRow(accountId: number): Promise<Record<string, unknown>> {
  return (await getPool().query('SELECT * FROM accounts WHERE id = $1', [accountId])).rows[0] as Record<string, unknown>;
}

export async function withdrawalRows(accountId: number): Promise<Record<string, unknown>[]> {
  return (await getPool().query('SELECT * FROM account_withdrawals WHERE account_id = $1 ORDER BY id', [accountId])).rows as Record<string, unknown>[];
}

export async function openWithdrawal(accountId: number): Promise<Record<string, unknown>> {
  return (await getPool().query("SELECT * FROM account_withdrawals WHERE account_id = $1 AND state = 'requested'", [accountId])).rows[0] as Record<string, unknown>;
}

export async function charNames(accountId: number): Promise<string[]> {
  const r = await getPool().query<{ name: string }>('SELECT name FROM characters WHERE account_id = $1 ORDER BY id', [accountId]);
  return r.rows.map((x) => x.name);
}

/** 유예 기한을 지나게 만든다(CHECK due_at > requested_at 을 지킨다) */
export async function makeDue(accountId: number, over = ''): Promise<void> {
  await getPool().query(
    `UPDATE account_withdrawals SET requested_at = now() - interval '31 days', due_at = now() - interval '1 minute'${over} WHERE account_id = $1 AND state = 'requested'`,
    [accountId],
  );
}

/** 한 건을 바로 익명화한다(작업 대신 서비스 직접 호출) */
export async function anonymizeNowDirect(accountId: number, override = false): Promise<string> {
  const w = await getPool().query<{ id: string }>("SELECT id FROM account_withdrawals WHERE account_id = $1 AND state = 'requested'", [accountId]);
  const id = Number((w.rows[0] as { id: string }).id);
  const r = await withTransaction((c) => anonymizeOne(c, id, { now: new Date(), mode: 'admin', override }));
  return r.status;
}

/** 보관 기한을 지나게 만든다 */
export async function makeRetained(accountId: number): Promise<void> {
  await getPool().query("UPDATE account_withdrawals SET retain_until = now() - interval '1 minute' WHERE account_id = $1 AND state = 'completed'", [accountId]);
}

export const jobCtx = { opts: {}, shouldStop: () => false };
