// 별빛 정기권(Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md B): ₩4,400 결제 = 별조각 300 즉시 + 30일 동안 매일 첫 확인 때
// 별조각 30(무료 입금, 원장 pass_daily)과 봉인된 상자 1개(시스템 우편). 기간 지급·회수는 결제 처리(orderMachine)가 부른다.
import type { PoolClient } from 'pg';
import { getPool, type Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { getStarCatalog, productById, type StarProduct } from '../../gamedata/starProducts';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { assertNoHold } from '../antiabuse/holds';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { createSystemMail } from '../mail/mailRepository';
import type { OrderRow } from '../payments/paymentsRepository';
import * as wallets from '../starshop/starWallet';
import * as tickets from '../sweep/ticketWallet';
import * as repo from './starPassRepository';

export const PASS_DAILY_STARS = 30;
export const PASS_DAILY_ITEM = { itemKey: 'box_sealed', count: 1 } as const;

const passProduct = (): StarProduct | undefined => getStarCatalog().products.find((p) => p.pass_days !== undefined);

function daysLeft(expires: Date | null, now: Date): number {
  if (!expires || expires.getTime() <= now.getTime()) return 0;
  return Math.ceil((expires.getTime() - now.getTime()) / repo.DAY_MS);
}

/** 결제 전 확인: 이번 구매로 더해질 기간이 상한(60일)에 잘리면 사지 못하게 한다(남은 기간 + 상품 일수 <= 60일) */
export async function assertPassPurchasable(db: Queryable, accountId: number, product: StarProduct, now: Date): Promise<void> {
  if (product.pass_days === undefined) return;
  const exp = await repo.expiresAt(db, accountId);
  const remaining = exp ? Math.max(0, exp.getTime() - now.getTime()) : 0;
  if (remaining + product.pass_days * repo.DAY_MS > repo.MAX_REMAINING_MS) {
    throw new AppError(409, '정기권 기간이 충분히 남아 있습니다. 남은 기간이 30일 이하일 때 다시 구매할 수 있습니다.', 'PASS_TOO_LONG', { days_left: daysLeft(exp, now) });
  }
}

/** 상품 목록의 구매 가능 여부(같은 규칙, 막히면 PASS_TOO_LONG) */
export async function passBlock(db: Queryable, accountId: number, product: StarProduct, now: Date): Promise<'PASS_TOO_LONG' | null> {
  try {
    await assertPassPurchasable(db, accountId, product, now);
    return null;
  } catch (err) {
    if (err instanceof AppError && err.code === 'PASS_TOO_LONG') return 'PASS_TOO_LONG';
    throw err;
  }
}

/** 결제 지급 트랜잭션 안(주문·지갑 잠금 뒤): 정기권 상품이면 기간을 더한다. 주문당 1회(star_pass_grants) */
export async function grantPassForOrder(client: PoolClient, o: OrderRow, now: Date): Promise<void> {
  const product = productById(o.product_id);
  if (!product || product.pass_days === undefined) return;
  if (await repo.grantOf(client, o.id, 'grant')) return;
  const cur = await repo.lockExpiresAt(client, o.account_id);
  const base = cur && cur.getTime() > now.getTime() ? cur.getTime() : now.getTime();
  const next = Math.min(base + product.pass_days * repo.DAY_MS, now.getTime() + repo.MAX_REMAINING_MS);
  const added = Math.max(0, next - base);
  const at = new Date(Math.max(next, base));
  await repo.setExpiresAt(client, o.account_id, at);
  await repo.insertGrant(client, { accountId: o.account_id, orderId: o.id, kind: 'grant', seconds: Math.round(added / 1000), expiresAfter: at });
}

/**
 * 환불·차지백 트랜잭션 안(기간을 뺀 뒤): 그 주문 기간에 이미 받은 일일 지급을 되돌린다.
 * 무료 별조각은 부채 방식으로 걷고(쓴 몫은 부채), 받지 않은 상자 우편은 폐기한다. 이미 받아 간 상자는 owner 회수 도구(gear)가 처리한다.
 * 돌려주는 stars 는 환불 남용 판정에서 "이미 받은 몫"으로 센다
 */
export async function reclaimDailiesForOrder(client: PoolClient, o: OrderRow): Promise<{ days: number; stars: number; mailsExpired: number }> {
  const none = { days: 0, stars: 0, mailsExpired: 0 };
  const g = await repo.grantOf(client, o.id, 'grant');
  if (!g || g.createdAt === null || g.expiresAfter === null) return none;
  const rows = await repo.unreclaimedDailies(client, o.account_id, g.createdAt, g.expiresAfter);
  if (rows.length === 0) return none;
  const stars = rows.reduce((a, r) => a + r.stars, 0);
  await wallets.reverseFree(client, { accountId: o.account_id, amount: stars, ref: o.uuid });
  const mailsExpired = await repo.expireUnclaimedMails(client, rows.flatMap((r) => (r.mail_id === null ? [] : [r.mail_id])), getNow());
  await repo.markReclaimed(client, o.account_id, rows.map((r) => r.day_start), o.id);
  return { days: rows.length, stars, mailsExpired };
}

/** 환불·차지백 트랜잭션 안: 그 주문이 더한 기간을 뺀다(지금보다 앞으로는 당기지 않는다). 일일 지급 회수는 reclaimDailiesForOrder */
export async function revokePassForOrder(client: PoolClient, o: OrderRow, now: Date): Promise<void> {
  const g = await repo.grantOf(client, o.id, 'grant');
  if (!g || (await repo.grantOf(client, o.id, 'revoke'))) return;
  const cur = await repo.lockExpiresAt(client, o.account_id);
  if (!cur) return;
  const at = new Date(Math.max(now.getTime(), cur.getTime() - g.seconds * 1000));
  const removed = Math.max(0, cur.getTime() - at.getTime());
  await repo.setExpiresAt(client, o.account_id, at);
  await repo.insertGrant(client, { accountId: o.account_id, orderId: o.id, kind: 'revoke', seconds: Math.round(removed / 1000), expiresAfter: at });
}

export interface PassStatus {
  active: boolean;
  expires_at: string | null;
  days_left: number;
  today_claimed: boolean;
  next_daily_at: string;
  daily: { stars: number; item_key: string; count: number };
  product: { product_id: string; name: string; stars: number; pass_days: number } | null;
}

async function statusOf(db: Queryable, accountId: number, now: Date): Promise<PassStatus> {
  const exp = await repo.expiresAt(db, accountId);
  const rb = resetBoundaries(now);
  const claimed = (await repo.dailyOf(db, accountId, new Date(rb.dailyStartAt))) !== null;
  const p = passProduct();
  return {
    active: exp !== null && exp.getTime() > now.getTime(),
    expires_at: exp ? exp.toISOString() : null,
    days_left: daysLeft(exp, now),
    today_claimed: claimed,
    next_daily_at: rb.nextDailyAt,
    daily: { stars: PASS_DAILY_STARS, item_key: PASS_DAILY_ITEM.itemKey, count: PASS_DAILY_ITEM.count },
    product: p ? { product_id: p.id, name: p.name, stars: p.stars, pass_days: p.pass_days as number } : null,
  };
}

/** GET /star-pass */
export function status(accountId: number): Promise<PassStatus> {
  return statusOf(getPool(), accountId, getNow());
}

/**
 * POST /characters/:uuid/star-pass/daily: 그날(06:00 KST 구간) 첫 확인 때 한 번. 별조각은 계정 지갑, 상자는 이 캐릭터에게 우편.
 * 락 순서: 캐릭터(runEconomy) -> 계정(lockAccount) -> 지갑(creditFree). 결제 지급(지갑 -> 정기권 행)과 정기권 행을 잠그지 않아 순환이 없다.
 */
export function claimDaily(accountId: number, characterUuid: string, body: { request_id: string }): Promise<StoredResult> {
  const { request_id: requestId } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/star-pass/daily',
    requestId,
    payload: {},
    handler: async (ctx) => {
      await assertNoHold(ctx.client, accountId, ctx.char.id);
      await tickets.lockAccount(ctx.client, accountId);
      const now = ctx.now;
      const exp = await repo.expiresAt(ctx.client, accountId);
      if (!exp || exp.getTime() <= now.getTime()) throw new AppError(409, '별빛 정기권이 없습니다.', 'PASS_REQUIRED');
      const dayStart = new Date(resetBoundaries(now).dailyStartAt);
      const before = await repo.dailyOf(ctx.client, accountId, dayStart);
      if (before) {
        const s = await statusOf(ctx.client, accountId, now);
        return { status: 200, data: { granted: false, stars: 0, item: null, pass: s, balance: (await wallets.readWallet(ctx.client, accountId)).balance } };
      }
      const w = await wallets.creditFree(ctx.client, { accountId, reason: 'pass_daily', amount: PASS_DAILY_STARS, ref: `pass_daily:${dayStart.toISOString()}`, requestId });
      const mail = await createSystemMail(ctx.client, {
        characterId: ctx.char.id,
        systemCode: 'attendance',
        gold: 0,
        item: { itemKey: PASS_DAILY_ITEM.itemKey, count: PASS_DAILY_ITEM.count, bind: 'account' },
        now,
        expiresAt: new Date(now.getTime() + getGameData().auction.mailDays * repo.DAY_MS),
        requestId,
      });
      await repo.insertDaily(ctx.client, { accountId, dayStart, characterId: ctx.char.id, mailId: mail.id, stars: PASS_DAILY_STARS, requestId });
      const s = await statusOf(ctx.client, accountId, now);
      return {
        status: 200,
        data: { granted: true, stars: PASS_DAILY_STARS, item: { item_key: PASS_DAILY_ITEM.itemKey, count: PASS_DAILY_ITEM.count, mail_id: mail.uuid }, pass: s, balance: w.balance },
      };
    },
  });
}
