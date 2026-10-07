// 11단계(결제) 테스트 공용: mock Steam, Steam 계정 로그인, 구매 흐름 도우미
import { randomBytes, randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { orderView } from '../src/domains/payments/orderView';
import { clearUserInfoCache } from '../src/domains/payments/paymentsService';
import { resetPayPartnerStats, setSteamPartner } from '../src/domains/payments/steamPartner';
import { getMockPartner } from '../src/domains/payments/steamPartnerHttp';
import { buildApp, ver } from './helpers';

export const mock = getMockPartner();
void orderView;

export const PAY_ENV: Record<string, string> = {
  PAYMENTS_ENABLED: 'true',
  PAYMENTS_STEAM_MODE: 'mock',
  PAYMENT_QUOTE_SECRET: 'quote-secret-quote-secret-quote-secret-0123',
  STAR_RATES_ACK_REQUIRED: 'false',
  PAY_MIN_ORDER_GAP_SECONDS: '0',
  PAY_MAX_ORDERS_PER_HOUR: '1000',
  PAY_SYNC_MIN_INTERVAL_SECONDS: '0',
  PAY_FAIL_COOLDOWN_THRESHOLD: '1000',
  STEAM_APP_ID: '480',
};

export function payApp(over: Record<string, string> = {}): Express {
  mock.reset();
  setSteamPartner(null);
  resetPayPartnerStats();
  clearUserInfoCache();
  return buildApp({ ...PAY_ENV, ...over });
}

export function resetPay(): void {
  mock.reset();
  setSteamPartner(null);
  resetPayPartnerStats();
  clearUserInfoCache();
}

export interface Payer {
  access: string;
  accountUuid: string;
  accountId: number;
  steamId: string;
}

export const newSteamId = (): string => `7656119${Math.floor(Math.random() * 1e10).toString().padStart(10, '0')}`;

export async function newPayer(app: Express): Promise<Payer> {
  const steamId = newSteamId();
  const res = await request(app).post('/auth/steam').set(ver()).send({ ticket: `mock:${steamId}:${randomBytes(8).toString('hex')}` });
  if (res.status !== 201) throw new Error(`steam login failed ${res.status} ${JSON.stringify(res.body)}`);
  const uuid = res.body.data.account.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [uuid]);
  return { access: res.body.data.access_token as string, accountUuid: uuid, accountId: Number((r.rows[0] as { id: string }).id), steamId };
}

export const authP = (p: Pick<Payer, 'access'>): Record<string, string> => ver({ Authorization: `Bearer ${p.access}` });

export const products = (app: Express, p: Payer) => request(app).get('/payments/products').set(authP(p));

export interface Offer {
  product_id: string;
  stars: number;
  quote_id: string;
}

export async function offerOf(app: Express, p: Payer, productId = 'stars_300'): Promise<Offer> {
  const res = await products(app, p);
  if (res.status !== 200) throw new Error(`products ${res.status} ${JSON.stringify(res.body)}`);
  const o = (res.body.data.offers as Offer[]).find((x) => x.product_id === productId);
  if (!o) throw new Error(`offer ${productId} not found ${JSON.stringify(res.body.data)}`);
  return o;
}

export const order = (app: Express, p: Payer, body: Record<string, unknown>) => request(app).post('/payments/orders').set(authP(p)).send(body);

/** B1(견적) -> B2(주문). 성공하면 supertest 응답을 돌려준다 */
export async function buy(app: Express, p: Payer, productId = 'stars_300', requestId: string = randomUUID()) {
  const o = await offerOf(app, p, productId);
  return order(app, p, { request_id: requestId, product_id: productId, quote_id: o.quote_id });
}

export const sync = (app: Express, p: Payer, orderUuid: string) => request(app).post(`/payments/orders/${orderUuid}/sync`).set(authP(p)).send({});
export const getOrderApi = (app: Express, p: Payer, orderUuid: string) => request(app).get(`/payments/orders/${orderUuid}`).set(authP(p));

/** 이용자가 오버레이에서 승인한 것으로 하고 sync(콜백 직후)를 부른다 */
export async function approveAndSync(app: Express, p: Payer, o: { id: string; steam_order_id: string }) {
  mock.approve(o.steam_order_id);
  return sync(app, p, o.id);
}

/** 구매를 끝까지(승인 -> sync) 진행해 granted 로 만든다 */
export async function purchase(app: Express, p: Payer, productId = 'stars_300'): Promise<{ id: string; steam_order_id: string }> {
  const res = await buy(app, p, productId);
  if (res.status !== 201) throw new Error(`buy ${res.status} ${JSON.stringify(res.body)}`);
  const o = res.body.data.order as { id: string; steam_order_id: string };
  const s = await approveAndSync(app, p, o);
  if (s.body.data?.order?.state !== 'granted') throw new Error(`not granted ${JSON.stringify(s.body)}`);
  return o;
}

export interface WalletState {
  balance: number;
  paid: number;
  debt: number;
  ledger: number;
  lots: number;
}

export async function walletState(accountId: number): Promise<WalletState> {
  const db = getPool();
  const w = await db.query<{ balance: string; paid_balance: string; debt: string }>('SELECT balance, paid_balance, debt FROM star_wallets WHERE account_id = $1', [accountId]);
  const l = await db.query<{ n: string }>('SELECT count(*) AS n FROM star_ledger WHERE account_id = $1', [accountId]);
  const lots = await db.query<{ s: string }>('SELECT coalesce(sum(remaining), 0) AS s FROM star_paid_lots WHERE account_id = $1', [accountId]);
  const r = w.rows[0];
  return { balance: Number(r?.balance ?? 0), paid: Number(r?.paid_balance ?? 0), debt: Number(r?.debt ?? 0), ledger: Number((l.rows[0] as { n: string }).n), lots: Number((lots.rows[0] as { s: string }).s) };
}

export async function ledgerCount(accountId: number, reason: string): Promise<number> {
  const r = await getPool().query<{ n: string }>('SELECT count(*) AS n FROM star_ledger WHERE account_id = $1 AND reason = $2', [accountId, reason]);
  return Number((r.rows[0] as { n: string }).n);
}

export async function orderRow(uuid: string): Promise<Record<string, unknown>> {
  return (await getPool().query('SELECT * FROM star_orders WHERE uuid = $1', [uuid])).rows[0] as Record<string, unknown>;
}

/** 시험 준비용: 기존 원장 규칙을 지키며 별조각을 넣는다(test_grant, DB 세션 설정을 켠 트랜잭션) */
export async function seedStars(accountId: number, amount: number): Promise<void> {
  const { creditFree } = await import('../src/domains/starshop/starWallet');
  const { withTransaction } = await import('../src/db/pool');
  await withTransaction(async (c) => {
    await c.query("SET LOCAL dotrpg.allow_test_grant = 'on'");
    await creditFree(c, { accountId, reason: 'test_grant', amount, ref: 'test', requestId: null });
  });
}

/** Steam 계정의 캐릭터 한 명(별조각 소비 API는 캐릭터 경로 아래다) */
export async function heroOf(app: Express, p: Payer): Promise<import('./economyHelpers').Hero> {
  const { createChar, randomName } = await import('./helpers');
  const res = await createChar(app, { access: p.access } as never, randomName());
  if (res.status !== 201) throw new Error(`createChar failed ${res.status} ${JSON.stringify(res.body)}`);
  const id = res.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { s: { access: p.access } as never, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' };
}

/** 지갑·원장 정합성: balance = SUM(delta) = 마지막 balance_after, paid = SUM(paid_delta) = 로트 합, debt = SUM(debt_delta) */
export async function expectWalletConsistent(accountId: number): Promise<void> {
  const r = await getPool().query<{ ok: boolean; s: string; ps: string; ds: string; w: string; wp: string; wd: string; lots: string }>(
    `SELECT true AS ok, coalesce(sum(l.delta), 0) AS s, coalesce(sum(l.paid_delta), 0) AS ps, coalesce(sum(l.debt_delta), 0) AS ds,
            (SELECT balance FROM star_wallets WHERE account_id = $1) AS w, (SELECT paid_balance FROM star_wallets WHERE account_id = $1) AS wp,
            (SELECT debt FROM star_wallets WHERE account_id = $1) AS wd,
            (SELECT coalesce(sum(remaining), 0) FROM star_paid_lots WHERE account_id = $1) AS lots
       FROM star_ledger l WHERE l.account_id = $1`,
    [accountId],
  );
  const x = r.rows[0] as { s: string; ps: string; ds: string; w: string; wp: string; wd: string; lots: string };
  expect({ balance: Number(x.w), paid: Number(x.wp), debt: Number(x.wd) }).toEqual({ balance: Number(x.s), paid: Number(x.ps), debt: Number(x.ds) });
  expect(Number(x.wp)).toBe(Number(x.lots));
}
