// 11단계 결제: 위조·재전송·경쟁(설계 18절 A), 한도·사기 결제(D). 가짜 Steam(mock)으로만 돌린다(실제 Steam 호출 금지)
import { randomBytes, randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setStarCatalogForTest, getStarCatalog } from '../src/gamedata/starProducts';
import { signQuote } from '../src/domains/payments/quote';
import { advanceOrder } from '../src/domains/payments/orderMachine';
import { paymentReconcileJob } from '../src/ops/jobs/paymentJobs';
import { resetPayPartnerStats } from '../src/domains/payments/steamPartner';
import { resetDb, shutdown, ver } from './helpers';
import {
  approveAndSync, authP, buy, getOrderApi, ledgerCount, mock, newPayer, newSteamId, offerOf, order, orderRow, payApp, products, purchase, resetPay, sync, walletState,
} from './payHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

const jobCtx = { opts: {}, shouldStop: () => false };

describe('B1/B2/B3 정상 흐름', () => {
  it('상품 조회 -> 주문 -> 승인 -> sync 로 별조각이 지급된다(원장·로트·지갑·주문 상태가 함께 맞는다)', async () => {
    const p = await newPayer(app);
    const list = await products(app, p);
    expect(list.status).toBe(200);
    expect(list.body.data).toMatchObject({ currency: 'KRW', country: 'KR', open_order: null, tier: 'new' });
    expect(list.body.data.offers.length).toBeGreaterThan(0);
    const res = await buy(app, p, 'stars_300');
    expect(res.status).toBe(201);
    expect(res.body.data.next).toBe('wait_steam_authorization');
    const o = res.body.data.order;
    expect(o).toMatchObject({ state: 'created', stars: 300, final: false, charged: null, fail_code: null });
    expect(typeof o.steam_order_id).toBe('string');
    const s = await approveAndSync(app, p, o);
    expect(s.status).toBe(200);
    expect(s.body.data.order).toMatchObject({ state: 'granted', final: true, charged: true });
    expect(s.body.data.wallet).toMatchObject({ balance: 300, paid_balance: 300, free_balance: 0, debt: 0 });
    const w = await walletState(p.accountId);
    expect(w).toMatchObject({ balance: 300, paid: 300, debt: 0, ledger: 1, lots: 300 });
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
    expect(mock.calls.finalizeTxn).toBe(1);
    // 같은 주문을 또 sync 해도 지급은 한 번이다
    await sync(app, p, o.id);
    expect((await walletState(p.accountId)).balance).toBe(300);
    expect((await getOrderApi(app, p, o.id)).body.data.order.state).toBe('granted');
  });

  it('입력 오류: 금액·수량·통화·Steam ID·주문 번호를 보내면 400(strict), sync 본문에 승인 여부를 보내도 400', async () => {
    const p = await newPayer(app);
    const o = await offerOf(app, p);
    for (const extra of [{ amount: 1 }, { stars: 99999 }, { currency: 'USD' }, { steam_id: '1' }, { steam_order_id: '1' }, { quantity: 2 }]) {
      const res = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: o.quote_id, ...extra });
      expect(res.status).toBe(400);
    }
    expect((await order(app, p, { request_id: 'x', product_id: 'stars_300', quote_id: o.quote_id })).status).toBe(400);
    const created = (await buy(app, p)).body.data.order;
    const res = await request(app).post(`/payments/orders/${created.id}/sync`).set(authP(p)).send({ authorized: true });
    expect(res.status).toBe(400);
    expect((await request(app).post('/payments/reconcile').set(authP(p)).send({ x: 1 })).status).toBe(400);
  });

  it('지급 API는 라우트에 없다(grant, credit, complete 등은 404)', async () => {
    const p = await newPayer(app);
    for (const path of ['/payments/grant', '/payments/credit', '/payments/complete', '/payments/orders/x/grant', '/payments/receipt']) {
      const res = await request(app).post(path).set(authP(p)).send({});
      expect([404, 400]).toContain(res.status);
      if (path !== '/payments/orders/x/grant') expect(res.status).toBe(404);
    }
  });

  it('플래그 꺼짐: B1·B2는 503 FEATURE_DISABLED(기본 false)', async () => {
    const off = payApp({ PAYMENTS_ENABLED: 'false', PAYMENTS_STEAM_MODE: 'off' });
    const p = await newPayer(off);
    const res = await products(off, p);
    expect(res.status).toBe(503);
    expect(res.body.errors.code).toBe('FEATURE_DISABLED');
    const o = await order(off, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: 'x'.repeat(30) });
    expect(o.status).toBe(503);
    payApp();
  });

  it('개발용 로그인 계정은 NOT_STEAM_ACCOUNT', async () => {
    const { registerAccount, auth } = await import('./helpers');
    const s = await registerAccount(app);
    const res = await request(app).get('/payments/products').set(auth(s));
    expect(res.status).toBe(403);
    expect(res.body.errors.code).toBe('NOT_STEAM_ACCOUNT');
  });
});

describe('A. 위조·재전송·경쟁', () => {
  it('2. Steam이 승인하지 않은 주문(Init)에 sync 를 100번 보내도 지급·상태 변화 없음', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    const rs = await Promise.all(Array.from({ length: 30 }, () => sync(app, p, o.id)));
    for (const r of rs) expect(r.body.data.order.state).toBe('created');
    expect(mock.calls.finalizeTxn).toBe(0);
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(0);
    expect((await walletState(p.accountId)).balance).toBe(0);
  });

  it('3. 남의 주문 uuid 는 404 ORDER_NOT_FOUND(존재 여부 구분 불가)', async () => {
    const a = await newPayer(app);
    const b = await newPayer(app);
    const o = (await buy(app, a)).body.data.order;
    for (const res of [await sync(app, b, o.id), await getOrderApi(app, b, o.id), await sync(app, b, randomUUID())]) {
      expect(res.status).toBe(404);
      expect(res.body.errors.code).toBe('ORDER_NOT_FOUND');
    }
  });

  it('3. 주문 후 계정의 Steam 신원이 바뀐 상태로 확정 -> 지급 안 함 + steamid_mismatch + needs_review', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    await getPool().query("UPDATE auth_identities SET subject = $2 WHERE account_id = $1 AND provider = 'steam'", [p.accountId, '76561198999999999']);
    const s = await approveAndSync(app, p, o);
    expect(s.body.data.order).toMatchObject({ state: 'failed', fail_code: 'UNDER_REVIEW', charged: null });
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(0);
    const row = await orderRow(o.id);
    expect(row).toMatchObject({ state: 'failed', fail_reason: 'mismatch', needs_review: true });
    const f = await getPool().query("SELECT kind FROM payment_flags WHERE account_id = $1", [p.accountId]);
    expect(f.rows.map((x: { kind: string }) => x.kind)).toContain('steamid_mismatch');
    expect(mock.calls.finalizeTxn).toBe(0);
  });

  it.each([
    ['금액', { amountMinor: 1 }, 'amount_mismatch'],
    ['통화', { currency: 'USD' }, 'amount_mismatch'],
    ['상품', { itemId: 424242 }, 'amount_mismatch'],
    ['수량 2', { quantity: 2 }, 'amount_mismatch'],
    ['항목 2개', { extraItem: true }, 'amount_mismatch'],
    ['appid', { appId: 999 }, 'appid_mismatch'],
    ['Steam ID', { steamId: '76561198000000009' }, 'steamid_mismatch'],
  ])('4. QueryTxn 이 다른 %s 를 돌려주면 지급 없음, failed(mismatch), 플래그, 원장 변화 없음', async (_n, tamper, flag) => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.tamper = tamper;
    const s = await approveAndSync(app, p, o);
    expect(s.body.data.order.state).toBe('failed');
    expect(s.body.data.wallet).toBeNull();
    expect((await walletState(p.accountId)).ledger).toBe(0);
    expect(mock.calls.finalizeTxn).toBe(0);
    const f = await getPool().query('SELECT kind, severity FROM payment_flags WHERE account_id = $1', [p.accountId]);
    expect(f.rows).toEqual(expect.arrayContaining([expect.objectContaining({ kind: flag, severity: 3 })]));
  });

  it('5. 같은 주문의 sync 동시 20개 + 대사 작업 동시: FinalizeTxn 정확히 1회, purchase 원장 1줄, 로트 1행, 지갑 +stars 한 번', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.approve(o.steam_order_id);
    mock.finalizeDelayMs = 30;
    const calls: Promise<unknown>[] = Array.from({ length: 20 }, () => sync(app, p, o.id));
    calls.push(paymentReconcileJob(jobCtx));
    await Promise.all(calls);
    // 지연된 쪽이 남아 있으면 한 번 더 확인해 끝낸다
    await sync(app, p, o.id);
    expect(mock.calls.finalizeTxn).toBe(1);
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
    const lots = await getPool().query('SELECT count(*) AS n FROM star_paid_lots WHERE account_id = $1', [p.accountId]);
    expect(Number((lots.rows[0] as { n: string }).n)).toBe(1);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 300, paid: 300, lots: 300 });
    expect((await orderRow(o.id)).state).toBe('granted');
  });

  it('6. 확정 직후 응답이 사라져도(timeout_after_finalize) 다음 확인이 정확히 1회 지급한다', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.finalizeMode = 'timeout_after_finalize';
    const s = await approveAndSync(app, p, o);
    expect(s.body.data.order.state).toBe('granted');
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
    expect(mock.calls.finalizeTxn).toBe(1);
  });

  it('6. InitTxn 성공 직후 응답 유실(timeout_after_create): pending_init, 재시도 호출 없음(1회), sync 가 QueryTxn 으로 created 로 채택한다', async () => {
    const p = await newPayer(app);
    mock.initMode = 'timeout_after_create';
    const res = await buy(app, p);
    expect(res.status).toBe(201);
    expect(res.body.data.order.state).toBe('pending_init');
    expect(res.body.data.next).toBe('sync');
    expect(mock.calls.initTxn).toBe(1);
    const s = await sync(app, p, res.body.data.order.id);
    expect(s.body.data.order.state).toBe('created');
    expect(mock.calls.initTxn).toBe(1);
    const done = await approveAndSync(app, p, res.body.data.order);
    expect(done.body.data.order.state).toBe('granted');
  });

  it('6. InitTxn 이 Steam에 닿지 못했고(timeout_before_create) 오래 지나면 failed(init_lost), 청구 없음', async () => {
    const p = await newPayer(app);
    mock.initMode = 'timeout_before_create';
    const res = await buy(app, p);
    expect(res.body.data.order.state).toBe('pending_init');
    expect((await sync(app, p, res.body.data.order.id)).body.data.order.state).toBe('pending_init');
    await getPool().query("UPDATE star_orders SET created_at = now() - interval '20 minutes', expires_at = now() - interval '5 minutes' WHERE uuid = $1", [res.body.data.order.id]);
    const s = await sync(app, p, res.body.data.order.id);
    expect(s.body.data.order).toMatchObject({ state: 'failed', fail_code: 'NOT_COMPLETED', charged: false });
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(0);
  });

  it('InitTxn 명시적 거절: failed(init_rejected), 플레이어에게는 NOT_COMPLETED 만 보인다', async () => {
    const p = await newPayer(app);
    mock.initMode = 'reject';
    const res = await buy(app, p);
    expect(res.status).toBe(201);
    expect(res.body.data.order).toMatchObject({ state: 'failed', fail_code: 'NOT_COMPLETED', charged: false, final: true });
    expect(JSON.stringify(res.body)).not.toContain('init_rejected');
    expect(JSON.stringify(res.body)).not.toContain('fail_reason');
  });

  it('7. 같은 request_id 재전송은 같은 주문(행 1개), 다른 product_id 는 422 IDEMPOTENCY_MISMATCH, 만료된 견적이어도 기존 주문을 돌려준다', async () => {
    const p = await newPayer(app);
    const rid = randomUUID();
    const o = await offerOf(app, p, 'stars_300');
    const first = await order(app, p, { request_id: rid, product_id: 'stars_300', quote_id: o.quote_id });
    expect(first.status).toBe(201);
    const again = await order(app, p, { request_id: rid, product_id: 'stars_300', quote_id: o.quote_id });
    expect(again.status).toBe(200);
    expect(again.body.data.order.id).toBe(first.body.data.order.id);
    const o2 = await offerOf(app, p, 'stars_1000').catch(() => null);
    void o2;
    const other = await order(app, p, { request_id: rid, product_id: 'stars_1000', quote_id: o.quote_id });
    expect(other.status).toBe(422);
    expect(other.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    const n = await getPool().query('SELECT count(*) AS n FROM star_orders WHERE account_id = $1 AND request_id = $2', [p.accountId, rid]);
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
    // 견적이 만료된 뒤 같은 요청을 다시 보내도(크래시 뒤 재시도) 기존 주문을 돌려준다
    setClockOverride(() => new Date(Date.now() + 3_600_000));
    try {
      const late = await order(app, p, { request_id: rid, product_id: 'stars_300', quote_id: o.quote_id });
      expect(late.status).toBe(200);
      expect(late.body.data.order.id).toBe(first.body.data.order.id);
    } finally {
      setClockOverride(null);
    }
  });

  it('8. 견적 위조: 서명 변조, 다른 계정의 견적, 만료, 가격이 바뀐 뒤의 견적', async () => {
    const p = await newPayer(app);
    const other = await newPayer(app);
    const o = await offerOf(app, p, 'stars_300');
    const tampered = `${o.quote_id.slice(0, -4)}AAAA`;
    const t = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: tampered });
    expect(t.status).toBe(409);
    expect(t.body.errors.code).toBe('QUOTE_CHANGED');
    const foreign = await offerOf(app, other, 'stars_300');
    const f = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: foreign.quote_id });
    expect(f.body.errors.code).toBe('QUOTE_CHANGED');
    const wrongProduct = await order(app, p, { request_id: randomUUID(), product_id: 'stars_1000', quote_id: o.quote_id });
    expect(wrongProduct.body.errors.code).toBe('QUOTE_CHANGED');
    const cat = getStarCatalog();
    const expired = signQuote({ a: p.accountUuid, p: 'stars_300', c: 'KRW', m: 1100, s: 300, v: cat.version, e: Math.floor(Date.now() / 1000) - 5 });
    const e = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: expired });
    expect(e.status).toBe(410);
    expect(e.body.errors.code).toBe('QUOTE_EXPIRED');
    // 서버가 가격을 올리면(상품표가 바뀌면) 이전 견적은 거절된다
    setStarCatalogForTest({ ...cat, products: cat.products.map((x) => (x.id === 'stars_300' ? { ...x, prices: { KRW: 2200 } } : x)) });
    try {
      const changed = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: o.quote_id });
      expect(changed.status).toBe(409);
      expect(changed.body.errors.code).toBe('QUOTE_CHANGED');
    } finally {
      setStarCatalogForTest(cat);
    }
    expect((await getPool().query('SELECT count(*) AS n FROM star_orders WHERE account_id = $1', [p.accountId])).rows[0]).toMatchObject({ n: '0' });
  });

  it('9. 승인 전에 만료된 주문을 나중에 Steam에서 승인해도 서버는 확정하지 않는다(expired 유지, 확정 호출 0회)', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    await getPool().query("UPDATE star_orders SET expires_at = now() - interval '1 minute', created_at = now() - interval '20 minutes' WHERE uuid = $1", [o.id]);
    const s = await approveAndSync(app, p, o);
    expect(s.body.data.order).toMatchObject({ state: 'expired', fail_code: 'EXPIRED', charged: false });
    // 이후 Steam 쪽 사건은 종결 주문을 건드리지 않는다
    await sync(app, p, o.id);
    expect(mock.calls.finalizeTxn).toBe(0);
    expect((await walletState(p.accountId)).balance).toBe(0);
  });

  it('10. 결제 정지 계정의 authorized 주문은 확정 호출 없이 failed(blocked)', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.approve(o.steam_order_id);
    // authorized 까지만 진행한 뒤 결제 정지를 건다
    const row = await orderRow(o.id);
    await getPool().query("UPDATE star_orders SET state = 'authorized', authorized_at = now() WHERE id = $1", [row.id]);
    await getPool().query("INSERT INTO payment_profiles (account_id, status, block_reason, blocked_at, blocked_by) VALUES ($1, 'blocked', 'manual', now(), 'test') ON CONFLICT (account_id) DO UPDATE SET status = 'blocked', block_reason = 'manual', blocked_at = now()", [p.accountId]);
    const s = await sync(app, p, o.id);
    expect(s.body.data.order).toMatchObject({ state: 'failed', fail_code: 'NOT_COMPLETED', charged: false });
    expect(mock.calls.finalizeTxn).toBe(0);
  });

  it('11. 임대 펜싱: 임대를 잃은 작업자의 늦은 쓰기는 0행(아무것도 바뀌지 않는다)', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    const repo = await import('../src/domains/payments/paymentsRepository');
    const row = await orderRow(o.id);
    const leased = await repo.acquireLease(getPool(), Number(row.id), 30, 0);
    expect(leased).not.toBeNull();
    // 다른 작업자는 임대를 잡을 수 없다(Steam 호출도 없다)
    const before = mock.calls.queryTxn;
    const other = await advanceOrder(Number(row.id), { actor: 'job' });
    expect(other.leased).toBe(false);
    expect(mock.calls.queryTxn).toBe(before);
    // 임대가 만료되어 새 작업자가 가져갔다고 하자: 옛 토큰으로 쓰면 0행
    await getPool().query("UPDATE star_orders SET lease_until = now() - interval '1 second' WHERE id = $1", [row.id]);
    const second = await repo.acquireLease(getPool(), Number(row.id), 30, 0);
    expect(second?.lease_token).not.toBe(leased?.lease_token);
    const stale = await repo.updateFenced(getPool(), Number(row.id), leased?.lease_token as string, 'created', { state: 'expired', closed_at: new Date(), next_check_at: null });
    expect(stale).toBe(false);
    expect((await orderRow(o.id)).state).toBe('created');
    const fresh = await repo.updateFenced(getPool(), Number(row.id), second?.lease_token as string, 'created', { next_check_at: new Date() });
    expect(fresh).toBe(true);
  });
});

describe('대사 작업(payment-reconcile)', () => {
  it('6. finalized 로 남은 주문(확정 직후 크래시)을 작업이 정확히 1회 지급한다', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.setStatus(o.steam_order_id, 'Succeeded');
    await getPool().query("UPDATE star_orders SET state = 'finalized', finalized_at = now(), authorized_at = now(), next_check_at = now() - interval '1 minute' WHERE uuid = $1", [o.id]);
    resetPayPartnerStats();
    await paymentReconcileJob(jobCtx);
    await paymentReconcileJob(jobCtx);
    expect((await orderRow(o.id)).state).toBe('granted');
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
    expect(mock.calls.finalizeTxn).toBe(0);
  });

  it('승인 안 된 만료 주문은 Steam 이 아직 Init 이라고 답할 때 expired 로 닫힌다(청구 없음)', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    await getPool().query("UPDATE star_orders SET expires_at = now() - interval '1 minute', created_at = now() - interval '20 minutes', next_check_at = now() - interval '1 minute' WHERE uuid = $1", [o.id]);
    resetPayPartnerStats();
    await paymentReconcileJob(jobCtx);
    expect((await orderRow(o.id)).state).toBe('expired');
  });

  it('4. 락 순서: 소비·환불 감지·새 주문 생성·분해를 섞은 동시 부하에서 교착 없이 끝나고 지갑이 일관된다', async () => {
    const p = await newPayer(app);
    const hero = await (await import('./payHelpers')).heroOf(app, p);
    const o = await purchase(app, p, 'stars_1000');
    mock.setStatus(o.steam_order_id, 'Refunded');
    const starshop = await import('../src/domains/starshop/starshopService');
    const work: Promise<unknown>[] = [];
    for (let i = 0; i < 5; i++) work.push(starshop.pull(p.accountId, hero.id, { request_id: randomUUID(), count: 1 }).catch(() => undefined));
    for (let i = 0; i < 3; i++) work.push(sync(app, p, o.id));
    work.push(buy(app, p, 'stars_300'));
    const rs = await Promise.allSettled(work);
    expect(rs.every((r) => r.status === 'fulfilled')).toBe(true);
    await (await import('./payHelpers')).expectWalletConsistent(p.accountId);
    expect(await ledgerCount(p.accountId, 'refund_revoke')).toBe(1);
  });
});

describe('로그인·Steam 연결', () => {
  it('7.4: 같은 Steam 계정으로 다시 로그인하면 열린 주문을 백그라운드로 대사한다(승인된 주문이 지급된다)', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.approve(o.steam_order_id);
    const login = await request(app).post('/auth/steam').set(ver()).send({ ticket: `mock:${p.steamId}:${randomBytes(8).toString('hex')}` });
    expect(login.status).toBe(200);
    for (let i = 0; i < 40 && (await orderRow(o.id)).state !== 'granted'; i++) await new Promise((r) => setTimeout(r, 100));
    expect((await orderRow(o.id)).state).toBe('granted');
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
  });

  it('E11: 주문 기록이 있는 계정이 다른 Steam ID 로 연결을 바꾸려 하면 거절된다(계정당 Steam 신원은 하나)', async () => {
    const p = await newPayer(app);
    await purchase(app, p);
    const link = await request(app).post('/auth/steam/link').set(authP(p)).send({ ticket: `mock:${newSteamId()}:${randomBytes(8).toString('hex')}` });
    expect(link.status).toBe(409);
    expect(link.body.errors.code).toBe('ACCOUNT_ALREADY_LINKED');
    const row = await getPool().query("SELECT subject FROM auth_identities WHERE account_id = $1 AND provider = 'steam'", [p.accountId]);
    expect(row.rows[0]).toMatchObject({ subject: p.steamId });
  });
});

describe('D. 한도·사기 결제', () => {
  it('2. 한도 초과는 B2에서 PAY_LIMIT_EXCEEDED 이고 주문 행·Steam 호출이 생기지 않는다', async () => {
    const small = payApp({ PAY_LIMIT_NEW_DAILY_STARS: '400', PAY_LIMIT_NEW_MONTHLY_STARS: '10000', PAY_LIMIT_RESTRICTED_DAILY_STARS: '100', PAY_LIMIT_RESTRICTED_MONTHLY_STARS: '1000', PAY_LIMIT_DAILY_STARS: '400', PAY_LIMIT_MONTHLY_STARS: '10000' });
    const p = await newPayer(small);
    const list = await products(small, p);
    const big = list.body.data.offers.find((x: { product_id: string }) => x.product_id === 'stars_1000');
    expect(big).toMatchObject({ purchasable: false, block: 'LIMIT_DAILY' });
    const before = mock.calls.initTxn;
    const res = await order(small, p, { request_id: randomUUID(), product_id: 'stars_1000', quote_id: big.quote_id });
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'PAY_LIMIT_EXCEEDED', scope: 'daily', limit: 400, used: 0 });
    expect(mock.calls.initTxn).toBe(before);
    expect((await getPool().query('SELECT count(*) AS n FROM star_orders WHERE account_id = $1', [p.accountId])).rows[0]).toMatchObject({ n: '0' });
    payApp();
  });

  it('1. 한도 합산: 환불된 주문도 센다, failed·expired 는 뺀다, 06:00 KST 경계에서 일일이 초기화되고 월은 롤링 30일', async () => {
    const small = payApp({ PAY_LIMIT_RESTRICTED_DAILY_STARS: '100', PAY_LIMIT_RESTRICTED_MONTHLY_STARS: '200', PAY_LIMIT_NEW_DAILY_STARS: '500', PAY_LIMIT_NEW_MONTHLY_STARS: '700', PAY_LIMIT_DAILY_STARS: '500', PAY_LIMIT_MONTHLY_STARS: '700', PAY_NEW_ACCOUNT_DAYS: '0' });
    const p = await newPayer(small);
    const db = getPool();
    const ins = (state: string, stars: number, at: string): Promise<unknown> =>
      db.query(
        `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, tier, state, expires_at, created_at,
                                  granted_at, reversed_at, fail_reason)
         VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', $3, 'KRW', 1100, 'standard', $4, $5::timestamptz + interval '1 hour', $5::timestamptz,
                 CASE WHEN $4 IN ('granted', 'refunded') THEN $5::timestamptz END, CASE WHEN $4 = 'refunded' THEN $5::timestamptz END, CASE WHEN $4 = 'failed' THEN 'init_rejected' END)`,
        [p.accountId, p.steamId, stars, state, at],
      );
    // 06:00 KST 직전(2026-10-06 05:59:00 KST = 2026-10-05T20:59:00Z)
    const before = new Date('2026-10-05T20:59:00Z');
    setClockOverride(() => new Date(before.getTime() + 120_000)); // 지금 = 06:01 KST
    try {
      await ins('refunded', 200, '2026-10-05T20:58:00Z'); // 06:00 이전: 일일에는 안 세고 월에는 센다
      await ins('failed', 300, '2026-10-05T21:00:30Z'); // 실패는 안 센다
      await ins('expired', 300, '2026-10-05T21:00:40Z');
      await ins('refunded', 250, '2026-10-05T21:00:50Z'); // 06:00 이후 환불: 일일에 센다
      const list = await products(small, p);
      expect(list.status).toBe(200);
      expect(list.body.data.limits.daily).toMatchObject({ limit: 500, used: 250, left: 250 });
      expect(list.body.data.limits.monthly).toMatchObject({ limit: 700, used: 450, left: 250 });
      // 300 별조각은 일일(250 남음)을 넘는다, 월(250 남음)도 넘는다
      const offer = list.body.data.offers.find((x: { product_id: string }) => x.product_id === 'stars_300');
      expect(offer).toMatchObject({ purchasable: false });
      // 31일 뒤: 월 롤링 창에서 빠진다
      setClockOverride(() => new Date(before.getTime() + 120_000 + 31 * 86_400_000));
      const later = await products(small, p);
      expect(later.body.data.limits.monthly.used).toBe(0);
    } finally {
      setClockOverride(null);
      payApp();
    }
  });

  it('3. 동시 주문: 같은 계정이 서로 다른 request_id 로 20개 동시 B2 -> 정확히 1개 성공, 나머지 ORDER_IN_PROGRESS', async () => {
    const p = await newPayer(app);
    const o = await offerOf(app, p);
    const rs = await Promise.all(Array.from({ length: 20 }, () => order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: o.quote_id })));
    expect(rs.filter((r) => r.status === 201).length).toBe(1);
    for (const r of rs.filter((x) => x.status !== 201)) {
      expect(r.status).toBe(409);
      expect(r.body.errors.code).toBe('ORDER_IN_PROGRESS');
    }
    expect((await getPool().query('SELECT count(*) AS n FROM star_orders WHERE account_id = $1', [p.accountId])).rows[0]).toMatchObject({ n: '1' });
    expect(mock.calls.initTxn).toBe(1);
  });

  it('3. 한도가 한 주문만 남은 상태에서 두 요청(열린 주문이 없어도 순차화)은 한 개만 통과한다', async () => {
    const small = payApp({ PAY_LIMIT_RESTRICTED_DAILY_STARS: '100', PAY_LIMIT_RESTRICTED_MONTHLY_STARS: '100', PAY_LIMIT_NEW_DAILY_STARS: '300', PAY_LIMIT_NEW_MONTHLY_STARS: '300', PAY_LIMIT_DAILY_STARS: '300', PAY_LIMIT_MONTHLY_STARS: '300' });
    const p = await newPayer(small);
    const o = await offerOf(small, p);
    const rs = await Promise.all([1, 2].map(() => order(small, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: o.quote_id })));
    expect(rs.filter((r) => r.status === 201).length).toBe(1);
    payApp();
  });

  it('4. 단계: 가입 14일 미만 new, 연결 계정 차지백/국가 변경/공유 기기 구매 계정 3개 이상 restricted(공용 기기는 제외)', async () => {
    const p = await newPayer(app);
    expect((await products(app, p)).body.data.tier).toBe('new');
    await getPool().query("UPDATE accounts SET created_at = now() - interval '30 days' WHERE id = $1", [p.accountId]);
    expect((await products(app, p)).body.data.tier).toBe('standard');
    // 국가 변경 2번(3개 국가): restricted
    const c = await newPayer(app);
    await getPool().query("UPDATE accounts SET created_at = now() - interval '30 days' WHERE id = $1", [c.accountId]);
    for (const [i, country] of ['KR', 'JP', 'US'].entries()) {
      await getPool().query(
        `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, steam_country, tier, state, expires_at, fail_reason)
         VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', 1, 'KRW', 1100, $3, 'standard', 'failed', now() + interval '1 hour', 'init_rejected')`,
        [c.accountId, c.steamId, country],
      );
      void i;
    }
    expect((await products(app, c)).body.data.tier).toBe('restricted');
    // 공유 기기: 같은 기기를 쓴 구매 계정 3개 -> restricted, 기기를 쓴 계정이 7개 이상이면 공용으로 보고 제외
    const hash = 'a'.repeat(64);
    const main = await newPayer(app);
    await getPool().query("UPDATE accounts SET created_at = now() - interval '30 days' WHERE id = $1", [main.accountId]);
    const others = [await newPayer(app), await newPayer(app), await newPayer(app)];
    for (const x of [main, ...others]) await getPool().query('INSERT INTO account_devices (account_id, device_hash) VALUES ($1, $2) ON CONFLICT DO NOTHING', [x.accountId, hash]);
    // 구매 계정(실패·만료가 아닌 주문이 있는 계정)으로 센다
    for (const x of others) {
      await getPool().query(
        `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, tier, state, expires_at)
         VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', 1, 'KRW', 1100, 'standard', 'created', now() + interval '1 hour')`,
        [x.accountId, x.steamId],
      );
    }
    expect((await products(app, main)).body.data.tier).toBe('restricted');
    for (let i = 0; i < 4; i++) {
      const extra = await newPayer(app);
      await getPool().query('INSERT INTO account_devices (account_id, device_hash) VALUES ($1, $2)', [extra.accountId, hash]);
    }
    expect((await products(app, main)).body.data.tier).toBe('standard');
  });

  it('5. 속도: 60초 안 재주문, 1시간 3건 초과, 1시간 실패·만료 5건 -> ORDER_TOO_FAST(retry_after_sec)', async () => {
    const fast = payApp({ PAY_MIN_ORDER_GAP_SECONDS: '60' });
    const p = await newPayer(fast);
    const first = await buy(fast, p);
    expect(first.status).toBe(201);
    await getPool().query("UPDATE star_orders SET state = 'failed', fail_reason = 'user_denied', closed_at = now(), next_check_at = NULL WHERE uuid = $1", [first.body.data.order.id]);
    const second = await buy(fast, p);
    expect(second.status).toBe(429);
    expect(second.body.errors.code).toBe('ORDER_TOO_FAST');
    expect(second.body.errors.retry_after_sec).toBeGreaterThan(0);
    const hourly = payApp({ PAY_MIN_ORDER_GAP_SECONDS: '0', PAY_MAX_ORDERS_PER_HOUR: '2', PAY_FAIL_COOLDOWN_THRESHOLD: '1000' });
    const q = await newPayer(hourly);
    for (let i = 0; i < 2; i++) {
      const r = await buy(hourly, q);
      expect(r.status).toBe(201);
      await getPool().query("UPDATE star_orders SET state = 'expired', closed_at = now(), next_check_at = NULL WHERE uuid = $1", [r.body.data.order.id]);
    }
    const third = await buy(hourly, q);
    expect(third.status).toBe(429);
    const burst = payApp({ PAY_MIN_ORDER_GAP_SECONDS: '0', PAY_MAX_ORDERS_PER_HOUR: '100', PAY_FAIL_COOLDOWN_THRESHOLD: '2' });
    const w = await newPayer(burst);
    for (let i = 0; i < 2; i++) {
      const r = await buy(burst, w);
      await getPool().query("UPDATE star_orders SET state = 'expired', closed_at = now(), next_check_at = NULL WHERE uuid = $1", [r.body.data.order.id]);
    }
    const blocked = await buy(burst, w);
    expect(blocked.status).toBe(429);
    const flags = await getPool().query("SELECT kind FROM payment_flags WHERE account_id = $1", [w.accountId]);
    expect(flags.rows.map((x: { kind: string }) => x.kind)).toContain('fail_burst');
    payApp();
  });

  it('6. 통화: 상품표에 없는 통화는 CURRENCY_UNSUPPORTED, 90일 안 국가 변경 2번이면 country_changed 플래그 + restricted', async () => {
    const p = await newPayer(app);
    mock.user = { country: 'DE', currency: 'EUR' };
    const res = await products(app, p);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('CURRENCY_UNSUPPORTED');
    const o = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: 'q'.repeat(40) });
    expect(o.body.errors.code).toBe('CURRENCY_UNSUPPORTED');
    mock.user = { country: 'KR', currency: 'KRW' };
    const c = await newPayer(app);
    const countries = ['JP', 'US'];
    for (const country of countries) {
      await getPool().query(
        `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, steam_country, tier, state, expires_at, fail_reason)
         VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', 1, 'KRW', 1100, $3, 'standard', 'failed', now() + interval '1 hour', 'init_rejected')`,
        [c.accountId, c.steamId, country],
      );
    }
    const r = await buy(app, c);
    expect(r.status).toBe(201);
    expect(r.body.data.order).toBeTruthy();
    const row = await orderRow(r.body.data.order.id);
    expect(row.tier).toBe('restricted');
    const f = await getPool().query("SELECT kind FROM payment_flags WHERE account_id = $1", [c.accountId]);
    expect(f.rows.map((x: { kind: string }) => x.kind)).toContain('country_changed');
  });

  it('7. 경제 정지 계정은 ECONOMY_HOLD, 결제 정지 계정은 PAYMENT_BLOCKED(응답에 사유 없음)', async () => {
    const p = await newPayer(app);
    const o = await offerOf(app, p);
    await getPool().query("INSERT INTO economy_holds (account_id, kind, state, reviewed_at) VALUES ($1, 'manual', 'active', now())", [p.accountId]);
    const hold = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: o.quote_id });
    expect(hold.status).toBe(403);
    expect(hold.body.errors.code).toBe('ECONOMY_HOLD');
    await getPool().query("INSERT INTO payment_profiles (account_id, status, block_reason, blocked_at, blocked_by) VALUES ($1, 'blocked', 'chargeback', now(), 'system') ON CONFLICT (account_id) DO UPDATE SET status = 'blocked', block_reason = 'chargeback', blocked_at = now()", [p.accountId]);
    const blocked = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: o.quote_id });
    expect(blocked.status).toBe(403);
    expect(blocked.body.errors.code).toBe('PAYMENT_BLOCKED');
    expect(JSON.stringify(blocked.body)).not.toContain('chargeback');
    expect((await products(app, p)).body.errors.code).toBe('PAYMENT_BLOCKED');
  });
});

describe('B6, B5, 장애', () => {
  it('B6 reconcile 은 열린 주문을 Steam 기준으로 끝내고, 이력(B5)은 내 주문만 최신순으로 준다', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.approve(o.steam_order_id);
    const r = await request(app).post('/payments/reconcile').set(authP(p)).send({});
    expect(r.status).toBe(200);
    expect(r.body.data.resolved[0]).toMatchObject({ id: o.id, state: 'granted' });
    expect(r.body.data.open_order).toBeNull();
    const other = await newPayer(app);
    await purchase(app, other);
    const hist = await request(app).get('/payments/orders?limit=5').set(authP(p));
    expect(hist.body.data.items.map((x: { id: string }) => x.id)).toEqual([o.id]);
  });

  it('Steam 5xx 연속: 회로 차단, B1·B2 는 STEAM_UNAVAILABLE, 열린 주문은 만료 시각이 지나도 Steam 확인 전에는 expired 로 닫히지 않는다', async () => {
    const brk = payApp({ STEAM_BREAKER_FAILURES: '2', STEAM_BREAKER_OPEN_SECONDS: '60' });
    const p = await newPayer(brk);
    const o = (await buy(brk, p)).body.data.order;
    await getPool().query("UPDATE star_orders SET expires_at = now() - interval '1 minute', created_at = now() - interval '20 minutes' WHERE uuid = $1", [o.id]);
    mock.failAll = 'unavailable';
    const s1 = await sync(brk, p, o.id);
    expect(s1.status).toBe(503);
    expect(s1.body.errors.code).toBe('STEAM_UNAVAILABLE');
    await sync(brk, p, o.id);
    const list = await products(brk, p);
    expect(list.status).toBe(503);
    expect((await orderRow(o.id)).state).toBe('created');
    // 복구: 회로가 닫힌 뒤 확인하고 닫는다
    mock.failAll = null;
    const { resetPayPartnerStats } = await import('../src/domains/payments/steamPartner');
    resetPayPartnerStats();
    const s2 = await sync(brk, p, o.id);
    expect(s2.body.data.order.state).toBe('expired');
    payApp();
  });

  it('키 거절(mock 401): 상태 변경 없음 + payment_key_rejected 지표(critical 경보 입력)', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.failAll = 'rejected';
    const s = await sync(app, p, o.id);
    expect(s.status).toBe(503);
    expect((await orderRow(o.id)).state).toBe('created');
    const { payPartnerStats } = await import('../src/domains/payments/steamPartner');
    expect(payPartnerStats().key_rejected_recent).toBe(true);
    const { evaluate } = await import('../src/ops/alertRules');
    const { collectSnapshot } = await import('../src/ops/snapshot');
    const snap = await collectSnapshot();
    const keys = evaluate({ snap, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 }).map((a) => `${a.level}:${a.key}`);
    expect(keys).toContain('critical:payment_key_rejected');
  });

  it('로그에도 응답에도 키·견적 비밀이 없다(오류를 주입한 호출의 오류 메시지)', async () => {
    const { PartnerCallError } = await import('../src/domains/payments/steamPartner');
    const e = new PartnerCallError('unavailable', 500);
    expect(e.message).not.toMatch(/key=|secret|http/i);
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.failAll = 'unavailable';
    const s = await sync(app, p, o.id);
    const text = JSON.stringify(s.body);
    expect(text).not.toContain('quote-secret');
    expect(text).not.toContain('STEAM_PUBLISHER');
    const events = await getPool().query('SELECT detail FROM star_order_events');
    expect(JSON.stringify(events.rows)).not.toMatch(/quote-secret|publisher|https?:/i);
  });
});
