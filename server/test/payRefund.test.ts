// 11단계 결제: 환불·차지백(설계 18절 C). 가짜 Steam(mock)으로 Steam 쪽 사건을 일으키고, 서버 감시가 감지해 회수한다.
import { randomUUID } from 'node:crypto';
import { getPool, withTransaction } from '../src/db/pool';
import { watchNextCheck } from '../src/domains/payments/orderMachine';
import { debit } from '../src/domains/starshop/starWallet';
import { paymentWatchJob } from '../src/ops/jobs/paymentJobs';
import { resetPayPartnerStats } from '../src/domains/payments/steamPartner';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { adminApp, adminPost, makeAdmin } from './opsHelpers';
import { post } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { approveAndSync, buy, order, expectWalletConsistent, heroOf, ledgerCount, mock, newPayer, orderRow, payApp, purchase, resetPay, seedStars, sync, walletState } from './payHelpers';

const app = payApp();
const admin = adminApp();
beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(shutdown);

const spend = (accountId: number, price: number) =>
  withTransaction((c) => debit(c, { accountId, reason: 'gacha', price, ref: 'aura_1', requestId: randomUUID() }));

const refundIt = async (p: Awaited<ReturnType<typeof newPayer>>, o: { id: string; steam_order_id: string }, status = 'Refunded') => {
  mock.setStatus(o.steam_order_id, status);
  return sync(app, p, o.id);
};

describe('C. 환불·차지백', () => {
  it('1. 지급 후 일부 소비(무료분 먼저)하고 Refunded: 로트 남은 분 회수, 부채 = 지급량 - 남은 분, 소비는 STAR_DEBT, 분해는 부채를 먼저 갚는다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 100);
    const o = await purchase(app, p);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 400, paid: 300 });
    for (let i = 0; i < 3; i++) expect((await post(app, hero, '/starshop/pull', { count: 1 })).status).toBe(200);
    // 무료분(100) 먼저, 유료분은 200만 줄었다
    expect(await walletState(p.accountId)).toMatchObject({ balance: 100, paid: 100, lots: 100 });
    const alloc = await getPool().query<{ s: string }>('SELECT coalesce(sum(a.stars), 0) AS s FROM star_spend_allocs a JOIN star_orders o ON o.id = a.order_id WHERE o.uuid = $1', [o.id]);
    expect(Number((alloc.rows[0] as { s: string }).s)).toBe(200);
    await expectWalletConsistent(p.accountId);

    const r = await refundIt(p, o);
    expect(r.body.data.order).toMatchObject({ state: 'refunded', final: true });
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, paid: 0, debt: 200, lots: 0 });
    expect(await ledgerCount(p.accountId, 'refund_revoke')).toBe(1);
    await expectWalletConsistent(p.accountId);

    // 뽑기·교환·선택·합성은 막힌다(캐릭터·계정별 초당 한도를 비운다)
    const go = (path: string, body: Record<string, unknown>) => {
      getRateLimitStore().clear();
      return post(app, hero, path, body);
    };
    const denied = async (res: { status: number; body: { errors: { code: string; debt?: number } } }): Promise<void> => {
      expect(res.status).toBe(403);
      expect(res.body.errors).toMatchObject({ code: 'STAR_DEBT', debt: 200 });
    };
    await denied(await go('/starshop/pull', { count: 1 }));
    await denied(await go('/starshop/exchange', { item_id: 'aura_dew' }));
    await denied(await go('/starshop/claim', { banner: 'aura', item_id: 'aura_rainbow' }));
    await denied(await go('/starshop/synth', { rarity: 'common', times: 1 }));
    // 분해는 허용되고 입금이 부채를 먼저 갚는다(dismantle + debt_settle)
    await getPool().query("INSERT INTO account_cosmetics (account_id, item_id, source, copies) VALUES ($1, 'aura_dew', 'gacha', 3) ON CONFLICT (account_id, item_id) DO UPDATE SET copies = 3", [p.accountId]);
    const d = await go('/starshop/dismantle', { item_id: 'aura_dew', count: 2 });
    expect(d.status).toBe(200);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, debt: 160 });
    expect(await ledgerCount(p.accountId, 'dismantle')).toBe(1);
    expect(await ledgerCount(p.accountId, 'debt_settle')).toBe(1);
    await expectWalletConsistent(p.accountId);
    // 요약 응답에 부채가 보인다(기존 필드는 그대로)
    const summary = await (await import('supertest')).default(app).get(`/characters/${hero.id}/starshop`).set((await import('./payHelpers')).authP(p));
    expect(summary.body.data).toMatchObject({ balance: 0, paid_balance: 0, free_balance: 0, debt: 160, payments: { enabled: true } });
    expect(summary.body.data.pity).toBeDefined();
  });

  it('2. 주문 A(전부 소비)와 B(미사용): A만 환불하면 B 로트·paid_balance 는 그대로, A 소비분이 부채', async () => {
    const p = await newPayer(app);
    const a = await purchase(app, p);
    await spend(p.accountId, 300);
    const b = await purchase(app, p);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 300, paid: 300 });
    await refundIt(p, a);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 300, paid: 300, debt: 300, lots: 300 });
    const lotB = await getPool().query<{ remaining: number }>('SELECT l.remaining FROM star_paid_lots l JOIN star_orders o ON o.id = l.order_id WHERE o.uuid = $1', [b.id]);
    expect(lotB.rows[0]?.remaining).toBe(300);
    await expectWalletConsistent(p.accountId);
  });

  it('2. B만 환불하면 B 로트 전부 회수, 부채 0', async () => {
    const p = await newPayer(app);
    await purchase(app, p);
    await spend(p.accountId, 300);
    const b = await purchase(app, p);
    await refundIt(p, b);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, paid: 0, debt: 0 });
    await expectWalletConsistent(p.accountId);
  });

  it('3. 환불 이벤트 반복·동시 감지는 회수 1번(멱등)', async () => {
    const p = await newPayer(app);
    const o = await purchase(app, p);
    mock.setStatus(o.steam_order_id, 'Refunded');
    await Promise.all(Array.from({ length: 6 }, () => sync(app, p, o.id)));
    await sync(app, p, o.id);
    expect(await ledgerCount(p.accountId, 'refund_revoke')).toBe(1);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, paid: 0, debt: 0 });
    const events = await getPool().query("SELECT count(*) AS n FROM star_order_events e JOIN star_orders o ON o.id = e.order_id WHERE o.uuid = $1 AND e.kind = 'refunded'", [o.id]);
    expect(Number((events.rows[0] as { n: string }).n)).toBe(1);
  });

  it('4. Refunded 뒤 Chargeback: 회수 재실행 없음, 상태 chargeback, 결제 정지 + 경제 정지(payment), 연결 계정 restricted, 전파 정지 없음', async () => {
    const p = await newPayer(app);
    const twin = await newPayer(app);
    // 같은 Steam 소유자 키(패밀리 공유)로 묶인 두 번째 계정
    await getPool().query("UPDATE auth_identities SET steam_owner_id = $2 WHERE account_id = $1 AND provider = 'steam'", [p.accountId, '76561198111111111']);
    await getPool().query("UPDATE auth_identities SET steam_owner_id = $2 WHERE account_id = $1 AND provider = 'steam'", [twin.accountId, '76561198111111111']);
    const o = await purchase(app, p);
    // 이미 속도 정지(active)가 걸려 있어도 결제 정지가 공존한다
    await getPool().query("INSERT INTO economy_holds (account_id, kind, state, reviewed_at) VALUES ($1, 'velocity', 'active', now())", [p.accountId]);
    await refundIt(p, o, 'Refunded');
    expect((await orderRow(o.id)).state).toBe('refunded');
    const taken = await ledgerCount(p.accountId, 'refund_revoke');
    expect(taken).toBe(1);
    const r = await refundIt(p, o, 'Chargeback');
    expect(r.body.data.order.state).toBe('chargeback');
    expect(await ledgerCount(p.accountId, 'refund_revoke')).toBe(1);
    expect(await ledgerCount(p.accountId, 'chargeback_revoke')).toBe(0);
    const db = getPool();
    const prof = await db.query('SELECT status, block_reason FROM payment_profiles WHERE account_id = $1', [p.accountId]);
    expect(prof.rows[0]).toMatchObject({ status: 'blocked', block_reason: 'chargeback' });
    const holds = await db.query("SELECT kind, state FROM economy_holds WHERE account_id = $1 ORDER BY id", [p.accountId]);
    expect(holds.rows.map((h: { kind: string }) => h.kind).sort()).toEqual(['payment', 'velocity']);
    // 연결 계정은 경제 정지가 아니라 제한 단계 + 검토 큐만(D8)
    expect((await db.query("SELECT count(*) AS n FROM economy_holds WHERE account_id = $1", [twin.accountId])).rows[0]).toMatchObject({ n: '0' });
    expect((await db.query('SELECT tier_floor FROM payment_profiles WHERE account_id = $1', [twin.accountId])).rows[0]).toMatchObject({ tier_floor: 'restricted' });
    const flags = await db.query('SELECT kind, account_id FROM payment_flags ORDER BY id');
    expect(flags.rows.map((f: { kind: string }) => f.kind)).toEqual(expect.arrayContaining(['chargeback', 'linked_chargeback']));
    expect((await db.query("SELECT count(*) AS n FROM economy_holds WHERE kind = 'linked'")).rows[0]).toMatchObject({ n: '0' });
    // 결제 정지·경제 정지 계정은 새 주문을 만들 수 없다
    const again = await order(app, p, { request_id: randomUUID(), product_id: 'stars_300', quote_id: 'q'.repeat(40) });
    expect(again.status).toBe(403);
    expect(again.body.errors.code).toBe('PAYMENT_BLOCKED');
  });

  it('5. 지급 전 주문(authorized/finalized)이 환불되면 회수 없이 refunded, 원장 변화 없음', async () => {
    const p = await newPayer(app);
    const o = (await buy(app, p)).body.data.order;
    mock.approve(o.steam_order_id);
    await getPool().query("UPDATE star_orders SET state = 'authorized', authorized_at = now() WHERE uuid = $1", [o.id]);
    mock.setStatus(o.steam_order_id, 'Refunded');
    const r = await sync(app, p, o.id);
    expect(r.body.data.order.state).toBe('refunded');
    expect((await walletState(p.accountId)).ledger).toBe(0);
    expect(mock.calls.finalizeTxn).toBe(0);
  });

  it('6. PartialRefund -> refunded(전량) + needs_review + 큐, 알 수 없는 상태 문자열 -> 변경 없음 + needs_review + 경보 플래그', async () => {
    const p = await newPayer(app);
    const o = await purchase(app, p);
    await refundIt(p, o, 'PartialRefund');
    const row = await orderRow(o.id);
    expect(row).toMatchObject({ state: 'refunded', needs_review: true });
    expect((await getPool().query("SELECT kind FROM payment_flags WHERE account_id = $1", [p.accountId])).rows.map((x: { kind: string }) => x.kind)).toContain('partial_refund');
    const q = await newPayer(app);
    const o2 = await purchase(app, q);
    await refundIt(q, o2, 'SomethingNew');
    expect(await orderRow(o2.id)).toMatchObject({ state: 'granted', needs_review: true, review_reason: 'unknown_steam_status' });
    expect((await getPool().query("SELECT kind FROM payment_flags WHERE account_id = $1", [q.accountId])).rows.map((x: { kind: string }) => x.kind)).toContain('unknown_steam_status');
    expect(await walletState(q.accountId)).toMatchObject({ balance: 300, debt: 0 });
  });

  it('7. 사서 뽑고 환불 반복: 소비율 50% 이상 환불 1회 = 플래그, 2회 = restricted, 3회 = 결제 정지', async () => {
    const mk = async (priorFlags: number) => {
      const p = await newPayer(app);
      for (let i = 0; i < priorFlags; i++) await getPool().query("INSERT INTO payment_flags (account_id, kind, severity) VALUES ($1, 'refund_after_spend', 2)", [p.accountId]);
      const o = await purchase(app, p);
      await spend(p.accountId, 300);
      await refundIt(p, o);
      return p;
    };
    const one = await mk(0);
    expect((await getPool().query("SELECT count(*) AS n FROM payment_flags WHERE account_id = $1 AND kind = 'refund_after_spend'", [one.accountId])).rows[0]).toMatchObject({ n: '1' });
    expect((await getPool().query('SELECT 1 FROM payment_profiles WHERE account_id = $1 AND (tier_floor = \'restricted\' OR status = \'blocked\')', [one.accountId])).rows.length).toBe(0);
    const two = await mk(1);
    expect((await getPool().query('SELECT tier_floor, status FROM payment_profiles WHERE account_id = $1', [two.accountId])).rows[0]).toMatchObject({ tier_floor: 'restricted', status: 'active' });
    const three = await mk(2);
    expect((await getPool().query('SELECT status, block_reason FROM payment_profiles WHERE account_id = $1', [three.accountId])).rows[0]).toMatchObject({ status: 'blocked', block_reason: 'refund_abuse' });
    // 소비가 적은 환불은 플래그를 만들지 않는다
    const lite = await newPayer(app);
    const lo = await purchase(app, lite);
    await spend(lite.accountId, 100);
    await refundIt(lite, lo);
    expect((await getPool().query("SELECT count(*) AS n FROM payment_flags WHERE account_id = $1", [lite.accountId])).rows[0]).toMatchObject({ n: '0' });
  });

  it('8. H3: payment 경제 정지는 operator 해제 403, owner 성공. 결제 정지 해제(PA6)는 경제 정지를 풀지 않는다', async () => {
    const p = await newPayer(app);
    const o = await purchase(app, p);
    await refundIt(p, o, 'Chargeback');
    const hold = (await getPool().query<{ uuid: string }>("SELECT uuid FROM economy_holds WHERE account_id = $1 AND kind = 'payment'", [p.accountId])).rows[0] as { uuid: string };
    const operator = await makeAdmin('operator');
    const owner = await makeAdmin('owner');
    const denied = await adminPost(admin, operator, `/admin/economy/holds/${hold.uuid}/release`, { note: '해제' });
    expect(denied.status).toBe(403);
    // 결제 정지 해제(owner)는 경제 정지를 풀지 않는다
    const acct = (await getPool().query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1', [p.accountId])).rows[0] as { uuid: string };
    const un = await adminPost(admin, owner, `/admin/payments/accounts/${acct.uuid}/unblock`, { note: '근거: 고객 확인' });
    expect(un.status).toBe(200);
    expect(un.body.data.restricted_until).toBeTruthy();
    expect((await getPool().query("SELECT state FROM economy_holds WHERE uuid = $1", [hold.uuid])).rows[0]).toMatchObject({ state: 'active' });
    const ok = await adminPost(admin, owner, `/admin/economy/holds/${hold.uuid}/release`, { note: '해제' });
    expect(ok.status).toBe(200);
    expect((await getPool().query("SELECT state FROM economy_holds WHERE uuid = $1", [hold.uuid])).rows[0]).toMatchObject({ state: 'released' });
  });

  it('9. 환불 감시: 나이별 간격(3일/30일/PAY_WATCH_DAYS), 감시 기간이 지나면 종료, Steam 호출 분당 상한을 지킨다', async () => {
    const now = new Date('2026-10-06T00:00:00Z');
    const day = 86_400_000;
    expect(watchNextCheck(new Date(now.getTime() - day), now)?.getTime()).toBe(now.getTime() + 900_000);
    expect(watchNextCheck(new Date(now.getTime() - 10 * day), now)?.getTime()).toBe(now.getTime() + 6 * 3_600_000);
    expect(watchNextCheck(new Date(now.getTime() - 40 * day), now)?.getTime()).toBe(now.getTime() + 24 * 3_600_000);
    expect(watchNextCheck(new Date(now.getTime() - 200 * day), now)).toBeNull();
    // 호출 상한: 3개가 모두 기한이 됐어도 분당 2회까지만 Steam 을 부른다
    const capped = payApp({ PAY_STEAM_MAX_CALLS_PER_MIN: '2' });
    resetPayPartnerStats();
    for (let i = 0; i < 3; i++) {
      const q = await newPayer(capped);
      const o = await purchase(capped, q);
      void o;
    }
    await getPool().query("UPDATE star_orders SET next_check_at = now() - interval '1 minute' WHERE state = 'granted'");
    const before = mock.calls.queryTxn;
    const res = await paymentWatchJob({ opts: {}, shouldStop: () => false });
    expect(mock.calls.queryTxn - before).toBe(2);
    expect(res.rows).toBe(2);
    payApp();
  });

  it('9. 감시 작업이 지급된 주문의 환불을 감지해 회수한다(클라이언트 호출 없이)', async () => {
    const p = await newPayer(app);
    const o = await purchase(app, p);
    mock.setStatus(o.steam_order_id, 'Refunded');
    await getPool().query("UPDATE star_orders SET next_check_at = now() - interval '1 minute' WHERE uuid = $1", [o.id]);
    resetPayPartnerStats();
    await paymentWatchJob({ opts: {}, shouldStop: () => false });
    expect((await orderRow(o.id)).state).toBe('refunded');
    expect(await ledgerCount(p.accountId, 'refund_revoke')).toBe(1);
  });

  it('승인 직후 감시 대상이 아닌 오래된 주문(감시 종료)은 next_check_at 이 NULL 로 끝난다', async () => {
    const p = await newPayer(app);
    const o = await purchase(app, p);
    await getPool().query("UPDATE star_orders SET granted_at = now() - interval '200 days', next_check_at = now() - interval '1 minute' WHERE uuid = $1", [o.id]);
    resetPayPartnerStats();
    await paymentWatchJob({ opts: {}, shouldStop: () => false });
    expect((await orderRow(o.id)).next_check_at).toBeNull();
  });
});

void approveAndSync;
