// 11단계 결제: 별조각 소비 경로 강화(설계 18절 E). 무료분 먼저·유료 FIFO, 동시 소비, 일일 소비 상한, 확률표 버전 확인
import { randomUUID } from 'node:crypto';
import { getPool, withTransaction } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { debit } from '../src/domains/starshop/starWallet';
import * as starshop from '../src/domains/starshop/starshopService';
import { exchangePriceOf, RATES_VERSION, STAR_COSMETIC_BY_ID } from '../src/domains/starshop/starshopDefs';
import { adminApp } from './opsHelpers';
import { post } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { expectWalletConsistent, heroOf, ledgerCount, newPayer, payApp, purchase, resetPay, seedStars, walletState } from './payHelpers';

void adminApp;
let app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

describe('E. 소비 강화', () => {
  it('1. 무료분 먼저, 유료분은 오래된 로트부터(FIFO): 배분 줄 합 = -paid_delta, 로트 remaining 합 = paid_balance', async () => {
    const p = await newPayer(app);
    await seedStars(p.accountId, 100);
    const a = await purchase(app, p);
    const b = await purchase(app, p);
    expect(await walletState(p.accountId)).toMatchObject({ balance: 700, paid: 600 });
    await withTransaction((c) => debit(c, { accountId: p.accountId, reason: 'exchange', price: 450, ref: 'aura_dew', requestId: randomUUID() }));
    // 무료 100, A 300, B 50
    const lots = await getPool().query<{ uuid: string; remaining: number }>('SELECT o.uuid, l.remaining FROM star_paid_lots l JOIN star_orders o ON o.id = l.order_id ORDER BY l.created_at');
    expect(lots.rows.map((x) => [x.uuid, x.remaining])).toEqual([[a.id, 0], [b.id, 250]]);
    const allocs = await getPool().query<{ stars: number }>("SELECT a.stars FROM star_spend_allocs a JOIN star_ledger l ON l.id = a.ledger_id WHERE l.reason = 'exchange' ORDER BY a.id");
    expect(allocs.rows.map((x) => x.stars)).toEqual([300, 50]);
    const line = await getPool().query<{ paid_delta: string; delta: string }>("SELECT paid_delta, delta FROM star_ledger WHERE reason = 'exchange'");
    expect(line.rows[0]).toMatchObject({ paid_delta: '-350', delta: '-450' });
    expect(await walletState(p.accountId)).toMatchObject({ balance: 250, paid: 250, lots: 250 });
    await expectWalletConsistent(p.accountId);
  });

  it('2. 동시 소비: 잔액 1,000으로 10+1 뽑기 20개 동시 -> 정확히 1개 성공, 음수 없음, 배분 줄 중복 없음', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await purchase(app, p, 'stars_1000');
    const rs = await Promise.allSettled(Array.from({ length: 20 }, () => starshop.pull(p.accountId, hero.id, { request_id: randomUUID(), count: 10 })));
    expect(rs.filter((r) => r.status === 'fulfilled').length).toBe(1);
    for (const r of rs.filter((x): x is PromiseRejectedResult => x.status === 'rejected')) expect((r.reason as { code?: string }).code).toBe('NOT_ENOUGH_STARS');
    expect(await walletState(p.accountId)).toMatchObject({ balance: 0, paid: 0, lots: 0 });
    expect(await ledgerCount(p.accountId, 'gacha')).toBe(1);
    const allocs = await getPool().query<{ n: string; s: string }>('SELECT count(*) AS n, sum(a.stars) AS s FROM star_spend_allocs a JOIN star_ledger l ON l.id = a.ledger_id WHERE l.account_id = $1', [p.accountId]);
    expect(allocs.rows[0]).toMatchObject({ n: '1', s: '1000' });
    await expectWalletConsistent(p.accountId);
  });

  it('3. 일일 소비 상한: 상한 직전까지 성공, 초과 STAR_SPEND_CAP(limit/used/resets_at), 06:00 KST 경계에서 초기화, 0 = 끔', async () => {
    app = payApp({ STAR_SPEND_DAILY_CAP: '300' });
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 5000);
    for (let i = 0; i < 3; i++) {
      getRateLimitStore().clear();
      expect((await post(app, hero, '/starshop/pull', { count: 1 })).status).toBe(200);
    }
    getRateLimitStore().clear();
    const over = await post(app, hero, '/starshop/pull', { count: 1 });
    expect(over.status).toBe(422);
    expect(over.body.errors).toMatchObject({ code: 'STAR_SPEND_CAP', limit: 300, used: 300 });
    expect(Date.parse(over.body.errors.resets_at)).not.toBeNaN();
    getRateLimitStore().clear();
    // 뽑기로 이미 얻은 외형이면 소유 검사(409)가 먼저라서, 가지지 않은 교환 가능 외형을 고른다
    const ownedRows = await getPool().query<{ item_id: string }>('SELECT item_id FROM account_cosmetics WHERE account_id = $1', [p.accountId]);
    const owned = new Set(ownedRows.rows.map((r) => r.item_id));
    const target = [...STAR_COSMETIC_BY_ID.values()].find((c) => !owned.has(c.id) && exchangePriceOf(c) > 0)!;
    expect((await post(app, hero, '/starshop/exchange', { item_id: target.id })).status).toBe(422);
    // 요약에 상한이 보인다
    const sum = await (await import('supertest')).default(app).get(`/characters/${hero.id}/starshop`).set((await import('./payHelpers')).authP(p));
    expect(sum.body.data.spend_cap).toMatchObject({ limit: 300, used: 300, left: 0 });
    // 다음 06:00 KST 이후에는 새 하루
    setClockOverride(() => new Date(Date.now() + 25 * 3_600_000));
    getRateLimitStore().clear();
    expect((await post(app, hero, '/starshop/pull', { count: 1 })).status).toBe(200);
    setClockOverride(null);
    // 상한 0 = 끔
    app = payApp({ STAR_SPEND_DAILY_CAP: '0' });
    for (let i = 0; i < 5; i++) {
      getRateLimitStore().clear();
      expect((await post(app, hero, '/starshop/pull', { count: 1 })).status).toBe(200);
    }
    app = payApp();
  });

  it('3. 계정당 분당 소비 속도 제한(RATE_STARSHOP_SPEND_PER_MIN)', async () => {
    app = payApp({ RATE_STARSHOP_SPEND_PER_MIN: '2' });
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 1000);
    const codes: number[] = [];
    for (let i = 0; i < 3; i++) {
      const res = await post(app, hero, '/starshop/pull', { count: 1 });
      codes.push(res.status);
      await new Promise((r) => setTimeout(r, 1100));
    }
    expect(codes).toEqual([200, 200, 429]);
    app = payApp();
  });

  it('4. 확률표: 클라이언트가 본 버전과 다르면 RATES_CHANGED(409), STAR_RATES_ACK_REQUIRED 면 없는 요청은 400, 뽑기 행에 버전이 남는다', async () => {
    app = payApp({ STAR_RATES_ACK_REQUIRED: 'true' });
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 1000);
    const missing = await post(app, hero, '/starshop/pull', { count: 1 });
    expect(missing.status).toBe(400);
    getRateLimitStore().clear();
    const wrong = await post(app, hero, '/starshop/pull', { count: 1, rates_version: '1999-01-01.0' });
    expect(wrong.status).toBe(409);
    expect(wrong.body.errors).toMatchObject({ code: 'RATES_CHANGED', rates_version: RATES_VERSION });
    getRateLimitStore().clear();
    const ok = await post(app, hero, '/starshop/pull', { count: 1, rates_version: RATES_VERSION });
    expect(ok.status).toBe(200);
    expect(ok.body.data.rates_version).toBe(RATES_VERSION);
    expect(ok.body.data.results[0]).toHaveProperty('item_id');
    const row = await getPool().query('SELECT rates_version FROM gacha_pulls WHERE account_id = $1', [p.accountId]);
    expect(row.rows[0]).toMatchObject({ rates_version: RATES_VERSION });
    app = payApp();
  });

  it('gacha_refund 원장은 더 이상 쓰이지 않는다(뽑기 결과의 refund 필드는 그대로 0)', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 4000);
    for (let i = 0; i < 4; i++) {
      getRateLimitStore().clear();
      const r = await post(app, hero, '/starshop/pull', { count: 10 });
      expect(r.status).toBe(200);
      expect(r.body.data.results.every((x: { refund: number }) => x.refund === 0)).toBe(true);
    }
    expect(await ledgerCount(p.accountId, 'gacha_refund')).toBe(0);
  });

  it('경제 정지 중 분해 거절, 장비 뽑기 결과는 계정 귀속(account)', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 500);
    const pull = await post(app, hero, '/starshop/pull', { count: 1, banner: 'weapon' });
    expect(pull.status).toBe(200);
    const key = pull.body.data.results[0].item_id as string;
    const bind = await getPool().query<{ bind: string }>("SELECT bind FROM character_items WHERE character_id = $1 AND item_key = $2 AND location = 'bag'", [hero.dbId, key]);
    expect(bind.rows.map((x) => x.bind)).toContain('account');
    await getPool().query("INSERT INTO account_cosmetics (account_id, item_id, source, copies) VALUES ($1, 'aura_dew', 'gacha', 2) ON CONFLICT (account_id, item_id) DO UPDATE SET copies = 2", [p.accountId]);
    await getPool().query("INSERT INTO economy_holds (account_id, kind, state, reviewed_at) VALUES ($1, 'manual', 'active', now())", [p.accountId]);
    getRateLimitStore().clear();
    const d = await post(app, hero, '/starshop/dismantle', { item_id: 'aura_dew', count: 1 });
    expect(d.status).toBe(403);
    expect(d.body.errors.code).toBe('ECONOMY_HOLD');
  });
});
