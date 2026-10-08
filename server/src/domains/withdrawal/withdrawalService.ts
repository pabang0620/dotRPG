// 회원 탈퇴 공개 API(W1 확인, W2 요청, W3 철회). 규칙은 Docs/server/phase12_withdrawal.md 4, 5, 6절.
// 서버가 받는 것은 request_id, 확인 문구, 확인 체크, 재인증 자격뿐이다. 유예·보관·손실 목록은 서버가 계산한다.
import { randomBytes } from 'node:crypto';
import argon2 from 'argon2';
import { getConfig } from '../../config/env';
import { findRequest, hashRequest } from '../../db/idempotency';
import { getPool, withTransaction } from '../../db/pool';
import { consumeOrThrow, getRateLimitStore, HOUR, MINUTE } from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { metrics } from '../../ops/metrics';
import type { ApiBody, StoredResult } from '../economy/economyService';
import { verifyTicket } from '../auth/steamProvider';
import { cancelWithdrawal, startWithdrawal, WITHDRAW_ENDPOINT } from './withdrawalCore';
import * as repo from './withdrawalRepository';
import type { CancelBody, WithdrawBody } from './withdrawalValidation';

const DAY_MS = 86_400_000;
const ARGON_OPTS = { type: argon2.argon2id } as const;

let dummyHash: string | null = null;
async function getDummyHash(): Promise<string> {
  if (!dummyHash) dummyHash = await argon2.hash(randomBytes(16).toString('hex'), ARGON_OPTS);
  return dummyHash;
}

export function assertEnabled(): void {
  if (!getConfig().withdraw.enabled) throw new AppError(503, '회원 탈퇴 기능을 지금은 쓸 수 없습니다.', 'FEATURE_DISABLED');
}

/** 확인 문구 비교: NFC 정규화와 양끝 공백 제거 뒤 일치해야 한다 */
export const normalizePhrase = (s: string): string => s.normalize('NFC').trim();

/** 개발용 비밀번호 확인. 실패 횟수는 로그인 실패 카운터(login-fail:<id>)와 같은 저장소를 쓴다 */
async function checkDevPassword(loginId: string, secretHash: string | null, password: string): Promise<boolean> {
  const failKey = `login-fail:${loginId}`;
  const failWindow = 15 * MINUTE;
  const store = getRateLimitStore();
  const { count, retryAfterSec } = store.count(failKey, failWindow);
  if (count >= getConfig().rate.loginFail) {
    throw new AppError(429, '요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.', 'RATE_LIMITED', { retry_after_sec: retryAfterSec });
  }
  const ok = await argon2.verify(secretHash ?? (await getDummyHash()), password);
  if (!secretHash || !ok) {
    store.add(failKey, failWindow);
    metrics.recordLoginFailure();
    return false;
  }
  store.reset(failKey);
  return true;
}

const reauthFailed = () => new AppError(403, '본인 확인에 실패했습니다.', 'REAUTH_FAILED');

/** W2 재인증: 이 계정의 신원과 일치해야 한다. 티켓이 무효·재사용이면 401 대신 403 REAUTH_FAILED */
async function reauthSelf(accountId: number, reauth: WithdrawBody['reauth']): Promise<void> {
  if (!getConfig().withdraw.reauthRequired) return;
  if (!reauth) throw reauthFailed();
  const db = getPool();
  if (reauth.provider === 'steam') {
    let steamId: string;
    try {
      steamId = (await verifyTicket(reauth.ticket)).steamId;
    } catch (err) {
      if (err instanceof AppError && err.status === 401) throw reauthFailed();
      throw err;
    }
    const subject = await repo.steamSubjectOf(db, accountId);
    if (!subject || subject !== steamId) throw reauthFailed();
    return;
  }
  const dev = await repo.devIdentityOf(db, accountId);
  const ok = await checkDevPassword(dev?.subject ?? `none:${accountId}`, dev?.secret_hash ?? null, reauth.password);
  if (!ok) throw reauthFailed();
}

// ---------- W1 GET /me/withdrawal ----------

export async function getInfo(accountId: number) {
  assertEnabled();
  const cfg = getConfig().withdraw;
  consumeOrThrow(`withdraw-info:${accountId}`, cfg.rate.infoPerMin, MINUTE);
  const db = getPool();
  const [losses, hasOrder, steam] = await Promise.all([repo.lossSummary(db, accountId), repo.hasOpenOrder(db, accountId), repo.steamSubjectOf(db, accountId)]);
  const blockers = hasOrder ? ['payment_open'] : [];
  const notices = ['refund_follows_steam_policy', 'auction_and_mail_lost_after_grace', 'friends_party_not_restored'];
  if (losses.paid_stars > 0) notices.unshift('paid_stars_forfeited');
  return {
    enabled: true,
    confirm_phrase: cfg.confirmPhrase,
    grace_days: cfg.graceDays,
    due_at_if_now: new Date(getNow().getTime() + cfg.graceDays * DAY_MS).toISOString(),
    reauth: steam ? 'steam' : 'dev',
    can_request: blockers.length === 0,
    blockers,
    losses,
    notices,
  };
}

// ---------- W2 POST /me/withdrawal ----------

export async function requestWithdrawal(accountId: number, ip: string, body: WithdrawBody): Promise<StoredResult> {
  assertEnabled();
  const cfg = getConfig().withdraw;
  consumeOrThrow(`withdraw-ip:${ip}`, cfg.rate.ipPerHour, HOUR);
  consumeOrThrow(`withdraw-acct:${accountId}`, cfg.rate.perHour, HOUR);
  if (normalizePhrase(body.confirm) !== normalizePhrase(cfg.confirmPhrase)) {
    throw new AppError(422, '확인 문구가 일치하지 않습니다.', 'CONFIRM_MISMATCH');
  }
  const hash = hashRequest({ endpoint: WITHDRAW_ENDPOINT, payload: { confirm: normalizePhrase(body.confirm), ack_paid_loss: body.ack_paid_loss === true } });
  // 같은 request_id 의 재전송은 재인증(1회용 Steam 티켓) 없이 첫 응답을 돌려준다
  const prior = await findRequest(getPool(), accountId, body.request_id);
  if (prior) {
    if (prior.requestHash !== hash || prior.endpoint !== WITHDRAW_ENDPOINT) {
      throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
    }
    return { status: prior.statusCode, body: prior.response as ApiBody, replay: true };
  }
  await reauthSelf(accountId, body.reauth);
  const now = getNow();
  const r = await withTransaction((client) =>
    startWithdrawal(client, {
      accountId,
      requestId: body.request_id,
      source: 'self',
      adminId: null,
      now,
      self: { ackPaidLoss: body.ack_paid_loss === true },
      cancelAllowed: true,
      idem: { endpoint: WITHDRAW_ENDPOINT, hash },
    }),
  );
  return { status: r.status, body: r.body, replay: r.replay };
}

// ---------- W3 POST /auth/withdrawal/cancel ----------

export async function cancelWithdrawalByCredentials(ip: string, body: CancelBody): Promise<{ cancelled: true; renamed_characters: number }> {
  assertEnabled();
  const cfg = getConfig().withdraw;
  consumeOrThrow(`withdraw-cancel-ip:${ip}`, cfg.rate.cancelIpPerHour, HOUR);
  const db = getPool();
  let accountId: number | null = null;
  if (body.reauth.provider === 'steam') {
    // 로그인 Steam 경로(/auth/steam)와 같은 버킷·한도(RATE_STEAM_IP_MAX/분): 티켓 검증은 Steam 웹 API 를 부르므로 IP 제한만으로는 부족하다
    consumeOrThrow(`steam-ip:${ip}`, getConfig().rate.steamIp, MINUTE);
    const id = await verifyTicket(body.reauth.ticket);
    accountId = (await repo.accountByIdentity(db, 'steam', id.steamId))?.id ?? null;
  } else {
    const found = await repo.accountByIdentity(db, 'dev', body.reauth.login_id);
    const ok = await checkDevPassword(body.reauth.login_id, found?.secret_hash ?? null, body.reauth.password);
    if (!ok) throw reauthFailed();
    accountId = found?.id ?? null;
  }
  // 신원이 없거나(이미 익명화됨) 열린 요청이 없으면 같은 응답: 계정이 있는지 알려 주지 않는다
  if (accountId === null) throw new AppError(404, '열린 탈퇴 요청이 없습니다.', 'NO_PENDING_WITHDRAWAL');
  consumeOrThrow(`withdraw-cancel-acct:${accountId}`, cfg.rate.cancelAccountPerHour, HOUR);
  const id = accountId;
  const r = await withTransaction((client) => cancelWithdrawal(client, { accountId: id, via: 'self', cancelRequestId: body.request_id, now: getNow() }));
  return { cancelled: true, renamed_characters: r.renamed_characters };
}
