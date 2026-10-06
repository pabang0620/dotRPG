// 플레이어 결제 API B1~B6(Docs/server/phase11_payments.md 13.1). 클라이언트가 호출할 "지급" API는 없다:
// 별조각은 서버가 Steam에 직접 조회(QueryTxn)한 결과로만 지급된다(orderMachine). 금액·수량·통화·확률·시간은 요청에서 받지 않는다.
import { isIP } from 'node:net';
import { getConfig } from '../../config/env';
import { hashRequest } from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { getStarCatalog, productById, type StarProduct } from '../../gamedata/starProducts';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { getNow } from '../../utils/clock';
import { HOLD_MESSAGE } from '../antiabuse/holds';
import { readWallet } from '../starshop/starWallet';
import { advanceOrder } from './orderMachine';
import { formatPrice, orderView, type OrderView } from './orderView';
import * as repo from './paymentsRepository';
import type { OrderRow } from './paymentsRepository';
import { assertSpeed, assertWithinLimits, computeTier, FailBurst, limitsFor, RESTRICT_DAYS, type LimitView } from './payTier';
import { readQuote, signQuote } from './quote';
import { getSteamPartner, PartnerCallError, payBreakerRemainingSec, type UserInfo } from './steamPartner';

const ENDPOINT = 'POST /payments/orders';
const DAY_MS = 86_400_000;

function assertEnabled(): void {
  const c = getConfig().pay;
  if (!c.enabled || c.mode === 'off') throw new AppError(503, '지금은 결제할 수 없습니다.', 'FEATURE_DISABLED');
}

/** 7.7: 결제 회로가 열려 있는 동안 B1·B2는 Steam을 부르지 않고 503 */
function assertBreakerClosed(): void {
  const left = payBreakerRemainingSec();
  if (left > 0) throw steamUnavailable(new PartnerCallError('breaker_open', null, left));
}

function steamUnavailable(err: unknown): AppError {
  const retry = err instanceof PartnerCallError ? err.retryAfterSec : undefined;
  return new AppError(503, '지금은 결제할 수 없습니다.', 'STEAM_UNAVAILABLE', retry !== undefined ? { retry_after_sec: retry } : undefined);
}

// ---------------- 공통 검사 ----------------

async function requireSteamId(accountId: number): Promise<string> {
  const id = await repo.steamIdOf(getPool(), accountId);
  if (!id) throw new AppError(403, 'Steam 계정으로 로그인해야 결제할 수 있습니다.', 'NOT_STEAM_ACCOUNT');
  return id;
}

async function assertNotBlocked(accountId: number): Promise<void> {
  const p = await repo.profileOf(getPool(), accountId);
  if (p?.status === 'blocked') throw new AppError(403, '결제 이용이 제한되었습니다. 문의해 주세요.', 'PAYMENT_BLOCKED');
}

const userInfoCache = new Map<number, { at: number; info: UserInfo }>();
const USER_INFO_TTL_MS = 10 * 60_000;
/** 테스트 전용 */
export function clearUserInfoCache(): void {
  userInfoCache.clear();
}

/** Steam 지갑의 국가·통화(GetUserInfo). 계정별 10분 메모리 캐시. forceFresh 는 주문 생성(B2)에서 쓴다 */
async function userInfoOf(accountId: number, steamId: string, forceFresh: boolean): Promise<UserInfo> {
  const hit = userInfoCache.get(accountId);
  if (!forceFresh && hit && Date.now() - hit.at < USER_INFO_TTL_MS) return hit.info;
  try {
    const info = await getSteamPartner().getUserInfo(steamId);
    userInfoCache.set(accountId, { at: Date.now(), info });
    return info;
  } catch (err) {
    if (err instanceof PartnerCallError) throw steamUnavailable(err);
    throw err;
  }
}

function pricedProducts(currency: string): StarProduct[] {
  return getStarCatalog().products.filter((p) => p.enabled && p.prices[currency] !== undefined).sort((a, b) => a.sort - b.sort);
}

/** 내 열린 주문 한 개를 Steam 기준으로 대사한다. Steam에 닿지 못해도 던지지 않는다(상태 유지) */
async function reconcileOpen(accountId: number): Promise<OrderRow | null> {
  const open = await repo.openOrderOf(getPool(), accountId);
  if (!open) return null;
  const r = await advanceOrder(open.id, { actor: 'player', throttleSec: getConfig().pay.syncMinIntervalSeconds });
  return r.order;
}

/** 로그인 직후 호출: 열린 주문이 있으면 백그라운드로 대사한다. 실패해도 로그인에 영향이 없다 */
export function reconcileOpenOrderQuietly(accountId: number): void {
  if (getConfig().pay.mode === 'off') return;
  void (async () => {
    try {
      await reconcileOpen(accountId);
    } catch (err) {
      logger.warn({ err: err instanceof Error ? err.message : String(err) }, 'payment.login_reconcile_failed');
    }
  })();
}

// ---------------- B1 ----------------

export async function listProducts(accountId: number, accountUuid: string) {
  assertEnabled();
  assertBreakerClosed();
  const steamId = await requireSteamId(accountId);
  await assertNotBlocked(accountId);
  const now = getNow();
  const cfg = getConfig().pay;
  const db = getPool();
  const afterReconcile = await reconcileOpen(accountId);
  const open = afterReconcile && ['pending_init', 'created', 'authorized', 'finalized'].includes(afterReconcile.state) ? afterReconcile : null;
  const info = await userInfoOf(accountId, steamId, false);
  const catalog = getStarCatalog();
  const products = pricedProducts(info.currency);
  if (products.length === 0) throw new AppError(422, '이 지역의 결제 통화는 지원하지 않습니다.', 'CURRENCY_UNSUPPORTED');
  const t = await computeTier(db, accountId, now, info.country);
  const limits: LimitView = await limitsFor(db, accountId, t.tier, now);
  const exp = Math.floor(now.getTime() / 1000) + cfg.quoteTtlSeconds;
  const offers = open
    ? []
    : products.map((p) => {
        const price = p.prices[info.currency] as number;
        const block = limits.daily.used + p.stars > limits.daily.limit ? 'LIMIT_DAILY' : limits.monthly.used + p.stars > limits.monthly.limit ? 'LIMIT_MONTHLY' : null;
        return {
          product_id: p.id,
          name: p.name,
          stars: p.stars,
          display_price: formatPrice(price, info.currency),
          currency: info.currency,
          quote_id: signQuote({ a: accountUuid, p: p.id, c: info.currency, m: price, s: p.stars, v: catalog.version, e: exp }),
          quote_expires_at: new Date(exp * 1000).toISOString(),
          purchasable: block === null,
          block,
        };
      });
  return {
    server_time: now.toISOString(),
    tier: t.tier,
    country: info.country,
    currency: info.currency,
    open_order: open ? orderView(open) : null,
    limits: {
      daily: { limit: limits.daily.limit, used: limits.daily.used, left: limits.daily.left, resets_at: limits.daily.resets_at },
      monthly: { limit: limits.monthly.limit, used: limits.monthly.used, left: limits.monthly.left },
    },
    offers,
  };
}

// ---------------- B2 ----------------

export interface CreateOrderBody {
  request_id: string;
  product_id: string;
  quote_id: string;
}

export interface OrderResult {
  status: number;
  data: { order: OrderView; next: 'wait_steam_authorization' | 'sync' };
}

const nextOf = (o: OrderRow): 'wait_steam_authorization' | 'sync' => (o.state === 'created' ? 'wait_steam_authorization' : 'sync');

function replayOf(existing: OrderRow, hash: string): OrderResult {
  if (existing.request_hash !== hash) throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
  return { status: 200, data: { order: orderView(existing), next: nextOf(existing) } };
}

function cleanIp(ip: string | null): string | null {
  return ip && isIP(ip) ? ip : null;
}

export async function createOrder(accountId: number, accountUuid: string, body: CreateOrderBody, meta: { ip: string | null }): Promise<OrderResult> {
  const cfg = getConfig();
  const pay = cfg.pay;
  const db = getPool();
  const hash = hashRequest({ endpoint: ENDPOINT, product_id: body.product_id, quote_id: body.quote_id });
  // 0. 같은 request_id: 현재 상태를 돌려주고 끝(크래시 뒤 재시도가 만료된 견적 때문에 실패하지 않게 가장 먼저)
  const prior = await repo.orderByRequest(db, accountId, body.request_id);
  if (prior) return replayOf(prior, hash);
  // 1~6
  assertEnabled();
  assertBreakerClosed();
  const steamId = await requireSteamId(accountId);
  await assertNotBlocked(accountId);
  const hold = await repo.accountByActiveHold(db, accountId);
  if (hold) throw new AppError(403, HOLD_MESSAGE, 'ECONOMY_HOLD', { scope: hold.character_id === null ? 'account' : 'character', since: hold.created_at.toISOString() });
  const product = productById(body.product_id);
  if (!product || !product.enabled) throw new AppError(404, '판매 중인 상품이 아닙니다.', 'PRODUCT_NOT_FOUND');
  // 7. 국가·통화
  const info = await userInfoOf(accountId, steamId, true);
  const price = product.prices[info.currency];
  if (price === undefined) throw new AppError(422, '이 지역의 결제 통화는 지원하지 않습니다.', 'CURRENCY_UNSUPPORTED');
  // 8. 견적: 서명 -> 만료 -> 서버가 지금 다시 계산한 값과 같은가
  const q = readQuote(body.quote_id);
  if (!q || q.a !== accountUuid) throw new AppError(409, '견적이 바뀌었습니다. 다시 확인해 주세요.', 'QUOTE_CHANGED');
  const now = getNow();
  if (q.e * 1000 < now.getTime()) throw new AppError(410, '견적이 만료되었습니다. 다시 확인해 주세요.', 'QUOTE_EXPIRED');
  if (q.p !== product.id || q.c !== info.currency || q.m !== price || q.s !== product.stars || q.v !== getStarCatalog().version) {
    throw new AppError(409, '견적이 바뀌었습니다. 다시 확인해 주세요.', 'QUOTE_CHANGED');
  }
  // 트랜잭션 1: accounts 행 잠금(동시 주문 직렬화) -> 열린 주문·속도·한도 -> pending_init 주문 커밋
  let created: OrderRow;
  try {
    created = await withTransaction(async (client) => {
      if (!(await repo.lockAccount(client, accountId))) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
      const dup = await repo.orderByRequest(client, accountId, body.request_id);
      if (dup) throw new ReplayFound(dup);
      const open = await repo.openOrderOf(client, accountId);
      if (open) throw new AppError(409, '이전 결제를 확인하는 중입니다.', 'ORDER_IN_PROGRESS', { order_id: open.uuid });
      await assertSpeed(client, accountId, now);
      const t = await computeTier(client, accountId, now, info.country);
      const limits = await limitsFor(client, accountId, t.tier, now);
      assertWithinLimits(limits, product.stars);
      for (const s of t.signals) {
        await repo.insertFlag(client, { accountId, orderId: null, kind: s.kind, severity: s.severity, detail: s.detail, dedupeMinutes: 1440 });
        if (s.raisesFloor) await repo.raiseTierFloor(client, accountId, new Date(now.getTime() + RESTRICT_DAYS * DAY_MS));
      }
      const acct = await repo.accountInfo(client, accountId);
      await repo.noteCountry(client, accountId, info.country, info.currency);
      const row = await repo.insertOrder(client, {
        accountId,
        requestId: body.request_id,
        requestHash: hash,
        steamId,
        appId: cfg.steam.appId ?? 480,
        productId: product.id,
        steamItemId: product.steam_item_id,
        catalogVersion: getStarCatalog().version,
        stars: product.stars,
        currency: info.currency,
        amountMinor: price,
        steamCountry: info.country,
        tier: t.tier,
        ip: cleanIp(meta.ip),
        deviceHash: acct?.active_device_hash ?? null,
        expiresAt: new Date(now.getTime() + pay.orderExpireMinutes * 60_000),
        nextCheckAt: new Date(now.getTime() + 30_000),
      });
      await repo.insertEvent(client, row.id, { kind: 'created', to: 'pending_init', actor: 'player', detail: { stars: row.stars, currency: row.currency, tier: row.tier } });
      return row;
    });
  } catch (err) {
    if (err instanceof ReplayFound) return replayOf(err.order, hash);
    if (isUniqueViolation(err, 'star_orders_account_id_request_id_key')) {
      const dup = await repo.orderByRequest(db, accountId, body.request_id);
      if (dup) return replayOf(dup, hash);
    }
    if (isUniqueViolation(err, 'star_orders_one_open')) {
      const open = await repo.openOrderOf(db, accountId);
      throw new AppError(409, '이전 결제를 확인하는 중입니다.', 'ORDER_IN_PROGRESS', open ? { order_id: open.uuid } : undefined);
    }
    // 거절은 롤백되므로 신호(속도·한도 반복)는 풀에서 따로 남긴다
    if (err instanceof AppError) await noteRejection(accountId, err);
    if (err instanceof FailBurst) throw new AppError(429, err.message, 'ORDER_TOO_FAST', err.extra);
    throw err;
  }
  return initSteam(created, product, info, body);
}

class ReplayFound extends Error {
  constructor(readonly order: OrderRow) {
    super('replay');
  }
}

async function noteRejection(accountId: number, err: AppError): Promise<void> {
  try {
    if (err instanceof FailBurst) {
      await repo.insertFlag(getPool(), { accountId, orderId: null, kind: 'fail_burst', severity: 2, detail: {}, dedupeMinutes: 60 });
    } else if (err.code === 'ORDER_TOO_FAST') {
      await repo.insertFlag(getPool(), { accountId, orderId: null, kind: 'rapid_orders', severity: 1, detail: {}, dedupeMinutes: 10 });
    } else if (err.code === 'PAY_LIMIT_EXCEEDED') {
      await repo.insertFlag(getPool(), { accountId, orderId: null, kind: 'limit_exceeded', severity: 1, detail: { scope: err.extra?.scope ?? null }, dedupeMinutes: 0 });
    }
  } catch {
    // 신호 기록 실패가 원래 오류 응답을 가리지 않는다
  }
}

/** 6.2-14·15, 6.3: 트랜잭션 밖에서 InitTxn 한 번(재시도 없음). 결과가 불명확하면 pending_init 으로 두고 sync가 판별한다 */
async function initSteam(order: OrderRow, product: StarProduct, info: UserInfo, body: CreateOrderBody): Promise<OrderResult> {
  const db = getPool();
  const token = order.lease_token as string;
  void body;
  let result: Awaited<ReturnType<ReturnType<typeof getSteamPartner>['initTxn']>> | null = null;
  let notSent = false;
  try {
    result = await getSteamPartner().initTxn({
      orderId: order.steam_order_id,
      steamId: order.steam_id,
      appId: order.app_id,
      itemId: product.steam_item_id,
      itemName: product.name,
      amountMinor: order.amount_minor,
      currency: info.currency,
      language: getConfig().pay.language,
    });
  } catch (err) {
    if (!(err instanceof PartnerCallError)) throw err;
    notSent = err.kind === 'breaker_open';
  }
  const now = getNow();
  await withTransaction(async (client) => {
    if (result && result.ok) {
      const ok = await repo.updateFenced(client, order.id, token, 'pending_init', {
        state: 'created',
        init_at: new Date(),
        ...(result.transId && /^\d+$/.test(result.transId) ? { steam_trans_id: result.transId } : {}),
        next_check_at: new Date(now.getTime() + 30_000),
        last_checked_at: new Date(),
      });
      if (ok) await repo.insertEvent(client, order.id, { kind: 'init_ok', from: 'pending_init', to: 'created', actor: 'player' });
    } else if (result && !result.ok) {
      const ok = await repo.updateFenced(client, order.id, token, 'pending_init', { state: 'failed', fail_reason: 'init_rejected', closed_at: new Date(), next_check_at: null });
      if (ok) await repo.insertEvent(client, order.id, { kind: 'init_failed', from: 'pending_init', to: 'failed', actor: 'player', detail: { error_code: result.errorCode.slice(0, 40) } });
    } else if (notSent) {
      // 회로 차단으로 Steam에 보내지 않았다: 청구 없음
      const ok = await repo.updateFenced(client, order.id, token, 'pending_init', { state: 'failed', fail_reason: 'init_lost', closed_at: new Date(), next_check_at: null });
      if (ok) await repo.insertEvent(client, order.id, { kind: 'init_failed', from: 'pending_init', to: 'failed', actor: 'player', detail: { reason: 'breaker_open' } });
    } else {
      const ok = await repo.updateFenced(client, order.id, token, 'pending_init', { next_check_at: new Date(now.getTime() + 30_000), last_checked_at: new Date() });
      if (ok) await repo.insertEvent(client, order.id, { kind: 'init_unknown', from: 'pending_init', to: 'pending_init', actor: 'player' });
    }
  });
  await repo.releaseLease(db, order.id, token);
  const fresh = (await repo.orderById(db, order.id)) as OrderRow;
  if (notSent) throw new AppError(503, '지금은 결제할 수 없습니다.', 'STEAM_UNAVAILABLE');
  return { status: 201, data: { order: orderView(fresh), next: nextOf(fresh) } };
}

// ---------------- B3~B6 ----------------

const NOT_FOUND = () => new AppError(404, '주문을 찾을 수 없습니다.', 'ORDER_NOT_FOUND');

export async function syncOrder(accountId: number, uuid: string) {
  const db = getPool();
  const o = await repo.ownedOrderByUuid(db, accountId, uuid);
  if (!o) throw NOT_FOUND();
  const r = await advanceOrder(o.id, { actor: 'player', throttleSec: getConfig().pay.syncMinIntervalSeconds });
  const view = orderView(r.order);
  if (r.steamError && !view.final) throw steamUnavailable(r.steamError);
  let wallet: { balance: number; paid_balance: number; free_balance: number; debt: number } | null = null;
  if (r.order.state === 'granted') {
    const w = await readWallet(db, accountId);
    wallet = { balance: w.balance, paid_balance: w.paidBalance, free_balance: w.balance - w.paidBalance, debt: w.debt };
  }
  return { order: view, wallet };
}

export async function getOrder(accountId: number, uuid: string) {
  const o = await repo.ownedOrderByUuid(getPool(), accountId, uuid);
  if (!o) throw NOT_FOUND();
  return { order: orderView(o) };
}

export async function listOrders(accountId: number, limit: number, cursor: string | undefined) {
  const before = cursor ? decodeCursor(cursor) : null;
  const rows = await repo.listOrders(getPool(), accountId, before, limit + 1);
  const page = rows.slice(0, limit);
  const last = page[page.length - 1];
  return { items: page.map(orderView), next_cursor: rows.length > limit && last ? Buffer.from(String(last.id)).toString('base64url') : null };
}

function decodeCursor(c: string): number {
  const n = Number(Buffer.from(c, 'base64url').toString('utf8'));
  if (!Number.isInteger(n) || n <= 0) throw new AppError(400, '입력값이 올바르지 않습니다.', 'VALIDATION', { fields: [{ path: 'cursor', message: '올바르지 않은 값' }] });
  return n;
}

export async function reconcile(accountId: number) {
  const db = getPool();
  const open = await repo.openOrderOf(db, accountId);
  if (!open) return { resolved: [] as OrderView[], open_order: null };
  const r = await advanceOrder(open.id, { actor: 'player', throttleSec: getConfig().pay.syncMinIntervalSeconds });
  const view = orderView(r.order);
  return view.final ? { resolved: [view], open_order: null } : { resolved: [] as OrderView[], open_order: view };
}
