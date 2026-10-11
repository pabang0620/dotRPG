// 주문 상태 기계(Docs/server/phase11_payments.md 6절, 8.2, 9절).
// advanceOrder 하나가 B3(sync), 대사·감시 작업, 관리자 재확인, B1·B6의 열린 주문 대사를 모두 움직인다.
// 한 시점에 한 작업자만(임대 lease), 모든 쓰기는 lease_token + 기대 상태로 펜싱한다. Steam 호출은 DB 트랜잭션 밖에서 한다.
// 상태 문자열(Init, Approved, Succeeded, Failed, Refunded, PartialRefund, Chargeback, RefundedSuspectedFraud, RefundedFriendlyFraud)은
// 설계 7.4/9.2가 가정한 이름이다. TODO [확인] 7.8-3 샌드박스 실측으로 정확한 철자·의미를 확정한다.
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import * as holdsRepo from '../antiabuse/holdsRepository';
import { creditPaid, reversePaid } from '../starshop/starWallet';
import * as repo from './paymentsRepository';
import type { EventKind, FlagKind, OrderRow, OrderState } from './paymentsRepository';
import { RESTRICT_DAYS } from './payTier';
import { getSteamPartner, PartnerCallError, type TxnView } from './steamPartner';
import './steamPartnerHttp';

const LEASE_SECONDS = 30;
const OPEN_NEXT_CHECK_SECONDS = 30;
const REVIEW_NEXT_CHECK_SECONDS = 3600;
const DAY_MS = 86_400_000;

type Actor = 'player' | 'job' | 'admin' | 'system';
interface Ctx {
  token: string;
  actor: Actor;
}

const KNOWN_STATUS = new Set(['Init', 'Approved', 'Succeeded', 'Failed', 'Refunded', 'PartialRefund', 'Chargeback', 'RefundedSuspectedFraud', 'RefundedFriendlyFraud']);
type Reversal = 'refund' | 'partial' | 'chargeback';
const REVERSAL_OF: Record<string, Reversal> = {
  Refunded: 'refund',
  PartialRefund: 'partial',
  Chargeback: 'chargeback',
  RefundedSuspectedFraud: 'chargeback',
  RefundedFriendlyFraud: 'chargeback',
};
const OPEN: readonly OrderState[] = ['pending_init', 'created', 'authorized', 'finalized'];

export interface AdvanceResult {
  order: OrderRow;
  /** Steam에 닿지 못했거나 회로가 열려 있어 확인하지 못했다(주문 상태는 바뀌지 않았다) */
  steamError: PartnerCallError | null;
  /** 이번 호출이 임대를 잡고 진행했는가(false면 다른 작업자가 진행 중이거나 sync 최소 간격 안) */
  leased: boolean;
}

/** 감시 간격(7.4): 3일까지 15분, 30일까지 6시간, PAY_WATCH_DAYS까지 24시간, 이후 점검 종료(null) */
export function watchNextCheck(grantedAt: Date, now: Date): Date | null {
  const days = (now.getTime() - grantedAt.getTime()) / DAY_MS;
  if (days > getConfig().pay.watchDays) return null;
  const sec = days < 3 ? 900 : days < 30 ? 21_600 : 86_400;
  return new Date(now.getTime() + sec * 1000);
}

const afterSec = (now: Date, s: number): Date => new Date(now.getTime() + s * 1000);

export async function advanceOrder(orderId: number, opts: { actor: Actor; throttleSec?: number; event?: EventKind }): Promise<AdvanceResult> {
  const db = getPool();
  const leased = await repo.acquireLease(db, orderId, LEASE_SECONDS, opts.throttleSec ?? 0);
  if (!leased) {
    const cur = await repo.orderById(db, orderId);
    if (!cur) throw new AppError(404, '주문을 찾을 수 없습니다.', 'ORDER_NOT_FOUND');
    return { order: cur, steamError: null, leased: false };
  }
  const token = leased.lease_token as string;
  const ctx: Ctx = { token, actor: opts.actor };
  let steamError: PartnerCallError | null = null;
  try {
    if (opts.event) await repo.insertEvent(db, orderId, { kind: opts.event, actor: opts.actor });
    steamError = await run(leased, ctx);
  } finally {
    await repo.releaseLease(db, orderId, token).catch((err: unknown) => logger.error({ err }, 'payment.lease_release_failed'));
  }
  const order = (await repo.orderById(db, orderId)) as OrderRow;
  return { order, steamError, leased: true };
}

/** 한 번의 진행: 조회 -> 판정 -> (확정) -> 재조회 -> 지급. 확정 호출은 한 번만 한다 */
async function run(first: OrderRow, ctx: Ctx): Promise<PartnerCallError | null> {
  let o = first;
  let finalizeTried = false;
  for (let i = 0; i < 6; i++) {
    let r: { again: boolean; finalizeCalled?: boolean };
    try {
      r = await stepOnce(o, ctx, finalizeTried);
    } catch (err) {
      if (err instanceof PartnerCallError) {
        // 확인하지 못했다. 상태는 그대로 두고 다음 대사 때 다시 본다(회로가 열린 동안 만료 시각이 지나도 닫지 않는다, 7.7)
        const cur = (await repo.orderById(getPool(), o.id)) ?? o;
        await repo.touchChecked(getPool(), o.id, ctx.token, cur.next_check_at !== null || OPEN.includes(cur.state) ? afterSec(getNow(), OPEN_NEXT_CHECK_SECONDS) : null);
        return err;
      }
      throw err;
    }
    if (r.finalizeCalled) finalizeTried = true;
    if (!r.again) return null;
    o = (await repo.orderById(getPool(), o.id)) as OrderRow;
  }
  return null;
}

async function transition(
  o: OrderRow,
  ctx: Ctx,
  to: OrderState,
  set: Record<string, unknown>,
  ev: { kind: EventKind; steamStatus?: string | null; detail?: Record<string, unknown> },
  extra?: (client: Parameters<Parameters<typeof withTransaction>[0]>[0]) => Promise<void>,
): Promise<boolean> {
  return withTransaction(async (client) => {
    const ok = await repo.updateFenced(client, o.id, ctx.token, o.state, { state: to, last_checked_at: new Date(), ...set });
    if (!ok) return false;
    await repo.insertEvent(client, o.id, { kind: ev.kind, from: o.state, to, steamStatus: ev.steamStatus ?? null, actor: ctx.actor, detail: ev.detail });
    if (extra) await extra(client);
    return true;
  });
}

const closed = (): Record<string, unknown> => ({ closed_at: new Date(), next_check_at: null });

async function isBlocked(accountId: number): Promise<boolean> {
  const db = getPool();
  const p = await repo.profileOf(db, accountId);
  if (p?.status === 'blocked') return true;
  const a = await db.query<{ banned_until: Date | null }>('SELECT banned_until FROM accounts WHERE id = $1', [accountId]);
  const until = a.rows[0]?.banned_until;
  return !!until && until.getTime() > getNow().getTime();
}

type Mismatch = { kind: FlagKind; field: string };

function validateTxn(o: OrderRow, txn: TxnView, currentSteamId: string | null): Mismatch | null {
  if (txn.orderId !== o.steam_order_id) return { kind: 'amount_mismatch', field: 'order_id' };
  if (o.steam_trans_id && txn.transId && txn.transId !== o.steam_trans_id) return { kind: 'amount_mismatch', field: 'trans_id' };
  if (txn.steamId !== o.steam_id || currentSteamId !== o.steam_id) return { kind: 'steamid_mismatch', field: 'steam_id' };
  if (txn.appId !== o.app_id) return { kind: 'appid_mismatch', field: 'app_id' };
  if (txn.items.length !== 1) return { kind: 'amount_mismatch', field: 'item_count' };
  const it = txn.items[0] as TxnView['items'][number];
  if (it.itemId !== o.steam_item_id) return { kind: 'amount_mismatch', field: 'item_id' };
  if (it.quantity !== 1) return { kind: 'amount_mismatch', field: 'quantity' };
  if (it.amountMinor !== o.amount_minor) return { kind: 'amount_mismatch', field: 'amount' };
  if (txn.currency !== o.currency) return { kind: 'amount_mismatch', field: 'currency' };
  return null;
}

/** 7.3-9: 검증 실패. 확정·지급하지 않고 failed(mismatch) + needs_review + 플래그 + critical 로그 */
async function closeMismatch(o: OrderRow, ctx: Ctx, m: Mismatch, txn: TxnView): Promise<void> {
  const done = await transition(
    o,
    ctx,
    'failed',
    { fail_reason: 'mismatch', needs_review: true, review_reason: `${m.kind}:${m.field}`, closed_at: new Date(), next_check_at: afterSec(getNow(), REVIEW_NEXT_CHECK_SECONDS), steam_status: txn.status.slice(0, 40) },
    { kind: 'mismatch', steamStatus: txn.status, detail: { mismatch: m.kind, field: m.field, steam_status: txn.status, currency: txn.currency, amount: txn.items[0]?.amountMinor ?? null } },
    async (client) => {
      await repo.insertFlag(client, { accountId: o.account_id, orderId: o.id, kind: m.kind, severity: 3, detail: { field: m.field, steam_status: txn.status }, dedupeMinutes: 60 });
    },
  );
  if (done) logger.error({ order: o.uuid, mismatch: m.kind, field: m.field }, 'payment.mismatch');
}

async function markNeedsReview(o: OrderRow, ctx: Ctx, reason: string, flag: FlagKind, detail: Record<string, unknown>): Promise<void> {
  if (o.needs_review) {
    await repo.touchChecked(getPool(), o.id, ctx.token, afterSec(getNow(), REVIEW_NEXT_CHECK_SECONDS));
    return;
  }
  await withTransaction(async (client) => {
    const ok = await repo.updateFenced(client, o.id, ctx.token, o.state, { needs_review: true, review_reason: reason, last_checked_at: new Date(), next_check_at: afterSec(getNow(), REVIEW_NEXT_CHECK_SECONDS) });
    if (!ok) return;
    await repo.insertEvent(client, o.id, { kind: 'needs_review', from: o.state, to: o.state, actor: ctx.actor, detail: { reason, ...detail } });
    await repo.insertFlag(client, { accountId: o.account_id, orderId: o.id, kind: flag, severity: 3, detail, dedupeMinutes: 60 });
  });
  logger.error({ order: o.uuid, reason }, 'payment.needs_review');
}

async function touch(o: OrderRow, ctx: Ctx): Promise<void> {
  const now = getNow();
  let next: Date | null;
  // 지급된 주문과 환불된 주문(차지백 승격 감시)은 나이별 감시 간격으로 계속 본다
  if (o.state === 'granted') next = o.granted_at ? watchNextCheck(o.granted_at, now) : null;
  else if (o.state === 'refunded' && !o.needs_review) next = o.reversed_at ? watchNextCheck(o.reversed_at, now) : null;
  else if (o.needs_review) next = afterSec(now, REVIEW_NEXT_CHECK_SECONDS);
  else next = OPEN.includes(o.state) ? afterSec(now, OPEN_NEXT_CHECK_SECONDS) : null;
  await repo.touchChecked(getPool(), o.id, ctx.token, next);
}

async function stepOnce(o: OrderRow, ctx: Ctx, finalizeTried: boolean): Promise<{ again: boolean; finalizeCalled?: boolean }> {
  const db = getPool();
  const now = getNow();
  const q = await getSteamPartner().queryTxn(o.steam_order_id);
  if (!q.found) return notFound(o, ctx, now);
  const txn = q.txn;
  const currentSteam = await repo.steamIdOf(db, o.account_id);
  const mismatch = validateTxn(o, txn, currentSteam);
  const status = txn.status;

  // 관측 기록(상태 문자열·국가·금액·통화·거래 번호). 상태 문자열이 바뀐 때만 이벤트를 남긴다
  try {
    await repo.updateFenced(db, o.id, ctx.token, o.state, {
      steam_status: status.slice(0, 40),
      ...(txn.country && /^[A-Z]{2}$/.test(txn.country) ? { steam_country: txn.country } : {}),
      ...(txn.items[0] ? { steam_amount_minor: txn.items[0].amountMinor } : {}),
      ...(/^[A-Z]{3}$/.test(txn.currency) ? { steam_currency: txn.currency } : {}),
      ...(o.steam_trans_id === null && txn.transId && /^\d+$/.test(txn.transId) ? { steam_trans_id: txn.transId } : {}),
      last_checked_at: new Date(),
    });
  } catch (err) {
    // 같은 거래 번호가 다른 주문에 이미 있다(steam_trans_id 유일): 재전송·위조 신호
    if (!isUniqueViolation(err)) throw err;
    if (!mismatch) await closeMismatch(o, ctx, { kind: 'amount_mismatch', field: 'trans_id' }, txn);
    return { again: false };
  }
  if (o.steam_status !== status) await repo.insertEvent(db, o.id, { kind: 'status_seen', from: o.state, steamStatus: status, actor: ctx.actor, detail: { steam_status: status } });

  const grantedLike = o.state === 'granted' || o.state === 'refunded' || o.state === 'chargeback';
  if (mismatch && !grantedLike) {
    // 이미 닫힌 검토 주문은 같은 불일치를 다시 기록하지 않는다
    if (o.state === 'failed') {
      await touch(o, ctx);
      return { again: false };
    }
    await closeMismatch(o, ctx, mismatch, txn);
    return { again: false };
  }
  if (!KNOWN_STATUS.has(status)) {
    await markNeedsReview(o, ctx, 'unknown_steam_status', 'unknown_steam_status', { steam_status: status.slice(0, 40) });
    return { again: false };
  }

  const reversal = REVERSAL_OF[status];
  if (reversal) {
    if (o.state === 'failed' || OPEN.includes(o.state) || o.state === 'granted' || o.state === 'refunded') {
      await reverseOrder(o, ctx, reversal);
      const cur = await repo.orderById(db, o.id);
      // 이미 처리된 환불을 또 봤다(멱등): 다음 감시 시각만 민다
      if (cur && cur.state === 'refunded') await touch(cur, ctx);
    }
    return { again: false };
  }

  // 검토 중인 failed(mismatch): 지금은 검증이 통과하고 Steam이 성공으로 확정했다면 되살려 지급까지 진행한다
  if (o.state === 'failed') {
    if (o.needs_review && o.fail_reason === 'mismatch' && status === 'Succeeded') {
      try {
        const ok = await transition(o, ctx, 'finalized', { fail_reason: null, needs_review: false, review_reason: null, closed_at: null, finalized_at: new Date(), next_check_at: afterSec(now, OPEN_NEXT_CHECK_SECONDS) }, { kind: 'review_cleared', steamStatus: status });
        if (ok) return { again: true };
      } catch (err) {
        if (!isUniqueViolation(err, 'star_orders_one_open')) throw err;
      }
    }
    await touch(o, ctx);
    return { again: false };
  }

  switch (status) {
    case 'Init': {
      if (o.state === 'pending_init') {
        await transition(o, ctx, 'created', { init_at: new Date(), next_check_at: afterSec(now, OPEN_NEXT_CHECK_SECONDS) }, { kind: 'init_ok', steamStatus: status, detail: { adopted: true } });
        return { again: false };
      }
      if (o.state === 'created' && now.getTime() > o.expires_at.getTime()) {
        await transition(o, ctx, 'expired', closed(), { kind: 'expired', steamStatus: status });
        return { again: false };
      }
      await touch(o, ctx);
      return { again: false };
    }
    case 'Approved': {
      if (o.state === 'pending_init' || o.state === 'created') {
        // 승인 전에 만료된 주문을 나중에 승인해도 확정하지 않는다(9번 시나리오): Steam 쪽은 확정되지 않아 청구되지 않는다 [확인]
        if (now.getTime() > o.expires_at.getTime()) {
          await transition(o, ctx, 'expired', closed(), { kind: 'expired', steamStatus: status });
          return { again: false };
        }
        const ok = await transition(o, ctx, 'authorized', { authorized_at: new Date(), next_check_at: afterSec(now, OPEN_NEXT_CHECK_SECONDS), ...(o.state === 'pending_init' ? { init_at: new Date() } : {}) }, { kind: 'authorized', steamStatus: status });
        return { again: ok };
      }
      if (o.state === 'authorized') {
        if (await isBlocked(o.account_id)) {
          await transition(o, ctx, 'failed', { fail_reason: 'blocked', ...closed() }, { kind: 'failed', steamStatus: status, detail: { reason: 'blocked' } });
          return { again: false };
        }
        if (finalizeTried) {
          await touch(o, ctx);
          return { again: false };
        }
        if (o.attempts >= getConfig().pay.finalizeMaxAttempts) {
          await markNeedsReview(o, ctx, 'finalize_attempts_exceeded', 'stuck_order', { attempts: o.attempts });
          return { again: false };
        }
        await finalizeCall(o, ctx);
        return { again: true, finalizeCalled: true };
      }
      await touch(o, ctx);
      return { again: false };
    }
    case 'Succeeded': {
      if (o.state === 'finalized') {
        await grantOrder(o.id, ctx);
        return { again: false };
      }
      if (o.state === 'pending_init' || o.state === 'created' || o.state === 'authorized') {
        const ok = await transition(o, ctx, 'finalized', { finalized_at: new Date(), next_check_at: afterSec(now, OPEN_NEXT_CHECK_SECONDS) }, { kind: 'finalized', steamStatus: status });
        return { again: ok };
      }
      await touch(o, ctx);
      return { again: false };
    }
    case 'Failed': {
      if (OPEN.includes(o.state)) {
        const denied = o.state === 'pending_init' || o.state === 'created';
        await transition(o, ctx, 'failed', { fail_reason: denied ? 'user_denied' : 'steam_failed', ...closed() }, { kind: 'failed', steamStatus: status, detail: { reason: denied ? 'user_denied' : 'steam_failed' } });
        return { again: false };
      }
      if (o.state === 'granted') await markNeedsReview(o, ctx, 'failed_after_grant', 'unknown_steam_status', { steam_status: status });
      return { again: false };
    }
    default:
      await touch(o, ctx);
      return { again: false };
  }
}

async function notFound(o: OrderRow, ctx: Ctx, now: Date): Promise<{ again: boolean }> {
  if (o.state === 'pending_init') {
    const ageMin = (now.getTime() - o.created_at.getTime()) / 60_000;
    if (ageMin > getConfig().pay.initLostMinutes) {
      await transition(o, ctx, 'failed', { fail_reason: 'init_lost', ...closed() }, { kind: 'failed', detail: { reason: 'init_lost' } });
    } else {
      await touch(o, ctx);
    }
    return { again: false };
  }
  if (OPEN.includes(o.state) || o.state === 'granted') {
    await markNeedsReview(o, ctx, 'steam_order_missing', 'stuck_order', { state: o.state });
    return { again: false };
  }
  await touch(o, ctx);
  return { again: false };
}

/** FinalizeTxn(재시도 없음). 결과는 다음 조회가 판별한다. 시도 횟수는 호출 전에 올린다 */
async function finalizeCall(o: OrderRow, ctx: Ctx): Promise<void> {
  const db = getPool();
  await repo.updateFenced(db, o.id, ctx.token, 'authorized', { attempts: o.attempts + 1 });
  try {
    const r = await getSteamPartner().finalizeTxn(o.steam_order_id);
    await repo.insertEvent(db, o.id, r.ok ? { kind: 'finalize_ok', actor: ctx.actor } : { kind: 'finalize_failed', actor: ctx.actor, detail: { error_code: r.errorCode.slice(0, 40) } });
  } catch (err) {
    if (!(err instanceof PartnerCallError)) throw err;
    await repo.insertEvent(db, o.id, { kind: 'finalize_failed', actor: ctx.actor, detail: { error_code: err.kind } });
  }
}

// ---------------- 지급(8.2) ----------------

/** 주문당 정확히 1회. 주문 행 FOR UPDATE -> 지갑 -> 로트 순으로 잠그고 한 트랜잭션에서 원장·로트·지갑·주문 상태를 바꾼다 */
async function grantOrder(orderId: number, ctx: Ctx): Promise<boolean> {
  return withTransaction(async (client) => {
    const o = await repo.lockOrderById(client, orderId);
    if (!o) return false;
    if (o.state === 'granted') return true;
    if (o.state !== 'finalized' || o.lease_token !== ctx.token) return false;
    const now = getNow();
    const r = await creditPaid(client, { accountId: o.account_id, orderId: o.id, orderUuid: o.uuid, stars: o.stars });
    const ok = await repo.updateFenced(client, o.id, ctx.token, 'finalized', {
      state: 'granted',
      granted_at: new Date(),
      next_check_at: watchNextCheck(now, now),
      last_checked_at: new Date(),
    });
    if (!ok) throw new Error('grant fence lost');
    await repo.insertEvent(client, o.id, { kind: 'granted', from: 'finalized', to: 'granted', actor: ctx.actor, detail: { stars: o.stars, debt_settled: r.settled } });
    return true;
  });
}

// ---------------- 환불·차지백(9절) ----------------

/** 9.3. 이미 refunded 면 차지백 승격만, 이미 chargeback 이면 아무것도 하지 않는다(회수 원장은 주문당 1줄) */
async function reverseOrder(o: OrderRow, ctx: Ctx, kind: Reversal): Promise<void> {
  const result = await withTransaction(async (client) => {
    const cur = await repo.lockOrderById(client, o.id);
    if (!cur || cur.lease_token !== ctx.token) return null;
    if (cur.state === 'chargeback') return null;
    if (cur.state === 'refunded' && kind !== 'chargeback') return null;
    const now = getNow();
    const wasGranted = cur.granted_at !== null;
    const toState: OrderState = kind === 'chargeback' ? 'chargeback' : 'refunded';
    let taken = 0;
    let owed = 0;
    if (cur.state === 'refunded') {
      // 환불 뒤 차지백으로 승격: 회수는 이미 끝났다
      await repo.updateFenced(client, cur.id, ctx.token, 'refunded', { state: 'chargeback', last_checked_at: new Date(), next_check_at: null, needs_review: false, review_reason: null });
      await repo.insertEvent(client, cur.id, { kind: 'chargeback', from: 'refunded', to: 'chargeback', actor: ctx.actor, detail: { upgraded: true } });
    } else {
      if (wasGranted) {
        const rv = await reversePaid(client, { accountId: cur.account_id, orderId: cur.id, orderUuid: cur.uuid, kind: kind === 'chargeback' ? 'chargeback' : 'refund' });
        taken = rv.taken;
        owed = rv.owed;
      }
      const partial = kind === 'partial';
      const ok = await repo.updateFenced(client, cur.id, ctx.token, cur.state, {
        state: toState,
        reversed_at: new Date(),
        closed_at: new Date(),
        fail_reason: null,
        needs_review: partial,
        review_reason: partial ? 'partial_refund' : null,
        // 환불은 차지백으로 승격될 수 있어 감시를 이어 간다(차지백은 종결)
        next_check_at: toState === 'refunded' ? watchNextCheck(now, now) : null,
        last_checked_at: new Date(),
      });
      if (!ok) throw new Error('reversal fence lost');
      await repo.insertEvent(client, cur.id, { kind: toState === 'chargeback' ? 'chargeback' : 'refunded', from: cur.state, to: toState, actor: ctx.actor, detail: { granted: wasGranted, partial } });
      if (wasGranted) await repo.insertEvent(client, cur.id, { kind: 'revoked_stars', actor: ctx.actor, detail: { taken, owed } });
    }
    await reversalPolicy(client, cur, kind, taken, owed, wasGranted);
    return { taken, owed, kind };
  });
  if (result) logger.warn({ order: o.uuid, kind: result.kind, taken: result.taken, owed: result.owed }, 'payment.reversal');
}

type Tx = Parameters<Parameters<typeof withTransaction>[0]>[0];

/** 9.2 정책표의 계정 조치. 차지백 = 결제 정지 + 경제 정지(payment, 전파 없음) + 연결 계정 제한 + 검토. 환불 = 소비 후 환불 패턴 탐지 */
async function reversalPolicy(client: Tx, o: OrderRow, kind: Reversal, taken: number, owed: number, wasGranted: boolean): Promise<void> {
  const pay = getConfig().pay;
  const now = getNow();
  if (kind === 'chargeback') {
    await repo.blockProfile(client, o.account_id, 'chargeback', 'system', null);
    await repo.insertPaymentHold(client, o.account_id, { order: o.uuid, via: 'chargeback' });
    await repo.insertFlag(client, { accountId: o.account_id, orderId: o.id, kind: 'chargeback', severity: 3, detail: { taken, owed }, dedupeMinutes: 0 });
    // 연결 계정은 경제 정지로 전파하지 않고 제한 단계 + 검토 큐만(D8)
    const hold = getConfig().aa.hold;
    const since = new Date(now.getTime() - hold.linkDeviceDays * DAY_MS);
    const linked = new Set<number>();
    for (const d of await holdsRepo.devicesWithCounts(client, o.account_id, since)) {
      if (d.accounts > hold.linkDeviceMaxAccounts) continue;
      for (const a of await holdsRepo.accountsOnDevice(client, d.device_hash, since)) linked.add(a);
    }
    const key = await holdsRepo.steamKeyOf(client, o.account_id);
    if (key) for (const a of await holdsRepo.accountsOfSteamKey(client, key)) linked.add(a);
    linked.delete(o.account_id);
    for (const a of [...linked].sort((x, y) => x - y)) {
      await repo.raiseTierFloor(client, a, new Date(now.getTime() + RESTRICT_DAYS * DAY_MS));
      await repo.insertFlag(client, { accountId: a, orderId: null, kind: 'linked_chargeback', severity: 3, detail: { origin_order: o.uuid }, dedupeMinutes: 1440 });
    }
    return;
  }
  if (kind === 'partial') {
    await repo.insertFlag(client, { accountId: o.account_id, orderId: o.id, kind: 'partial_refund', severity: 2, detail: { taken, owed }, dedupeMinutes: 0 });
    return;
  }
  if (!wasGranted || o.stars <= 0) return;
  // 사서 뽑고 환불: 환불 시점 소비율이 기준 이상이면 플래그, 반복하면 제한·정지(9.6)
  if (owed / o.stars < pay.refundSpendRatio) return;
  let severity: 2 | 3 = 2;
  if (o.granted_at) {
    const burnEnd = new Date(o.granted_at.getTime() + pay.burnMinutes * 60_000);
    const burned = await client.query<{ s: string }>(
      `SELECT coalesce(sum(a.stars), 0) AS s FROM star_spend_allocs a JOIN star_ledger l ON l.id = a.ledger_id
        WHERE a.order_id = $1 AND l.reason IN ('gacha', 'exchange', 'sealed_pull', 'pass_buy', 'skill_style_buy') AND l.created_at <= $2`,
      [o.id, burnEnd],
    );
    if (Number(burned.rows[0]?.s ?? 0) >= o.stars * 0.9) severity = 3;
  }
  await repo.insertFlag(client, { accountId: o.account_id, orderId: o.id, kind: 'refund_after_spend', severity, detail: { owed, stars: o.stars }, dedupeMinutes: 0 });
  const n = await repo.countFlags(client, o.account_id, 'refund_after_spend', new Date(now.getTime() - 90 * DAY_MS));
  if (n >= pay.refundBlockCount) {
    await repo.blockProfile(client, o.account_id, 'refund_abuse', 'system', null);
  } else if (n >= pay.refundRestrictCount) {
    await repo.raiseTierFloor(client, o.account_id, new Date(now.getTime() + RESTRICT_DAYS * DAY_MS));
  }
}
