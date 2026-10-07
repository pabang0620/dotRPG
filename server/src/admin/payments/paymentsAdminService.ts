// PA1~PA8, PA14: 결제 주문·계정·플래그 관리자 API(Docs/server/phase11_payments.md 13.3). 서버에 "환불하기" API는 없다(RefundTxn 미사용):
// 환불은 Steamworks 파트너 사이트에서 하고 서버는 감시 작업이 감지해 자동 회수한다. 변경은 runAdminAction(멱등 + 감사 같은 트랜잭션).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import * as holdsRepo from '../../domains/antiabuse/holdsRepository';
import { advanceOrder } from '../../domains/payments/orderMachine';
import * as payRepo from '../../domains/payments/paymentsRepository';
import { computeTier, limitsFor } from '../../domains/payments/payTier';
import { readWallet } from '../../domains/starshop/starWallet';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import type { AdminCtx } from '../common/adminTypes';
import { auditView, runAdminAction, runAdminActionDetached, type ActionResult } from '../common/audit';
import * as repo from './paymentsAdminRepository';
import type { AdminOrderRow, FlagRow } from './paymentsAdminRepository';
import type { BlockBody, FlagListQuery, OrderListQuery, RecheckBody, ResolveBody, UnblockBody } from './paymentsAdminValidation';

const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);
const DAY_MS = 86_400_000;
export const ORDER_NOT_FOUND = () => new AppError(404, '주문을 찾을 수 없습니다.', 'ORDER_NOT_FOUND');
export const ACCOUNT_NOT_FOUND = () => new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');

/** 관리자에게는 상세를 보인다(플레이어 응답과 달리 fail_reason·검토 사유 포함). 임대 토큰·IP는 싣지 않는다 */
export function adminOrderView(o: AdminOrderRow) {
  return {
    id: o.uuid,
    account_id: o.account_uuid,
    state: o.state,
    fail_reason: o.fail_reason,
    needs_review: o.needs_review,
    review_reason: o.review_reason,
    product_id: o.product_id,
    stars: o.stars,
    currency: o.currency,
    amount_minor: o.amount_minor,
    steam_order_id: o.steam_order_id,
    steam_trans_id: o.steam_trans_id,
    steam_id: o.steam_id,
    steam_status: o.steam_status,
    steam_country: o.steam_country,
    steam_amount_minor: o.steam_amount_minor,
    steam_currency: o.steam_currency,
    tier: o.tier,
    app_id: o.app_id,
    catalog_version: o.catalog_version,
    attempts: o.attempts,
    created_at: o.created_at.toISOString(),
    expires_at: o.expires_at.toISOString(),
    init_at: iso(o.init_at),
    authorized_at: iso(o.authorized_at),
    finalized_at: iso(o.finalized_at),
    granted_at: iso(o.granted_at),
    reversed_at: iso(o.reversed_at),
    closed_at: iso(o.closed_at),
    next_check_at: iso(o.next_check_at),
    last_checked_at: iso(o.last_checked_at),
  };
}

const flagView = (f: FlagRow) => ({
  id: f.uuid,
  account_id: f.account_uuid,
  order_id: f.order_uuid,
  kind: f.kind,
  severity: f.severity,
  detail: f.detail,
  state: f.state,
  reviewed_by: f.reviewed_by,
  reviewed_at: iso(f.reviewed_at),
  note: f.note,
  created_at: f.created_at.toISOString(),
});

// ---------- PA1, PA2 ----------

export async function listOrders(q: OrderListQuery) {
  const rows = await repo.listOrders(getPool(), {
    state: q.state,
    account: q.account,
    from: q.from,
    to: q.to,
    needsReview: q.needs_review === undefined ? undefined : q.needs_review === 'true',
    cursor: repo.decodeCursor(q.cursor),
    limit: q.limit,
  });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return { items: page.map(adminOrderView), next_cursor: rows.length > q.limit && last ? repo.encodeCursor(last.id) : null };
}

export async function orderDetail(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const o = await repo.orderDetail(db, uuid);
  if (!o) throw ORDER_NOT_FOUND();
  await auditView(admin, ip, 'payment.order.view', 'payment_order', uuid);
  return {
    order: adminOrderView(o),
    events: (await payRepo.eventsOf(db, o.id)).map((e) => ({ id: e.id, kind: e.kind, from_state: e.from_state, to_state: e.to_state, steam_status: e.steam_status, actor: e.actor, detail: e.detail, created_at: e.created_at.toISOString() })),
    spend_allocations: (await repo.allocsOfOrder(db, o.id)).map((a) => ({ ledger_id: a.ledger_id, reason: a.reason, ref: a.ref, stars: a.stars, at: a.created_at.toISOString() })),
    ledger: (await repo.ledgerOfOrder(db, o.uuid)).map((l) => ({ ...l, created_at: l.created_at.toISOString() })),
    flags: (await repo.flagsOfOrder(db, o.id)).map(flagView),
  };
}

// ---------- PA3 ----------

/** Steam에 다시 조회해 진행한다. 검증을 건너뛰게 하는 옵션은 없다(지급 가능한 상태가 되면 advanceOrder가 지급까지 한다) */
export function recheck(admin: AdminCtx, ip: string, uuid: string, body: RecheckBody): Promise<ActionResult> {
  return runAdminActionDetached({
    admin,
    ip,
    action: 'payment.order.recheck',
    targetType: 'payment_order',
    targetUuid: uuid,
    requestId: body.request_id,
    params: {},
    work: async () => {
      const o = await payRepo.orderByUuid(getPool(), uuid);
      if (!o) throw ORDER_NOT_FOUND();
      const eligible = ['pending_init', 'created', 'authorized', 'finalized', 'granted'].includes(o.state) || o.needs_review;
      if (!eligible) throw new AppError(409, '이 상태에서는 다시 확인할 수 없습니다.', 'PAYMENT_STATE');
      const r = await advanceOrder(o.id, { actor: 'admin', event: 'admin_recheck' });
      const fresh = await repo.orderDetail(getPool(), uuid);
      return { status: 200, data: { order: fresh ? adminOrderView(fresh) : null, steam_reachable: r.steamError === null, progressed: r.leased } };
    },
  });
}

// ---------- PA4 ----------

export async function accountPayments(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const accountId = await payRepo.accountIdByUuid(db, uuid);
  if (accountId === null) throw ACCOUNT_NOT_FOUND();
  await auditView(admin, ip, 'payment.account.view', 'payment_account', uuid);
  const now = getNow();
  const profile = await payRepo.profileOf(db, accountId);
  const t = await computeTier(db, accountId, now, profile?.last_country ?? null);
  const limits = await limitsFor(db, accountId, t.tier, now);
  const wallet = await readWallet(db, accountId);
  const hold = getConfig().aa.hold;
  const since = new Date(now.getTime() - hold.linkDeviceDays * DAY_MS);
  const linkedIds = new Set<number>();
  for (const d of await holdsRepo.devicesWithCounts(db, accountId, since)) {
    if (d.accounts > hold.linkDeviceMaxAccounts) continue;
    for (const a of await holdsRepo.accountsOnDevice(db, d.device_hash, since)) linkedIds.add(a);
  }
  const key = await holdsRepo.steamKeyOf(db, accountId);
  if (key) for (const a of await holdsRepo.accountsOfSteamKey(db, key)) linkedIds.add(a);
  linkedIds.delete(accountId);
  const linked: string[] = [];
  for (const id of linkedIds) {
    const u = await payRepo.accountUuid(db, id);
    if (u) linked.push(u);
  }
  return {
    profile: profile
      ? { status: profile.status, block_reason: profile.block_reason, blocked_at: iso(profile.blocked_at), blocked_by: profile.blocked_by, tier_floor: profile.tier_floor, tier_floor_until: iso(profile.tier_floor_until), last_country: profile.last_country, last_currency: profile.last_currency, note: profile.note }
      : { status: 'active', block_reason: null, blocked_at: null, blocked_by: null, tier_floor: 'none', tier_floor_until: null, last_country: null, last_currency: null, note: null },
    tier: t.tier,
    tier_reasons: t.reasons,
    limits,
    wallet: { balance: wallet.balance, paid_balance: wallet.paidBalance, free_balance: wallet.balance - wallet.paidBalance, debt: wallet.debt },
    recent_orders: (await repo.recentOrdersOfAccount(db, accountId, 20)).map(adminOrderView),
    flags: (await repo.flagsOfAccount(db, accountId, 20)).map(flagView),
    linked_accounts: linked,
  };
}

// ---------- PA5, PA6 ----------

export function block(admin: AdminCtx, ip: string, uuid: string, body: BlockBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'payment.account.block',
    targetType: 'payment_account',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { reason: body.reason, note_length: body.note.length },
    handler: async (client) => {
      const accountId = await payRepo.accountIdByUuid(client, uuid);
      if (accountId === null) throw ACCOUNT_NOT_FOUND();
      const changed = await payRepo.blockProfile(client, accountId, body.reason, admin.loginId, body.note);
      return { status: 200, data: { blocked: true, already: !changed } };
    },
  });
}

const RELEASE_RESTRICT_REASONS = ['chargeback', 'refund_abuse', 'linked_chargeback'];

/** owner만. 차지백·환불 남용 사유의 해제는 14일 restricted 로 시작한다. 경제 정지(payment)는 H3에서 따로 푼다(결제 정지 해제가 경제 정지를 풀지 않는다) */
export function unblock(admin: AdminCtx, ip: string, uuid: string, body: UnblockBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'payment.account.unblock',
    targetType: 'payment_account',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { note_length: body.note.length },
    handler: async (client) => {
      const accountId = await payRepo.accountIdByUuid(client, uuid);
      if (accountId === null) throw ACCOUNT_NOT_FOUND();
      const p = await payRepo.profileOf(client, accountId);
      if (!p || p.status !== 'blocked') throw new AppError(409, '결제 정지 상태가 아닙니다.', 'PAYMENT_STATE');
      const floor = p.block_reason && RELEASE_RESTRICT_REASONS.includes(p.block_reason) ? new Date(getNow().getTime() + 14 * DAY_MS) : null;
      await payRepo.unblockProfile(client, accountId, floor, body.note);
      return { status: 200, data: { blocked: false, restricted_until: iso(floor) } };
    },
  });
}

// ---------- PA7, PA8 ----------

export async function listFlags(q: FlagListQuery) {
  const rows = await repo.listFlags(getPool(), { state: q.state, account: q.account, cursor: repo.decodeCursor(q.cursor), limit: q.limit });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return { items: page.map(flagView), next_cursor: rows.length > q.limit && last ? repo.encodeCursor(last.id) : null };
}

export function resolveFlag(admin: AdminCtx, ip: string, uuid: string, body: ResolveBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'payment.flag.resolve',
    targetType: 'payment_flag',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { resolution: body.resolution, note_length: body.note.length },
    handler: async (client) => {
      const cur = await repo.flagExists(client, uuid);
      if (!cur) throw new AppError(404, '플래그를 찾을 수 없습니다.', 'FLAG_NOT_FOUND');
      const r = await repo.resolveFlag(client, uuid, body.resolution, admin.loginId, body.note);
      if (!r) throw new AppError(409, '이미 처리된 플래그입니다.', 'PAYMENT_STATE');
      return { status: 200, data: { id: uuid, state: body.resolution } };
    },
  });
}

// ---------- PA14 ----------

export async function reconcileStatus(admin: AdminCtx, ip: string) {
  const db = getPool();
  await auditView(admin, ip, 'payment.reconcile.view', null, null);
  const last = await repo.lastReportRun(db);
  return {
    orders_by_state: await repo.stateCounts(db),
    stuck_orders: (await repo.stuckOrders(db)).map((s) => ({ id: s.uuid, state: s.state, created_at: s.created_at.toISOString() })),
    steam_sums: await repo.grantedSums(db),
    last_report: last ? { at: last.started_at.toISOString(), detail: last.detail } : null,
    unknown_orders: last && typeof last.detail.unknown_orders === 'number' ? last.detail.unknown_orders : 0,
    open_flags: (await repo.listFlags(db, { state: 'open', cursor: null, limit: 50 })).length > 0,
  };
}
