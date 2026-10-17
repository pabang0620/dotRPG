// 카카오·Google 로그인(0045, Docs/server/OAUTH_LOGIN.md). 게임은 딥링크나 로컬 수신 없이 이렇게 한다:
//   1. start: 서버가 state 와 폴링 비밀을 만들고 공급자 로그인 주소를 준다. 게임은 그 주소를 브라우저로 연다.
//   2. callback: 공급자가 브라우저를 우리 콜백으로 돌려보내면 인가 코드를 사용자 번호로 바꿔 세션에 적는다(토큰은 화면에 싣지 않는다).
//   3. poll: 게임이 state + 폴링 비밀로 결과를 한 번 가져간다. 로그인이면 Steam 로그인과 같은 길로 토큰을 발급, 연동이면 계정에 붙인다.
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { validationError } from '../../middleware/validationMiddleware';
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { accessSignal, recordLogin } from '../antiabuse/deviceRecords';
import { beginSession } from '../antiabuse/sessionService';
import { reconcileOpenOrderQuietly } from '../payments/paymentsService';
import { applyCarry, checkRejoin } from '../withdrawal/withdrawalIdentity';
import { pendingWithdrawalError } from '../withdrawal/withdrawalLogin';
import * as repo from './authRepository';
import type { AccessMeta, TokenPair } from './authService';
import { issueTokens } from './authService';
import { authorizeUrl, exchangeCode, type OauthProvider } from './oauthProvider';
import { assertOpen } from './playWindow';

const SESSION_MINUTES = 10;
const sha256 = (s: string): string => createHash('sha256').update(s).digest('hex');
const IDENTITY_UNIQUE = 'auth_identities_provider_subject_key';
const ACCOUNT_PROVIDER_UNIQUE = 'auth_identities_account_provider_uq';

/** 탈퇴 이월 표시(9.3)의 신원 키. Steam 은 SteamID 그대로, 카카오·Google 은 공급자 이름을 붙인다 */
export const oauthIdentityKey = (p: OauthProvider, subject: string): string => `${p}:${subject}`;

export async function start(p: OauthProvider, mode: 'login' | 'link', accountId: number | null) {
  const state = randomBytes(32).toString('base64url');
  const pollSecret = randomBytes(32).toString('base64url');
  const url = authorizeUrl(p, state); // 꺼진 공급자는 여기서 404
  const expiresAt = new Date(Date.now() + SESSION_MINUTES * 60_000);
  const db = getPool();
  await repo.insertOauthSession(db, { state, pollSecretHash: sha256(pollSecret), provider: p, mode, accountId, expiresAt });
  repo.purgeOauthSessions(db).catch((err: Error) => logger.warn({ err: err.message }, 'oauth session purge failed'));
  return { authorize_url: url, state, poll_secret: pollSecret, expires_at: expiresAt.toISOString(), poll_interval_sec: 2 };
}

/** 콜백 결과. 브라우저에 보여 줄 문장만 돌려준다(토큰·번호는 싣지 않는다) */
export async function callback(p: OauthProvider, q: { code?: string; state?: string; error?: string }): Promise<{ ok: boolean }> {
  if (!q.state) return { ok: false };
  const s = await repo.findOauthSession(getPool(), q.state);
  if (!s || s.provider !== p || s.status !== 'pending' || s.expires_at.getTime() <= Date.now()) return { ok: false };
  if (q.error || !q.code) {
    await repo.settleOauthSession(getPool(), s.id, { error: q.error === 'access_denied' ? 'cancelled' : 'provider_error' });
    return { ok: false };
  }
  try {
    const subject = await exchangeCode(p, q.code);
    await repo.settleOauthSession(getPool(), s.id, { subject });
    return { ok: true };
  } catch (err) {
    if (!(err instanceof AppError)) throw err;
    await repo.settleOauthSession(getPool(), s.id, { error: 'exchange_failed' });
    return { ok: false };
  }
}

type PollResult =
  | { status: 'pending' }
  | { status: 'error'; code: string }
  | ({ status: 'done'; mode: 'login'; provider: OauthProvider; created: boolean } & Record<string, unknown>)
  | { status: 'done'; mode: 'link'; provider: OauthProvider; linked: true };

export async function poll(state: string, pollSecret: string, meta: AccessMeta = {}, ip = ''): Promise<PollResult> {
  const s = await repo.findOauthSession(getPool(), state);
  // 폴링 비밀이 틀리면 세션이 없는 것과 같게 답한다(state 만 본 사람에게 아무것도 알리지 않는다)
  if (!s || s.poll_secret_hash !== sha256(pollSecret)) throw new AppError(404, '로그인 요청을 찾을 수 없습니다.', 'OAUTH_SESSION_NOT_FOUND');
  if (s.status === 'done') throw new AppError(409, '이미 처리된 로그인 요청입니다.', 'OAUTH_SESSION_USED');
  if (s.status === 'error') return { status: 'error', code: s.error_code ?? 'provider_error' };
  if (s.status === 'pending') {
    if (s.expires_at.getTime() <= Date.now()) return { status: 'error', code: 'expired' };
    return { status: 'pending' };
  }
  // ready: 한 번만 가져간다
  if (!(await repo.consumeOauthSession(getPool(), s.id))) throw new AppError(409, '이미 처리된 로그인 요청입니다.', 'OAUTH_SESSION_USED');
  const subject = s.subject as string;
  if (s.mode === 'link') {
    await link(s.account_id as number, s.provider, subject);
    return { status: 'done', mode: 'link', provider: s.provider, linked: true };
  }
  const data = await login(s.provider, subject, meta, ip);
  return { status: 'done', mode: 'login', provider: s.provider, ...data };
}

async function link(accountId: number, p: OauthProvider, subject: string): Promise<void> {
  try {
    await withTransaction((client) => repo.insertOauthIdentity(client, accountId, p, subject));
  } catch (err) {
    if (isUniqueViolation(err, IDENTITY_UNIQUE)) throw new AppError(409, '이미 다른 계정에 연결된 로그인입니다.', 'OAUTH_ALREADY_LINKED');
    if (isUniqueViolation(err, ACCOUNT_PROVIDER_UNIQUE)) throw new AppError(409, '이미 같은 방식의 로그인이 연결된 계정입니다.', 'ACCOUNT_ALREADY_LINKED');
    throw err;
  }
}

/** Steam 로그인(authService.steamLogin)과 같은 순서: 찾거나 만들기 -> 탈퇴·정지·시간대 검사 -> 세션 교체·토큰 발급·로그인 기록 */
async function login(p: OauthProvider, subject: string, meta: AccessMeta, ip: string) {
  if (getConfig().aa.deviceInfoRequired && !meta.device) throw validationError([{ path: 'device', message: '필수 값입니다' }]);
  const sig = accessSignal(meta.device, ip, meta.clientVersion);
  let created = false;
  let account = await repo.findAccountByIdentity(p, subject);
  if (!account) {
    try {
      account = await withTransaction(async (client) => {
        const carry = await checkRejoin(client, oauthIdentityKey(p, subject));
        const a = await repo.insertAccount(client);
        await repo.insertOauthIdentity(client, a.id, p, subject);
        if (carry) await applyCarry(client, a.id, carry);
        return a;
      });
      created = true;
    } catch (err) {
      if (!isUniqueViolation(err, IDENTITY_UNIQUE)) throw err;
      account = await repo.findAccountByIdentity(p, subject);
    }
  }
  if (account?.deleted_at) {
    const pending = await pendingWithdrawalError(account.id);
    if (pending) throw pending;
  }
  if (!account || account.deleted_at) throw new AppError(401, '로그인 확인에 실패했습니다.', 'OAUTH_EXCHANGE_FAILED');
  if (account.banned_until && account.banned_until.getTime() > Date.now()) {
    throw new AppError(403, '정지된 계정입니다.', 'ACCOUNT_BANNED', {
      banned_until: account.banned_until.toISOString(),
      reason_code: await repo.activeBanReason(getPool(), account.id),
    });
  }
  assertOpen({ start: account.play_window_start, end: account.play_window_end });
  const acc = account;
  const result = await withTransaction(async (client) => {
    const lastLogin = await repo.touchLastLogin(client, acc.id);
    const familyId = randomUUID();
    const session = await beginSession(client, acc.id, familyId, sig);
    const tokens: TokenPair = await issueTokens(client, acc.id, acc.uuid, familyId, sig);
    await recordLogin(client, acc.id, 'oauth_login', sig, { flags: session.replacedOther ? ['replaced_other'] : [] });
    return {
      account: { id: acc.uuid, created_at: acc.created_at.toISOString(), last_login_at: lastLogin.toISOString() },
      ...tokens,
      session: { replaced_other: session.replacedOther },
      created,
    };
  });
  if (!created) reconcileOpenOrderQuietly(acc.id);
  return result;
}
