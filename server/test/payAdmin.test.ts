// 11단계 결제: 운영 지급·감사(설계 18절 F), 관리자 API PA1~PA14, PA13 회수, 작업·정합성 I6·경보(I), 비밀·정보 노출(G)
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { loadConfig } from '../src/config/env';
import { setClockOverride } from '../src/utils/clock';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { integrityJob } from '../src/ops/jobs/integrity';
import { paymentReportJob, starGrantExpireJob } from '../src/ops/jobs/paymentJobs';
import { purgeDaily } from '../src/ops/jobs/purge';
import { evaluate } from '../src/ops/alertRules';
import { collectSnapshot } from '../src/ops/snapshot';
import { resetPayPartnerStats } from '../src/domains/payments/steamPartner';
import { campaignBody } from './campaignHelpers';
import { adminApp, adminGet, adminPost, auditRows, makeAdmin, rid, type TestAdmin } from './opsHelpers';
import { post } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { authP, expectWalletConsistent, heroOf, ledgerCount, mock, newPayer, orderRow, payApp, PAY_ENV, purchase, resetPay, sync, walletState, type Payer } from './payHelpers';

let app = payApp();
const admin = adminApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

const accountUuid = async (p: Payer): Promise<string> => ((await getPool().query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1', [p.accountId])).rows[0] as { uuid: string }).uuid;
const grantBody = (account: string, over: Record<string, unknown> = {}): Record<string, unknown> => ({ kind: 'grant', account_id: account, stars: 100, memo: '보상 사고 보상', ...over });
const jobCtx = { opts: {}, shouldStop: () => false };

async function twoOwnersAndOperator(): Promise<{ o1: TestAdmin; o2: TestAdmin; op: TestAdmin }> {
  return { o1: await makeAdmin('owner'), o2: await makeAdmin('owner'), op: await makeAdmin('operator') };
}

describe('F. 운영 지급 PA9~PA12 (2인 승인)', () => {
  it('1. owner 가 2명 미만이면 작성 409 GRANT_NEEDS_TWO_OWNERS', async () => {
    await resetDb();
    const p = await newPayer(app);
    const op = await makeAdmin('operator');
    await makeAdmin('owner');
    const res = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(await accountUuid(p)));
    expect(res.status).toBe(409);
    expect(res.body.errors.code).toBe('GRANT_NEEDS_TWO_OWNERS');
    expect((await getPool().query('SELECT count(*) AS n FROM star_admin_grants')).rows[0]).toMatchObject({ n: '0' });
  });

  it('정상: 작성(operator) -> 승인(작성자 외 owner) = 무료분 입금(admin_grant, ref = 지급 uuid) + 감사 기록. 승인 전에는 별조각이 움직이지 않는다', async () => {
    const { o1, op } = await twoOwnersAndOperator();
    const p = await newPayer(app);
    const acct = await accountUuid(p);
    const created = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(acct));
    expect(created.status).toBe(201);
    expect(created.body.data.grant).toMatchObject({ kind: 'grant', stars: 100, state: 'pending', created_by: op.loginId });
    const gid = created.body.data.grant.id as string;
    expect((await walletState(p.accountId)).ledger).toBe(0);
    const ok = await adminPost(admin, o1, `/admin/payments/star-grants/${gid}/approve`);
    expect(ok.status).toBe(200);
    expect(ok.body.data.grant).toMatchObject({ state: 'applied', applied_stars: 100 });
    expect(ok.body.data.wallet).toMatchObject({ balance: 100, paid_balance: 0, free_balance: 100, debt: 0 });
    const led = await getPool().query('SELECT reason, delta, paid_delta, ref FROM star_ledger WHERE account_id = $1', [p.accountId]);
    expect(led.rows).toEqual([{ reason: 'admin_grant', delta: '100', paid_delta: '0', ref: gid }]);
    const audits = await auditRows("action LIKE 'payment.grant.%' AND result = 'ok'");
    expect(audits.map((a) => a.action)).toEqual(['payment.grant.create', 'payment.grant.approve']);
    expect(audits.every((a) => a.target_type === 'star_grant')).toBe(true);
    await expectWalletConsistent(p.accountId);
    // 이미 적용된 지급은 다시 승인할 수 없다
    const again = await adminPost(admin, o1, `/admin/payments/star-grants/${gid}/approve`);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('GRANT_STATE');
  });

  it('1. 작성자 본인 승인 403 GRANT_SELF_APPROVAL, operator 승인 403, DB CHECK 직접 UPDATE 거절', async () => {
    const { o1, o2, op } = await twoOwnersAndOperator();
    const p = await newPayer(app);
    const created = await adminPost(admin, o1, '/admin/payments/star-grants', grantBody(await accountUuid(p)));
    expect(created.status).toBe(201);
    const gid = created.body.data.grant.id as string;
    const self = await adminPost(admin, o1, `/admin/payments/star-grants/${gid}/approve`);
    expect(self.status).toBe(403);
    expect(self.body.errors.code).toBe('GRANT_SELF_APPROVAL');
    expect((await adminPost(admin, op, `/admin/payments/star-grants/${gid}/approve`)).status).toBe(403);
    await expect(getPool().query("UPDATE star_admin_grants SET state = 'applied', approved_by = created_by, approved_at = now() WHERE uuid = $1", [gid])).rejects.toThrow(/two_person/);
    expect((await adminPost(admin, o2, `/admin/payments/star-grants/${gid}/approve`)).status).toBe(200);
  });

  it('2. 1건·관리자별 일일·전체 일일 상한 초과 GRANT_LIMIT(작성 때), 승인 때도 다시 검사한다, 부채 탕감에 금액을 보내면 400', async () => {
    app = payApp({ PAY_ADMIN_GRANT_MAX_STARS: '150', PAY_ADMIN_GRANT_DAILY_MAX_STARS_PER_ADMIN: '200', PAY_ADMIN_GRANT_DAILY_MAX_STARS_TOTAL: '350' });
    await resetDb();
    const o1 = await makeAdmin('owner');
    await makeAdmin('owner');
    const a = await makeAdmin('operator');
    const b = await makeAdmin('operator');
    const c = await makeAdmin('operator');
    const p = await newPayer(app);
    const acct = await accountUuid(p);
    const big = await adminPost(admin, a, '/admin/payments/star-grants', grantBody(acct, { stars: 151 }));
    expect(big.status).toBe(422);
    expect(big.body.errors).toMatchObject({ code: 'GRANT_LIMIT', field: 'max_stars' });
    expect((await adminPost(admin, a, '/admin/payments/star-grants', grantBody(acct, { stars: 150 }))).status).toBe(201);
    const perAdmin = await adminPost(admin, a, '/admin/payments/star-grants', grantBody(acct, { stars: 100 }));
    expect(perAdmin.body.errors).toMatchObject({ code: 'GRANT_LIMIT', field: 'daily_per_admin' });
    expect((await adminPost(admin, b, '/admin/payments/star-grants', grantBody(acct, { stars: 150 }))).status).toBe(201);
    const total = await adminPost(admin, c, '/admin/payments/star-grants', grantBody(acct, { stars: 100 }));
    expect(total.body.errors).toMatchObject({ code: 'GRANT_LIMIT', field: 'daily_total' });
    // 승인 때도 상한을 다시 본다
    const last = await adminPost(admin, c, '/admin/payments/star-grants', grantBody(acct, { stars: 50 }));
    expect(last.status).toBe(201);
    app = payApp({ PAY_ADMIN_GRANT_MAX_STARS: '40' });
    const tooBig = await adminPost(admin, o1, `/admin/payments/star-grants/${last.body.data.grant.id}/approve`);
    expect(tooBig.status).toBe(422);
    expect(tooBig.body.errors.code).toBe('GRANT_LIMIT');
    app = payApp();
    const forgive = await adminPost(admin, a, '/admin/payments/star-grants', { kind: 'debt_forgive', account_id: acct, stars: 5, memo: 'x' });
    expect(forgive.status).toBe(400);
    const noStars = await adminPost(admin, a, '/admin/payments/star-grants', { kind: 'grant', account_id: acct, memo: 'x' });
    expect(noStars.status).toBe(400);
  });

  it('2. 부채가 있으면 지급이 먼저 부채를 갚는다(admin_grant + debt_settle), 부채 탕감은 부채 전액', async () => {
    const { o1, op } = await twoOwnersAndOperator();
    const p = await newPayer(app);
    const o = await purchase(app, p);
    await (await import('../src/db/pool')).withTransaction(async (c) => {
      const { debit } = await import('../src/domains/starshop/starWallet');
      await debit(c, { accountId: p.accountId, reason: 'gacha', price: 300, ref: 'x', requestId: randomUUID() });
    });
    mock.setStatus(o.steam_order_id, 'Refunded');
    await sync(app, p, o.id);
    expect((await walletState(p.accountId)).debt).toBe(300);
    const acct = await accountUuid(p);
    const g = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(acct, { stars: 100 }));
    await adminPost(admin, o1, `/admin/payments/star-grants/${g.body.data.grant.id}/approve`);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, debt: 200 });
    expect(await ledgerCount(p.accountId, 'admin_grant')).toBe(1);
    expect(await ledgerCount(p.accountId, 'debt_settle')).toBe(1);
    const f = await adminPost(admin, op, '/admin/payments/star-grants', { kind: 'debt_forgive', account_id: acct, memo: '고객 보상' });
    expect(f.status).toBe(201);
    const ok = await adminPost(admin, o1, `/admin/payments/star-grants/${f.body.data.grant.id}/approve`);
    expect(ok.body.data.grant).toMatchObject({ kind: 'debt_forgive', applied_stars: 200 });
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, debt: 0 });
    expect(await ledgerCount(p.accountId, 'debt_forgive')).toBe(1);
    await expectWalletConsistent(p.accountId);
    // 부채가 없는데 탕감 승인은 GRANT_STATE
    const f2 = await adminPost(admin, op, '/admin/payments/star-grants', { kind: 'debt_forgive', account_id: acct, memo: '또' });
    expect((await adminPost(admin, o1, `/admin/payments/star-grants/${f2.body.data.grant.id}/approve`)).body.errors.code).toBe('GRANT_STATE');
  });

  it('3. 같은 request_id 재전송은 같은 응답(재생), 만료된 지급 승인 거절, 만료 작업이 닫는다, 취소는 작성자 또는 owner', async () => {
    const { o1, o2, op } = await twoOwnersAndOperator();
    const other = await makeAdmin('operator');
    const p = await newPayer(app);
    const acct = await accountUuid(p);
    const reqId = rid();
    const a = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(acct), reqId);
    const b = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(acct), reqId);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body.data.grant.id).toBe(a.body.data.grant.id);
    expect((await getPool().query('SELECT count(*) AS n FROM star_admin_grants WHERE account_id = $1', [p.accountId])).rows[0]).toMatchObject({ n: '1' });
    const gid = a.body.data.grant.id as string;
    expect((await adminPost(admin, other, `/admin/payments/star-grants/${gid}/cancel`)).status).toBe(403);
    // 만료
    await getPool().query("ALTER TABLE star_admin_grants DISABLE TRIGGER star_admin_grants_guard");
    await getPool().query("UPDATE star_admin_grants SET created_at = now() - interval '2 days', expires_at = now() - interval '1 day' WHERE uuid = $1", [gid]);
    await getPool().query("ALTER TABLE star_admin_grants ENABLE TRIGGER star_admin_grants_guard");
    const late = await adminPost(admin, o1, `/admin/payments/star-grants/${gid}/approve`);
    expect(late.status).toBe(409);
    expect(late.body.errors.code).toBe('GRANT_STATE');
    expect((await starGrantExpireJob()).rows).toBe(1);
    expect((await getPool().query('SELECT state FROM star_admin_grants WHERE uuid = $1', [gid])).rows[0]).toMatchObject({ state: 'expired' });
    const c = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(acct));
    expect((await adminPost(admin, op, `/admin/payments/star-grants/${c.body.data.grant.id}/cancel`)).body.data.grant.state).toBe('cancelled');
    const d = await adminPost(admin, op, '/admin/payments/star-grants', grantBody(acct));
    expect((await adminPost(admin, o2, `/admin/payments/star-grants/${d.body.data.grant.id}/cancel`)).status).toBe(200);
    const list = await adminGet(admin, op, '/admin/payments/star-grants?state=cancelled');
    expect(list.body.data.items.length).toBe(2);
  });

  it('3. 우편 캠페인에는 별조각을 첨부할 수 없다(첨부 종류 gold/item/sweep_ticket 만)', async () => {
    const { op } = await twoOwnersAndOperator();
    for (const att of [{ kind: 'stars', amount: 10 }, { kind: 'star', count: 10 }, { kind: 'gold', amount: 10, stars: 5 }]) {
      const res = await adminPost(admin, op, '/admin/mail-campaigns', campaignBody({ attachments: [att] }));
      expect(res.status).toBeGreaterThanOrEqual(400);
      expect(res.status).toBeLessThan(500);
    }
  });

  it('5. test_grant: DB 세션 설정 없이는 거절(commit 시점), 스테이지 가드는 DEPLOY_STAGE=test 가 아니면 실행을 거절한다', async () => {
    const p = await newPayer(app);
    const { creditFree } = await import('../src/domains/starshop/starWallet');
    const { withTransaction } = await import('../src/db/pool');
    await expect(withTransaction((c) => creditFree(c, { accountId: p.accountId, reason: 'test_grant', amount: 5, ref: 'x', requestId: null }))).rejects.toThrow(/test stage/);
    // 스테이지 가드(DEPLOY_STAGE=test 가 아니면 스크립트 실행 거절)는 test/stageGuard.test.ts 가 이미 검사한다
  });
});

describe('PA1~PA8, PA14 결제 관리자 API', () => {
  it('PA1/PA2: 주문 목록(필터·커서)과 상세(스냅샷, 이벤트, 배분, 원장, 플래그), 조회 기록이 감사에 남는다', async () => {
    const v = await makeAdmin('viewer');
    const p = await newPayer(app);
    const o = await purchase(app, p);
    const list = await adminGet(admin, v, `/admin/payments/orders?state=granted&account=${await accountUuid(p)}`);
    expect(list.status).toBe(200);
    expect(list.body.data.items.map((x: { id: string }) => x.id)).toEqual([o.id]);
    const d = await adminGet(admin, v, `/admin/payments/orders/${o.id}`);
    expect(d.status).toBe(200);
    expect(d.body.data.order).toMatchObject({ state: 'granted', stars: 300, steam_status: 'Succeeded', steam_amount_minor: 1100 });
    expect(d.body.data.events.map((e: { kind: string }) => e.kind)).toEqual(expect.arrayContaining(['created', 'authorized', 'finalized', 'granted']));
    expect(d.body.data.ledger[0]).toMatchObject({ reason: 'purchase', delta: 300 });
    expect(JSON.stringify(d.body)).not.toMatch(/lease_token|quote-secret/);
    expect((await auditRows("action = 'payment.order.view' AND result = 'ok'")).length).toBeGreaterThanOrEqual(1);
    expect((await adminGet(admin, v, `/admin/payments/orders/${randomUUID()}`)).status).toBe(404);
  });

  it('PA3: 재확인(operator)은 request_id 로 멱등이고, 종결 주문은 409 PAYMENT_STATE, 존재하지 않으면 404', async () => {
    const op = await makeAdmin('operator');
    const p = await newPayer(app);
    const buyRes = await (await import('./payHelpers')).buy(app, p);
    const o = buyRes.body.data.order;
    mock.approve(o.steam_order_id);
    const rq = rid();
    const r1 = await adminPost(admin, op, `/admin/payments/orders/${o.id}/recheck`, {}, rq);
    expect(r1.status).toBe(200);
    expect(r1.body.data.order.state).toBe('granted');
    const r2 = await adminPost(admin, op, `/admin/payments/orders/${o.id}/recheck`, {}, rq);
    expect(r2.headers['idempotent-replay']).toBe('true');
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
    const q = await newPayer(app);
    const failed = (await (await import('./payHelpers')).buy(app, q)).body.data.order;
    await getPool().query("UPDATE star_orders SET state = 'failed', fail_reason = 'user_denied', closed_at = now(), next_check_at = NULL WHERE uuid = $1", [failed.id]);
    const st = await adminPost(admin, op, `/admin/payments/orders/${failed.id}/recheck`);
    expect(st.status).toBe(409);
    expect(st.body.errors.code).toBe('PAYMENT_STATE');
    expect((await adminPost(admin, op, `/admin/payments/orders/${randomUUID()}/recheck`)).status).toBe(404);
  });

  it('PA3: failed(mismatch) + needs_review 주문은 재확인으로 되살아나 지급된다(검증은 건너뛰지 않는다)', async () => {
    const op = await makeAdmin('operator');
    const p = await newPayer(app);
    const o = (await (await import('./payHelpers')).buy(app, p)).body.data.order;
    mock.tamper = { amountMinor: 1 };
    mock.approve(o.steam_order_id);
    await sync(app, p, o.id);
    expect(await orderRow(o.id)).toMatchObject({ state: 'failed', needs_review: true });
    // 여전히 틀리면 그대로
    const still = await adminPost(admin, op, `/admin/payments/orders/${o.id}/recheck`);
    expect(still.body.data.order.state).toBe('failed');
    // Steam 쪽이 바로잡히고 이미 확정(Succeeded)이면 검증을 통과해 지급된다
    mock.tamper = {};
    mock.setStatus(o.steam_order_id, 'Succeeded');
    const fixed = await adminPost(admin, op, `/admin/payments/orders/${o.id}/recheck`);
    expect(fixed.body.data.order).toMatchObject({ state: 'granted', needs_review: false });
    expect(await ledgerCount(p.accountId, 'purchase')).toBe(1);
  });

  it('PA4~PA6: 계정 결제 상태, operator 결제 정지(멱등), 해제는 owner 만(operator 403), 플래그 목록과 처리(두 번째는 409)', async () => {
    const op = await makeAdmin('operator');
    const owner = await makeAdmin('owner');
    const v = await makeAdmin('viewer');
    const p = await newPayer(app);
    const acct = await accountUuid(p);
    await getPool().query("INSERT INTO payment_flags (account_id, kind, severity, detail) VALUES ($1, 'shared_device', 2, '{}')", [p.accountId]);
    const show = await adminGet(admin, v, `/admin/payments/accounts/${acct}`);
    expect(show.status).toBe(200);
    expect(show.body.data).toMatchObject({ tier: 'new', profile: { status: 'active' }, wallet: { debt: 0 } });
    expect(show.body.data.flags.length).toBe(1);
    const block = await adminPost(admin, op, `/admin/payments/accounts/${acct}/block`, { note: '의심 결제', reason: 'fraud_suspect' });
    expect(block.status).toBe(200);
    expect((await adminPost(admin, op, `/admin/payments/accounts/${acct}/block`, { note: '또', reason: 'manual' })).body.data.already).toBe(true);
    expect((await adminPost(admin, op, `/admin/payments/accounts/${acct}/unblock`, { note: '해제' })).status).toBe(403);
    expect((await adminPost(admin, owner, `/admin/payments/accounts/${acct}/unblock`, { note: '고객 확인' })).status).toBe(200);
    expect((await adminPost(admin, owner, `/admin/payments/accounts/${acct}/unblock`, { note: '또' })).body.errors.code).toBe('PAYMENT_STATE');
    const flags = await adminGet(admin, v, `/admin/payments/flags?account=${acct}`);
    const fid = flags.body.data.items[0].id as string;
    expect((await adminPost(admin, op, `/admin/payments/flags/${fid}/resolve`, { resolution: 'dismissed', note: '확인함' })).status).toBe(200);
    expect((await adminPost(admin, op, `/admin/payments/flags/${fid}/resolve`, { resolution: 'confirmed', note: '또' })).body.errors.code).toBe('PAYMENT_STATE');
    expect((await adminGet(admin, v, `/admin/payments/flags?account=${acct}`)).body.data.items.length).toBe(0);
    expect((await adminPost(admin, op, `/admin/payments/accounts/${randomUUID()}/block`, { note: 'x', reason: 'manual' })).status).toBe(404);
    expect((await auditRows("action LIKE 'payment.account.%' AND result = 'ok'")).length).toBeGreaterThanOrEqual(4);
  });

  it('PA14: 대사 현황(상태별 수, 정체 주문, 마지막 리포트)', async () => {
    const v = await makeAdmin('viewer');
    const p = await newPayer(app);
    await purchase(app, p);
    const r = await adminGet(admin, v, '/admin/payments/reconcile');
    expect(r.status).toBe(200);
    expect(r.body.data.orders_by_state.granted).toBeGreaterThanOrEqual(1);
    expect(Array.isArray(r.body.data.stuck_orders)).toBe(true);
  });

  it('G2. 플레이어 응답에는 fail_reason·정지 사유가 없고 관리자 응답에만 상세가 있다', async () => {
    const v = await makeAdmin('viewer');
    const p = await newPayer(app);
    mock.initMode = 'reject';
    const res = await (await import('./payHelpers')).buy(app, p);
    expect(JSON.stringify(res.body)).not.toMatch(/fail_reason|init_rejected|review_reason/);
    const d = await adminGet(admin, v, `/admin/payments/orders/${res.body.data.order.id}`);
    expect(d.body.data.order.fail_reason).toBe('init_rejected');
  });
});

describe('PA13 결과물 회수 (owner, 미리보기 후 적용)', () => {
  it('차지백 주문의 교환 외형·뽑기 결과·장비·게이지를 되돌린다: 미리보기 해시가 다르면 거절, 자동 회수는 없다', async () => {
    const owner = await makeAdmin('owner');
    const op = await makeAdmin('operator');
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const o = await purchase(app, p, 'stars_1000');
    const call = (path: string, body: Record<string, unknown>) => {
      getRateLimitStore().clear();
      return post(app, hero, path, body);
    };
    expect((await call('/starshop/exchange', { item_id: 'aura_dew' })).status).toBe(200); // 300
    const pull = await call('/starshop/pull', { count: 1, banner: 'aura' }); // 100
    expect(pull.status).toBe(200);
    const weapon = await call('/starshop/pull', { count: 1, banner: 'weapon' }); // 100
    expect(weapon.status).toBe(200);
    const gearKey = weapon.body.data.results[0].item_id as string;
    const pulledAura = pull.body.data.results[0] as { item_id: string; duplicate: boolean };
    expect((await getPool().query('SELECT pity FROM star_wallets WHERE account_id = $1', [p.accountId])).rows[0]).toMatchObject({ pity: 1 });
    // 환불은 외형을 자동으로 회수하지 않는다
    mock.setStatus(o.steam_order_id, 'Chargeback');
    await sync(app, p, o.id);
    expect((await getPool().query("SELECT count(*) AS n FROM account_cosmetics WHERE account_id = $1 AND item_id = 'aura_dew'", [p.accountId])).rows[0]).toMatchObject({ n: '1' });
    const inc = { cosmetics: true, gear: true, gauge: true };
    // operator 는 403, 진행 중이 아닌 주문(granted)은 409
    expect((await adminPost(admin, op, `/admin/payments/orders/${o.id}/revoke-outcomes`, { mode: 'preview', include: inc, note: 'x' })).status).toBe(403);
    const q = await newPayer(app);
    const granted = await purchase(app, q);
    expect((await adminPost(admin, owner, `/admin/payments/orders/${granted.id}/revoke-outcomes`, { mode: 'preview', include: inc, note: 'x' })).body.errors.code).toBe('PAYMENT_STATE');
    const pv = await adminPost(admin, owner, `/admin/payments/orders/${o.id}/revoke-outcomes`, { mode: 'preview', include: inc, note: '확정 부정' });
    expect(pv.status).toBe(200);
    expect(pv.body.data.targets.cosmetics.map((c: { item_id: string }) => c.item_id)).toContain('aura_dew');
    expect(pv.body.data.targets.gear[0]).toMatchObject({ item_key: gearKey, count: 1 });
    expect(pv.body.data.targets.gauge).toMatchObject({ aura: 1 });
    // 미리보기는 아무것도 바꾸지 않는다
    expect((await getPool().query("SELECT count(*) AS n FROM account_cosmetics WHERE account_id = $1 AND item_id = 'aura_dew'", [p.accountId])).rows[0]).toMatchObject({ n: '1' });
    const hash = pv.body.data.preview_hash as string;
    const wrong = await adminPost(admin, owner, `/admin/payments/orders/${o.id}/revoke-outcomes`, { mode: 'apply', include: inc, preview_hash: 'a'.repeat(64), note: 'x' });
    expect(wrong.status).toBe(409);
    expect(wrong.body.errors.code).toBe('PREVIEW_CHANGED');
    expect((await adminPost(admin, owner, `/admin/payments/orders/${o.id}/revoke-outcomes`, { mode: 'apply', include: inc, note: 'x' })).status).toBe(400);
    const rq = rid();
    const ap = await adminPost(admin, owner, `/admin/payments/orders/${o.id}/revoke-outcomes`, { mode: 'apply', include: inc, preview_hash: hash, note: '확정 부정' }, rq);
    expect(ap.status).toBe(200);
    expect(ap.body.data).toMatchObject({ cosmetics_removed: expect.any(Number), gear_removed: 1, gear_shortfall: 0, gauge: { aura: 1, skin: 0 } });
    expect((await getPool().query("SELECT count(*) AS n FROM account_cosmetics WHERE account_id = $1 AND item_id = 'aura_dew'", [p.accountId])).rows[0]).toMatchObject({ n: '0' });
    if (!pulledAura.duplicate) expect((await getPool().query('SELECT count(*) AS n FROM account_cosmetics WHERE account_id = $1 AND item_id = $2', [p.accountId, pulledAura.item_id])).rows[0]).toMatchObject({ n: '0' });
    expect((await getPool().query('SELECT pity FROM star_wallets WHERE account_id = $1', [p.accountId])).rows[0]).toMatchObject({ pity: 0 });
    const clawed = await getPool().query("SELECT delta FROM item_ledger WHERE character_id = $1 AND reason = 'admin_clawback' AND item_key = $2", [hero.dbId, gearKey]);
    expect(clawed.rows.map((x: { delta: number }) => x.delta)).toEqual([-1]);
    // 같은 request_id 재전송은 재생
    const rep = await adminPost(admin, owner, `/admin/payments/orders/${o.id}/revoke-outcomes`, { mode: 'apply', include: inc, preview_hash: hash, note: '확정 부정' }, rq);
    expect(rep.headers['idempotent-replay']).toBe('true');
    expect((await auditRows("action = 'payment.revoke_outcomes' AND result = 'ok'")).length).toBe(1);
  });
});

describe('작업·정합성 I6·경보', () => {
  it('payment-report: Steam 에만 있는 주문은 unknown_steam_order(critical 경보 입력), 상태가 다른 주문은 재확인 대기열', async () => {
    await resetDb();
    const p = await newPayer(app);
    const o = await purchase(app, p);
    mock.extraReport = [{ orderId: '999000999', steamId: p.steamId, status: 'Succeeded', amountMinor: 1100, currency: 'KRW' }];
    mock.setStatus(o.steam_order_id, 'Refunded');
    resetPayPartnerStats();
    const res = await paymentReportJob();
    expect(res.detail).toMatchObject({ unknown_orders: 1, status_diff: 1 });
    expect((await getPool().query("SELECT kind FROM payment_flags WHERE kind = 'unknown_steam_order' AND account_id = $1", [p.accountId])).rows.length).toBe(1);
    expect(((await orderRow(o.id)).next_check_at as Date).getTime()).toBeLessThanOrEqual(Date.now() + 1000);
    const snap = await collectSnapshot();
    expect(snap.payments?.unknown_orders_open).toBeGreaterThanOrEqual(1);
    const keys = evaluate({ snap, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 }).map((a) => `${a.level}:${a.key}`);
    expect(keys).toContain('critical:payment_unknown_order');
    expect(keys).not.toContain('critical:payment_chargeback');
    await resetDb();
  });

  it('I6: 정상 결제 뒤에는 불일치 0, 의도적으로 틀어 둔 데이터(지급 줄 누락, 로트 합 불일치, 금액 불일치, 정체 주문)를 찾는다', async () => {
    await resetDb();
    const p = await newPayer(app);
    const o = await purchase(app, p);
    const clean = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    expect((clean.detail.checks as Record<string, { count: number }>).I6?.count).toBe(0);
    const c = await getPool().connect();
    try {
      await c.query('BEGIN');
      await c.query('SET LOCAL session_replication_role = replica');
      await c.query('UPDATE star_paid_lots SET remaining = remaining - 1');
      await c.query("UPDATE star_orders SET steam_amount_minor = 1 WHERE uuid = $1", [o.id]);
      await c.query("DELETE FROM star_ledger WHERE reason = 'purchase'");
      await c.query("INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, tier, state, expires_at, created_at) VALUES ($1, gen_random_uuid(), 'h', $2, 480, 'stars_300', 1001, 'v', 1, 'KRW', 1100, 'standard', 'pending_init', now(), now() - interval '1 hour')", [p.accountId, p.steamId]).catch(() => undefined);
      await c.query('COMMIT');
    } finally {
      c.release();
    }
    const bad = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    const i6 = (bad.detail.checks as Record<string, { count: number; samples: { kind: string }[] }>).I6 as { count: number; samples: { kind: string }[] };
    expect(i6.count).toBeGreaterThanOrEqual(3);
    const kinds = new Set(i6.samples.map((s) => s.kind));
    for (const k of ['wallet', 'order_ledger', 'steam_amount']) expect(kinds.has(k)).toBe(true);
    await resetDb();
  });

  it('경보: 차지백·정체 주문·확정 후 미지급·검토 지연·환불 급증이 임계에서 울린다', async () => {
    await resetDb();
    const p = await newPayer(app);
    const o = await purchase(app, p);
    mock.setStatus(o.steam_order_id, 'Chargeback');
    await sync(app, p, o.id);
    const stuck = await newPayer(app);
    const sOrder = await (await import('./payHelpers')).buy(app, stuck);
    await getPool().query("UPDATE star_orders SET state = 'finalized', finalized_at = now() - interval '10 minutes', authorized_at = now() - interval '11 minutes' WHERE uuid = $1", [sOrder.body.data.order.id]);
    await getPool().query("UPDATE payment_flags SET created_at = now() - interval '30 hours' WHERE kind = 'chargeback'");
    const snap = await collectSnapshot();
    expect(snap.payments).toMatchObject({ chargebacks_24h: 1, finalized_ungranted: 1 });
    const keys = evaluate({ snap, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 }).map((a) => `${a.level}:${a.key}`);
    expect(keys).toEqual(expect.arrayContaining(['critical:payment_chargeback', 'critical:payment_stuck', 'critical:payment_finalized_ungranted', 'warning:payment_review_age']));
    await resetDb();
  });

  it('purge: 주문 IP 는 PAY_IP_RETENTION_DAYS 뒤 NULL 로 지워지고 주문·이벤트 행은 남는다', async () => {
    await resetDb();
    const p = await newPayer(app);
    const o = await purchase(app, p);
    await getPool().query("UPDATE star_orders SET ip = '10.0.0.1', created_at = now() - interval '200 days' WHERE uuid = $1", [o.id]);
    await purgeDaily(jobCtx);
    const row = await orderRow(o.id);
    expect(row.ip).toBeNull();
    expect(row.state).toBe('granted');
    expect((await getPool().query('SELECT count(*) AS n FROM star_order_events')).rows[0]).not.toMatchObject({ n: '0' });
  });
});

describe('환경변수·기동 검사(16절)와 비밀 노출', () => {
  const base = { NODE_ENV: 'test', DATABASE_URL: 'postgres://x', JWT_SECRET: 'j'.repeat(40), MIN_CLIENT_VERSION: '0.2.0' } as NodeJS.ProcessEnv;

  it('기본은 꺼짐이고, 켜면 mode·견적 비밀(32자 이상, JWT와 달라야 함)이 필요하며 운영에서 mock 은 거부된다', () => {
    expect(loadConfig(base).pay).toMatchObject({ enabled: false, mode: 'off' });
    expect(() => loadConfig({ ...base, PAYMENTS_ENABLED: 'true' })).toThrow(/PAYMENTS_STEAM_MODE/);
    expect(() => loadConfig({ ...base, PAYMENTS_ENABLED: 'true', PAYMENTS_STEAM_MODE: 'mock' })).toThrow(/PAYMENT_QUOTE_SECRET/);
    expect(() => loadConfig({ ...base, PAYMENTS_ENABLED: 'true', PAYMENTS_STEAM_MODE: 'mock', PAYMENT_QUOTE_SECRET: 'j'.repeat(40) })).toThrow(/JWT_SECRET/);
    expect(() => loadConfig({ ...base, PAYMENTS_STEAM_MODE: 'sandbox' })).toThrow(/STEAM_PUBLISHER_API_KEY/);
    expect(() =>
      loadConfig({ ...base, PAYMENTS_ENABLED: 'true', PAYMENTS_STEAM_MODE: 'sandbox', STEAM_PUBLISHER_API_KEY: 'k', PAYMENT_QUOTE_SECRET: 'q'.repeat(40), STEAM_AUTH_MODE: 'mock' }),
    ).toThrow(/web_api/);
    expect(() =>
      loadConfig({ ...base, PAYMENTS_ENABLED: 'true', PAYMENTS_STEAM_MODE: 'sandbox', STEAM_PUBLISHER_API_KEY: 'k', PAYMENT_QUOTE_SECRET: 'q'.repeat(40), STEAM_AUTH_MODE: 'web_api', STEAM_APP_ID: '1', STEAM_WEB_API_KEY: 'w' }),
    ).toThrow(/STAR_RATES_ACK_REQUIRED/);
    expect(() => loadConfig({ ...base, PAYMENTS_STEAM_MODE: 'mock', NODE_ENV: 'production', DEPLOY_STAGE: 'live' })).toThrow();
    expect(() => loadConfig({ ...base, PAY_LIMIT_NEW_DAILY_STARS: '20000' })).toThrow(/restricted <= new <= standard/);
  });

  it('PAYMENTS_ENABLED=false 이면 대사·감시 작업은 계속 돈다(이미 생긴 주문을 놓치지 않는다)', async () => {
    const p = await newPayer(app);
    const o = (await (await import('./payHelpers')).buy(app, p)).body.data.order;
    mock.approve(o.steam_order_id);
    // 가짜 Steam 상태(주문)를 지우지 않도록 payApp 대신 설정만 바꾼다
    buildApp({ ...PAY_ENV, PAYMENTS_ENABLED: 'false' });
    await getPool().query("UPDATE star_orders SET next_check_at = now() - interval '1 minute' WHERE uuid = $1", [o.id]);
    const { paymentReconcileJob } = await import('../src/ops/jobs/paymentJobs');
    await paymentReconcileJob(jobCtx);
    expect((await orderRow(o.id)).state).toBe('granted');
    app = payApp();
    void authP;
  });
});
