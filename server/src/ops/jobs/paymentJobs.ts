// 11단계 작업(설계 7.4): payment-reconcile(열린 주문 대사), payment-watch(환불·차지백 감시), payment-report(일일 교차 점검), star-grant-expire.
// 대사·감시 작업은 PAYMENTS_ENABLED와 무관하게 돈다(결제 중인 돈을 놓치지 않기 위해). 모든 Steam 호출은 분당 상한(PAY_STEAM_MAX_CALLS_PER_MIN)을 지킨다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { advanceOrder } from '../../domains/payments/orderMachine';
import * as repo from '../../domains/payments/paymentsRepository';
import { getSteamPartner, PartnerCallError, takeJobCall } from '../../domains/payments/steamPartner';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import type { JobCtx, JobResult } from '../jobRunner';

const BATCH = 50;

async function runDue(ctx: JobCtx, states: repo.OrderState[]): Promise<{ checked: number; skippedBudget: boolean; steamErrors: number }> {
  let checked = 0;
  let steamErrors = 0;
  const now = getNow();
  while (!ctx.shouldStop()) {
    const due = await repo.dueOrders(getPool(), states, now, BATCH);
    if (due.length === 0) break;
    for (const d of due) {
      if (ctx.shouldStop()) break;
      if (!takeJobCall()) return { checked, skippedBudget: true, steamErrors };
      const r = await advanceOrder(d.id, { actor: 'job' });
      checked++;
      if (r.steamError) steamErrors++;
      // 회로가 열려 있으면 이번 틱은 접는다(계속 두드리지 않는다)
      if (r.steamError?.kind === 'breaker_open') return { checked, skippedBudget: false, steamErrors };
    }
    if (due.length < BATCH) break;
    // 같은 주문을 다시 읽지 않도록(Steam 오류로 next_check_at이 안 밀린 경우 방지) 한 번에 한 배치만
    break;
  }
  return { checked, skippedBudget: false, steamErrors };
}

/** payment-reconcile(60초): 열린 주문과 검토 중인 실패 주문(failed + needs_review)의 대사 */
export async function paymentReconcileJob(ctx: JobCtx): Promise<JobResult> {
  if (getConfig().pay.mode === 'off') return { rows: 0, detail: { skipped: 'mode_off' } };
  const r = await runDue(ctx, ['pending_init', 'created', 'authorized', 'finalized', 'failed']);
  return { rows: r.checked, detail: { ...r } };
}

/** payment-watch(5분): 지급된 주문(과 차지백으로 승격될 수 있는 환불 주문)을 나이별 간격으로 조회해 환불·차지백을 감지한다 */
export async function paymentWatchJob(ctx: JobCtx): Promise<JobResult> {
  if (getConfig().pay.mode === 'off') return { rows: 0, detail: { skipped: 'mode_off' } };
  const r = await runDue(ctx, ['granted', 'refunded']);
  return { rows: r.checked, detail: { ...r } };
}

/** payment-report(1일): Steam이 보는 주문 목록과 우리 star_orders를 대조한다. 읽기와 경보만, 주문 상태는 직접 바꾸지 않는다(재확인 대기열에 넣을 뿐) */
export async function paymentReportJob(): Promise<JobResult> {
  const cfg = getConfig().pay;
  if (cfg.mode === 'off') return { rows: 0, detail: { skipped: 'mode_off' } };
  const db = getPool();
  const now = getNow();
  const from = new Date(now.getTime() - cfg.reportDays * 86_400_000);
  if (!takeJobCall()) return { rows: 0, detail: { skipped: 'budget' } };
  let entries;
  try {
    entries = await getSteamPartner().getReport(from, now);
  } catch (err) {
    if (err instanceof PartnerCallError) return { rows: 0, detail: { error: err.kind } };
    throw err;
  }
  let unknown = 0;
  let statusDiff = 0;
  let amountDiff = 0;
  for (const e of entries) {
    const mine = await repo.orderBySteamOrderId(db, e.orderId).catch(() => null);
    if (!mine) {
      unknown++;
      const acct = await db.query<{ account_id: string }>("SELECT account_id FROM auth_identities WHERE provider = 'steam' AND subject = $1", [e.steamId]);
      logger.error({ steam_order: e.orderId }, 'payment.unknown_steam_order');
      if (acct.rows[0]) {
        await repo.insertFlag(db, { accountId: Number(acct.rows[0].account_id), orderId: null, kind: 'unknown_steam_order', severity: 3, detail: { steam_order_id: e.orderId, status: e.status.slice(0, 40) }, dedupeMinutes: 1440 });
      }
      continue;
    }
    let recheck = false;
    if (mine.steam_status !== null && mine.steam_status !== e.status) {
      statusDiff++;
      recheck = true;
    }
    if (e.amountMinor !== mine.amount_minor || e.currency !== mine.currency) {
      amountDiff++;
      recheck = true;
    }
    // 재확인 대기열: 다음 대사·감시 틱이 바로 집는다(감시가 끝난 오래된 주문도 한 번 다시 본다)
    if (recheck && ['pending_init', 'created', 'authorized', 'finalized', 'granted'].includes(mine.state)) {
      await db.query('UPDATE star_orders SET next_check_at = now() WHERE id = $1 AND lease_token IS NULL', [mine.id]);
    }
  }
  return { rows: entries.length, detail: { entries: entries.length, unknown_orders: unknown, status_diff: statusDiff, amount_diff: amountDiff } };
}

/** star-grant-expire(1시간): 승인되지 않은 운영 지급 대기를 만료로 닫는다 */
export async function starGrantExpireJob(): Promise<JobResult> {
  const r = await getPool().query("UPDATE star_admin_grants SET state = 'expired', closed_at = now() WHERE state = 'pending' AND expires_at <= now()");
  return { rows: r.rowCount ?? 0, detail: { expired: r.rowCount ?? 0 } };
}
