// PA9~PA12: 관리자 별조각 지급·부채 탕감(2인 승인, Docs/server/phase11_payments.md 12.1). 기존에 관리자 별조각 경로가 없어 가장 좁게 만든다:
// 대상 계정 1개, 1건·관리자별 일일·전체 일일 상한, 사유 필수, 작성(operator) + 승인(작성자 외 owner), 승인 전에는 별조각이 움직이지 않는다.
// 우편 캠페인에는 별조각을 첨부할 수 없다(기존 첨부 종류 gold/item/sweep_ticket).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import * as payRepo from '../../domains/payments/paymentsRepository';
import { creditFree, forgiveDebt, lockWallet, readWallet } from '../../domains/starshop/starWallet';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import type { AdminCtx } from '../common/adminTypes';
import { runAdminAction, type ActionResult } from '../common/audit';
import { ACCOUNT_NOT_FOUND, ORDER_NOT_FOUND } from './paymentsAdminService';
import * as repo from './paymentsAdminRepository';
import type { GrantRow } from './paymentsAdminRepository';
import type { GrantActionBody, GrantCreateBody, GrantListQuery } from './paymentsAdminValidation';

const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);
const limitErr = (field: string, message: string) => new AppError(422, message, 'GRANT_LIMIT', { field });

export function grantView(g: GrantRow) {
  return {
    id: g.uuid,
    kind: g.kind,
    account_id: g.account_uuid,
    stars: g.stars,
    related_order_id: g.related_order_uuid,
    state: g.state,
    created_by: g.created_by_name,
    approved_by: g.approved_by_name,
    memo: g.memo,
    created_at: g.created_at.toISOString(),
    approved_at: iso(g.approved_at),
    expires_at: g.expires_at.toISOString(),
    applied_stars: g.applied_stars,
  };
}

// ---------- PA9 ----------

export function createGrant(admin: AdminCtx, ip: string, body: GrantCreateBody): Promise<ActionResult> {
  const cfg = getConfig().pay.adminGrant;
  return runAdminAction({
    admin,
    ip,
    action: 'payment.grant.create',
    targetType: 'star_grant',
    requestId: body.request_id,
    params: { kind: body.kind, account_id: body.account_id, ...(body.stars !== undefined ? { stars: body.stars } : {}), ...(body.related_order_id ? { related_order_id: body.related_order_id } : {}), memo_length: body.memo.length },
    handler: async (client) => {
      // 승인이 불가능한 지급을 만들지 않는다: 활성 owner 2명 미만이면 거절
      if ((await repo.activeOwnerCount(client)) < 2) throw new AppError(409, '활성 owner 가 2명 이상이어야 별조각 지급을 작성할 수 있습니다.', 'GRANT_NEEDS_TWO_OWNERS');
      const accountId = await payRepo.accountIdByUuid(client, body.account_id);
      if (accountId === null) throw ACCOUNT_NOT_FOUND();
      let relatedOrderId: number | null = null;
      if (body.related_order_id) {
        const o = await payRepo.orderByUuid(client, body.related_order_id);
        if (!o) throw ORDER_NOT_FOUND();
        relatedOrderId = o.id;
      }
      const now = getNow();
      if (body.kind === 'grant') {
        const stars = body.stars as number;
        if (stars > cfg.maxStars) throw limitErr('max_stars', '1건 상한을 넘었습니다.');
        const since = new Date(resetBoundaries(now).dailyStartAt);
        const mine = await repo.grantedStarsSince(client, since, { createdBy: admin.id, states: ['pending', 'applied'], by: 'created' });
        if (mine + stars > cfg.dailyPerAdmin) throw limitErr('daily_per_admin', '관리자별 일일 상한을 넘었습니다.');
        const total = await repo.grantedStarsSince(client, since, { states: ['pending', 'applied'], by: 'created' });
        if (total + stars > cfg.dailyTotal) throw limitErr('daily_total', '전체 일일 상한을 넘었습니다.');
      }
      const uuid = await repo.insertGrant(client, {
        kind: body.kind,
        accountId,
        stars: body.kind === 'grant' ? (body.stars as number) : null,
        relatedOrderId,
        memo: body.memo,
        requestId: body.request_id,
        createdBy: admin.id,
        expiresAt: new Date(now.getTime() + cfg.pendingHours * 3_600_000),
      });
      const g = (await repo.grantByUuid(client, uuid)) as GrantRow;
      return { status: 201, data: { grant: grantView(g) }, targetUuid: uuid };
    },
  });
}

// ---------- PA10 ----------

export async function listGrants(q: GrantListQuery) {
  const rows = await repo.listGrants(getPool(), { state: q.state, account: q.account, cursor: repo.decodeCursor(q.cursor), limit: q.limit });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return { items: page.map(grantView), next_cursor: rows.length > q.limit && last ? repo.encodeCursor(last.id) : null };
}

// ---------- PA11 ----------

/** 승인 = 실행. 락 순서: accounts(②) -> 지급 행(③) -> 지갑(④). 작성자 본인은 승인할 수 없다(API + DB CHECK 이중 장치) */
export function approveGrant(admin: AdminCtx, ip: string, uuid: string, body: GrantActionBody): Promise<ActionResult> {
  const cfg = getConfig().pay.adminGrant;
  return runAdminAction({
    admin,
    ip,
    action: 'payment.grant.approve',
    targetType: 'star_grant',
    targetUuid: uuid,
    requestId: body.request_id,
    params: {},
    handler: async (client) => {
      const peek = await repo.grantByUuid(client, uuid);
      if (!peek) throw new AppError(404, '지급을 찾을 수 없습니다.', 'GRANT_NOT_FOUND');
      if (peek.created_by === admin.id) throw new AppError(403, '작성자는 승인할 수 없습니다.', 'GRANT_SELF_APPROVAL');
      await payRepo.lockAccount(client, peek.account_id);
      const g = (await repo.grantByUuid(client, uuid, true)) as GrantRow;
      const now = getNow();
      if (g.state !== 'pending') throw new AppError(409, '대기 중인 지급이 아닙니다.', 'GRANT_STATE');
      if (g.expires_at.getTime() <= now.getTime()) throw new AppError(409, '승인 기한이 지났습니다.', 'GRANT_STATE');
      let applied = 0;
      if (g.kind === 'grant') {
        const stars = g.stars as number;
        if (stars > cfg.maxStars) throw limitErr('max_stars', '1건 상한을 넘었습니다.');
        const since = new Date(resetBoundaries(now).dailyStartAt);
        const mine = await repo.grantedStarsSince(client, since, { createdBy: g.created_by, states: ['applied'], by: 'approved' });
        if (mine + stars > cfg.dailyPerAdmin) throw limitErr('daily_per_admin', '관리자별 일일 상한을 넘었습니다.');
        const total = await repo.grantedStarsSince(client, since, { states: ['applied'], by: 'approved' });
        if (total + stars > cfg.dailyTotal) throw limitErr('daily_total', '전체 일일 상한을 넘었습니다.');
        await lockWallet(client, g.account_id);
        // 무료분(환불 대상이 아니다). 부채가 있으면 입금 함수가 먼저 갚는다(보상을 주려면 먼저 탕감)
        await creditFree(client, { accountId: g.account_id, reason: 'admin_grant', amount: stars, ref: g.uuid, requestId: null });
        applied = stars;
      } else {
        applied = await forgiveDebt(client, { accountId: g.account_id, ref: g.uuid });
        if (applied <= 0) throw new AppError(409, '탕감할 부채가 없습니다.', 'GRANT_STATE');
      }
      await repo.markGrantApplied(client, g.id, admin.id, applied);
      const w = await readWallet(client, g.account_id);
      const fresh = (await repo.grantByUuid(client, uuid)) as GrantRow;
      return { status: 200, data: { grant: grantView(fresh), wallet: { balance: w.balance, paid_balance: w.paidBalance, free_balance: w.balance - w.paidBalance, debt: w.debt } } };
    },
  });
}

// ---------- PA12 ----------

export function cancelGrant(admin: AdminCtx, ip: string, uuid: string, body: GrantActionBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'payment.grant.cancel',
    targetType: 'star_grant',
    targetUuid: uuid,
    requestId: body.request_id,
    params: {},
    handler: async (client) => {
      const g = await repo.grantByUuid(client, uuid, true);
      if (!g) throw new AppError(404, '지급을 찾을 수 없습니다.', 'GRANT_NOT_FOUND');
      // 작성자 또는 owner
      if (g.created_by !== admin.id && admin.role !== 'owner') throw new AppError(403, '권한이 없습니다.', 'FORBIDDEN_ROLE');
      if (!(await repo.markGrantCancelled(client, g.id))) throw new AppError(409, '대기 중인 지급이 아닙니다.', 'GRANT_STATE');
      const fresh = (await repo.grantByUuid(client, uuid)) as GrantRow;
      return { status: 200, data: { grant: grantView(fresh) } };
    },
  });
}
