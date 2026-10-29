// 별의 결정 -> 무료 별조각 바꾸기(crystalConvert.ts): 비율·내림, 멱등, 동시 요청, 결정 부족, 유료 결정도 무료 별조각, 되돌이 상한
import { randomUUID } from 'node:crypto';
import { getPool, withTransaction } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { creditCrystals } from '../src/domains/starshop/crystalWallet';
import { CRYSTALS_PER_STAR, convertPlan } from '../src/domains/starshop/crystalConvert';
import { slotTable } from '../src/domains/starshop/starshopService';
import {
  COLLECTION_DUP_BONUS,
  GEAR_CEILING,
  GEAR_DISMANTLE,
  GEAR_DISMANTLE_ENHANCE_BONUS,
  GEAR_RARITY_PERMILLE,
  GEAR_RATE_SCALE,
  RATE_SCALE,
  TEN_COUNT,
  TEN_PRICE,
  dismantleCrystalsOf,
  dismantleStarsOf,
} from '../src/domains/starshop/starshopDefs';
import { get, post } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { expectWalletConsistent, heroOf, ledgerCount, newPayer, payApp, purchase, resetPay, walletState } from './payHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterAll(shutdown);

const go = (h: Parameters<typeof post>[1], body: Record<string, unknown>, requestId?: string) => {
  getRateLimitStore().clear();
  return post(app, h, '/starshop/crystal-convert', body, requestId);
};
const crystalsOf = async (accountId: number) => {
  const r = await getPool().query<{ balance: string; paid_balance: string; debt: string }>('SELECT balance, paid_balance, debt FROM crystal_wallets WHERE account_id = $1', [accountId]);
  const x = r.rows[0];
  return { balance: Number(x?.balance ?? 0), paid: Number(x?.paid_balance ?? 0), debt: Number(x?.debt ?? 0) };
};
const grantCrystals = (accountId: number, amount: number) =>
  withTransaction((c) => creditCrystals(c, { accountId, amount, reason: 'test_grant', ref: 'test', requestId: null }));
const crystalLedger = async (accountId: number, reason: string) =>
  Number((await getPool().query<{ n: string }>('SELECT count(*) AS n FROM crystal_ledger WHERE account_id = $1 AND reason = $2', [accountId, reason])).rows[0]?.n ?? 0);

describe('별의 결정 -> 별조각 바꾸기', () => {
  it('비율과 내림: 결정 2개 = 별조각 1개, 남는 결정은 쓰지 않는다', () => {
    expect(CRYSTALS_PER_STAR).toBe(2);
    expect(convertPlan(1)).toEqual({ stars: 0, crystals: 0 });
    expect(convertPlan(2)).toEqual({ stars: 1, crystals: 2 });
    expect(convertPlan(7)).toEqual({ stars: 3, crystals: 6 });
    expect(convertPlan(1000)).toEqual({ stars: 500, crystals: 1000 });
  });

  it('정상: 결정을 내고 무료 별조각을 받는다(양쪽 원장, 홀수는 내림), 조회에 비율이 나온다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await grantCrystals(p.accountId, 101);
    const r = await go(hero, { crystals: 101 });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ crystals: 100, stars: 50, per_star: 2, crystal_balance: 1, balance: 50 });
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 1, paid: 0 });
    expect(await walletState(p.accountId)).toMatchObject({ balance: 50, paid: 0 });
    expect(await ledgerCount(p.accountId, 'crystal_convert')).toBe(1);
    expect(await crystalLedger(p.accountId, 'convert_stars')).toBe(1);
    await expectWalletConsistent(p.accountId);
    const view = await get(app, hero, '/starshop/exchange-shop');
    expect(view.body.data.convert).toEqual({ per_star: 2 });
  });

  it('입력 오류: 결정 1개는 바꿀 수 없고(422), 0·소수·문자열은 검증에서 막힌다(400)', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await grantCrystals(p.accountId, 10);
    const small = await go(hero, { crystals: 1 });
    expect(small.status).toBe(422);
    expect(small.body.errors.code).toBe('CONVERT_TOO_SMALL');
    for (const bad of [0, 2.5, '10']) expect((await go(hero, { crystals: bad })).status).toBe(400);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 10 });
  });

  it('결정 부족: 거절하고 아무것도 바뀌지 않는다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await grantCrystals(p.accountId, 10);
    const r = await go(hero, { crystals: 12 });
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('NOT_ENOUGH_CRYSTALS');
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 10 });
    expect((await walletState(p.accountId)).balance).toBe(0);
  });

  it('재전송: 같은 request_id 두 번은 한 번만 바꾸고 같은 결과를 돌려준다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await grantCrystals(p.accountId, 100);
    const rid = randomUUID();
    const a = await go(hero, { crystals: 40 }, rid);
    const b = await go(hero, { crystals: 40 }, rid);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(b.body.data).toEqual(a.body.data);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 60 });
    expect((await walletState(p.accountId)).balance).toBe(20);
    expect(await ledgerCount(p.accountId, 'crystal_convert')).toBe(1);
  });

  it('동시 요청 2개: 잔액이 한 번 몫뿐이면 하나만 성공한다', async () => {
    const p = await newPayer(app);
    const first = await heroOf(app, p);
    const second = await heroOf(app, p);
    await grantCrystals(p.accountId, 30);
    const replies = await Promise.all([first, second].map((h) => go(h, { crystals: 30 })));
    expect(replies.map((r) => r.status).sort()).toEqual([200, 422]);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 0 });
    expect((await walletState(p.accountId)).balance).toBe(15);
    await expectWalletConsistent(p.accountId);
  });

  it('유료 별조각에서 나온 결정도 무료 별조각으로만 들어가고, 결정 쪽은 주문 몫에서 빠진다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await purchase(app, p); // 300 유료
    for (let i = 0; i < 3; i++) expect((await post(app, hero, '/starshop/pull', { count: 1, banner: 'aura' })).status).toBe(200);
    await getPool().query(
      `INSERT INTO account_cosmetics (account_id, item_id, source, copies) VALUES ($1, 'aura_rainbow', 'grant', 1)
       ON CONFLICT (account_id, item_id) DO UPDATE SET copies = account_cosmetics.copies + 1`,
      [p.accountId],
    );
    getRateLimitStore().clear();
    expect((await post(app, hero, '/starshop/dismantle', { item_id: 'aura_rainbow', count: 1 })).status).toBe(200);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 60, paid: 60 });
    const before = await walletState(p.accountId);
    const r = await go(hero, { crystals: 60 });
    expect(r.status).toBe(200);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 0, paid: 0 });
    const after = await walletState(p.accountId);
    expect(after.balance - before.balance).toBe(30);
    expect(after.paid).toBe(before.paid);
    await expectWalletConsistent(p.accountId);
  });

  it('되돌이 상한: 모든 결과를 분해하고 결정을 바꿔도 별조각 1개당 돌아오는 몫은 50% 미만이다', () => {
    const toStars = (stars: number, crystals: number) => stars + crystals / CRYSTALS_PER_STAR;
    // 외형 뽑기: 11회 묶음(1,000)의 보너스 1회는 일반 보통 칸을 빼고 나머지를 키운다. 컬렉션 +20%를 늘 받는다고 둔다
    for (const banner of ['aura', 'skin'] as const) {
      for (const cls of ['warrior', 'mage', 'rogue']) {
        const rows = slotTable(banner, cls);
        const pull = (keep: (r: (typeof rows)[number]) => boolean) => {
          const kept = rows.filter(keep);
          const total = kept.reduce((a, r) => a + r.weight, 0);
          let v = 0;
          for (const row of kept) {
            for (const c of row.items) v += (row.weight / total / row.items.length) * toStars(dismantleStarsOf(c), dismantleCrystalsOf(c) * (1 + COLLECTION_DUP_BONUS));
          }
          return v;
        };
        expect(rows.reduce((a, r) => a + r.weight, 0)).toBe(RATE_SCALE);
        const one = pull(() => true);
        const bonus = pull((r) => !(r.rarity === 'common' && !r.hidden));
        const ratio = ((TEN_COUNT - 1) * one + bonus) / TEN_PRICE;
        expect({ banner, cls, ok: ratio < 0.5 }).toEqual({ banner, cls, ok: true });
      }
    }
    // 장비 뽑기: 결정만 나온다. 전부 최대 강화(+20, 2배)라고 두고 천장 유니크 몫도 더한다
    const maxEnhance = 1 + GEAR_DISMANTLE_ENHANCE_BONUS * 20;
    let gear = 0;
    for (const [rarity, w] of GEAR_RARITY_PERMILLE) gear += (w / GEAR_RATE_SCALE) * (GEAR_DISMANTLE[rarity]?.crystals ?? 0) * maxEnhance;
    gear += ((GEAR_DISMANTLE.Unique?.crystals ?? 0) * maxEnhance) / GEAR_CEILING.max;
    expect((TEN_COUNT * toStars(0, gear)) / TEN_PRICE).toBeLessThan(0.2);
  });
});
