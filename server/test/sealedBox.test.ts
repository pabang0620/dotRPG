// 14단계: 봉인된 상자 캐시샵 뽑기, 부스터 게이지, 확률 표
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { getSealedBox, sealedTable } from '../src/gamedata/sealedBox';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { setRng } from '../src/utils/rng';
import * as sealed from '../src/domains/sealedbox/sealedBoxService';
import { countOf, expectLedgerConsistent } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { expectWalletConsistent, ledgerCount, payApp, purchase, resetPay, walletState } from './payHelpers';
import { newPlayer, pullApi, rollOf, scriptRng, sealedInfo } from './sealedBoxHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterEach(() => setRng(null));
afterAll(async () => {
  setRng(null);
  await shutdown();
});

describe('확률 표', () => {
  it('일반 표와 부스터 표 모두 합이 100, 부스터는 희귀 이상 x2·일반 비례 축소·수량 x2', () => {
    const d = getSealedBox();
    expect(d.rows.length).toBe(13);
    const sum = (t: { rate: number }[]) => t.reduce((a, r) => a + r.rate, 0);
    const normal = sealedTable(d, false);
    const boosted = sealedTable(d, true);
    expect(sum(normal)).toBeCloseTo(100, 9);
    expect(sum(boosted)).toBeCloseTo(100, 9);
    for (let i = 0; i < normal.length; i++) {
      const n = normal[i]!;
      const b = boosted[i]!;
      expect(b.mult).toBe(2);
      if (n.row.tier === 'rare') expect(b.rate).toBeCloseTo(n.rate * 2, 9);
      else expect(b.rate).toBeLessThan(n.rate);
    }
    expect(normal.filter((r) => r.row.tier === 'rare').reduce((a, r) => a + r.rate, 0)).toBeCloseTo(14.968, 9);
  });

  it('GET /starshop/sealed: 가격·게이지·표', async () => {
    const pl = await newPlayer(app);
    const r = await sealedInfo(app, pl);
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ price_one: 100, price_eleven: 1000, booster: { gauge: 0, next_boosted: false } });
    expect(r.body.data.table).toHaveLength(13);
    expect(r.body.data.boosted_table).toHaveLength(13);
    expect(r.body.data.table[0]).toMatchObject({ item_key: 'potion_hp_hi', count: 5, tier: 'common' });
    expect(r.body.data.boosted_table[0].count).toBe(10);
  });
});

describe('뽑기', () => {
  it('1회: 100 차감(무료분 먼저, 모자란 만큼 유료), 상급 물약 5+5 가방 지급, 원장·기록', async () => {
    const pl = await newPlayer(app, 50);
    await purchase(app, pl.p, 'stars_300');
    expect(await walletState(pl.p.accountId)).toMatchObject({ balance: 350, paid: 300 });
    scriptRng([rollOf('potion_hi_x5', false)]);
    const r = await pullApi(app, pl, 1);
    expect(r.status).toBe(200);
    expect(r.body.data.balance).toBe(250);
    expect(r.body.data.results).toEqual([
      expect.objectContaining({ item_key: 'potion_hp_hi', count: 5, boosted: false, tier: 'common', rewards: [{ item_key: 'potion_hp_hi', count: 5 }, { item_key: 'potion_mp_hi', count: 5 }] }),
    ]);
    expect(r.body.data.booster).toEqual({ gauge: 1, next_boosted: false });
    expect(await countOf(pl.hero, 'potion_hp_hi')).toBe(5);
    expect(await countOf(pl.hero, 'potion_mp_hi')).toBe(5);
    // 무료 50 + 유료 50
    const l = await getPool().query<{ delta: string; paid_delta: string; ref: string }>("SELECT delta, paid_delta, ref FROM star_ledger WHERE reason = 'sealed_pull' AND account_id = $1", [pl.p.accountId]);
    expect(l.rows).toEqual([{ delta: '-100', paid_delta: '-50', ref: 'sealed_1' }]);
    expect(await walletState(pl.p.accountId)).toMatchObject({ balance: 250, paid: 250, lots: 250 });
    const log = await getPool().query("SELECT source, row_id, boosted, gauge_before, gauge_after FROM sealed_pulls WHERE account_id = $1", [pl.p.accountId]);
    expect(log.rows).toEqual([{ source: 'shop', row_id: 'potion_hi_x5', boosted: false, gauge_before: 0, gauge_after: 1 }]);
    await expectWalletConsistent(pl.p.accountId);
    await expectLedgerConsistent(pl.hero);
  });

  it('10+1: 1000 차감, 마지막(11번째) 한 번만 부스터(수량 x2), 게이지 0으로 복귀', async () => {
    const pl = await newPlayer(app, 2000);
    scriptRng([rollOf('essence_x10', false)]);
    // 11번째만 부스터 표에서 굴린다: 앞 10개는 일반 표의 마력 정수 칸, 마지막은 부스터 표의 마력 정수 칸
    const rolls = [...Array<number>(10).fill(rollOf('essence_x10', false)), rollOf('essence_x10', true)];
    scriptRng(rolls);
    const r = await pullApi(app, pl, 11);
    expect(r.status).toBe(200);
    expect(r.body.data.balance).toBe(1000);
    const res = r.body.data.results as { boosted: boolean; count: number; item_key: string }[];
    expect(res).toHaveLength(11);
    expect(res.filter((x) => x.boosted)).toHaveLength(1);
    expect(res[10]).toMatchObject({ boosted: true, item_key: 'mat_essence', count: 20 });
    expect(res[0]).toMatchObject({ boosted: false, count: 10 });
    expect(r.body.data.booster).toEqual({ gauge: 0, next_boosted: false });
    expect(await countOf(pl.hero, 'mat_essence')).toBe(10 * 10 + 20);
    const l = await getPool().query<{ delta: string }>("SELECT delta FROM star_ledger WHERE reason = 'sealed_pull' AND account_id = $1", [pl.p.accountId]);
    expect(l.rows).toEqual([{ delta: '-1000' }]);
    const log = await getPool().query<{ boosted: boolean; seq: number }>('SELECT seq, boosted FROM sealed_pulls WHERE account_id = $1 ORDER BY seq', [pl.p.accountId]);
    expect(log.rows).toHaveLength(11);
    expect(log.rows.filter((x) => x.boosted).map((x) => x.seq)).toEqual([10]);
  });

  it('11번째 열기마다 부스터: 1회씩 10번 뒤 next_boosted, 11번째가 부스터, 그 뒤 다시 0부터', async () => {
    const pl = await newPlayer(app, 5000);
    scriptRng([rollOf('protect_x1', false)]);
    for (let i = 1; i <= 10; i++) {
      const r = await sealed.pull(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), count: 1 });
      expect((r.body.data as { results: { boosted: boolean }[] }).results[0]!.boosted).toBe(false);
    }
    expect((await sealedInfo(app, pl)).body.data.booster).toEqual({ gauge: 10, next_boosted: true });
    scriptRng([rollOf('protect_x1', true)]);
    const eleventh = await sealed.pull(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), count: 1 });
    const d = eleventh.body.data as { results: { boosted: boolean; count: number }[]; booster: unknown };
    expect(d.results[0]).toMatchObject({ boosted: true, count: 2 });
    expect(d.booster).toEqual({ gauge: 0, next_boosted: false });
    scriptRng([rollOf('protect_x1', false)]);
    const twelfth = await sealed.pull(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), count: 1 });
    expect((twelfth.body.data as { results: { boosted: boolean }[] }).results[0]!.boosted).toBe(false);
    expect(await countOf(pl.hero, 'ticket_protect')).toBe(10 + 2 + 1);
  });

  it('같은 request_id 재전송: 같은 결과, 한 번만 차감·지급', async () => {
    const pl = await newPlayer(app, 500);
    scriptRng([rollOf('essence_x10', false)]);
    const rid = randomUUID();
    const a = await pullApi(app, pl, 1, rid);
    const b = await pullApi(app, pl, 1, rid);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(b.body.data).toEqual(a.body.data);
    expect(await ledgerCount(pl.p.accountId, 'sealed_pull')).toBe(1);
    expect(await countOf(pl.hero, 'mat_essence')).toBe(10);
    expect((await walletState(pl.p.accountId)).balance).toBe(400);
    expect((await sealedInfo(app, pl)).body.data.booster.gauge).toBe(1);
  });

  it('별조각 부족은 거절(NOT_ENOUGH_STARS), 아무것도 바뀌지 않는다', async () => {
    const pl = await newPlayer(app, 99);
    const r = await pullApi(app, pl, 1);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('NOT_ENOUGH_STARS');
    expect(await ledgerCount(pl.p.accountId, 'sealed_pull')).toBe(0);
    expect((await walletState(pl.p.accountId)).balance).toBe(99);
    const r11 = await pullApi(app, pl, 11);
    expect(r11.body.errors.code).toBe('NOT_ENOUGH_STARS');
    expect((await sealedInfo(app, pl)).body.data.booster.gauge).toBe(0);
  });

  it('입력 오류(400): count 5, 금액·결과 필드는 거절', async () => {
    const pl = await newPlayer(app, 1000);
    expect((await pullApi(app, pl, 5 as never)).status).toBe(400);
    const bad = await (await import('./economyHelpers')).post(app, pl.hero, '/starshop/sealed/pull', { count: 1, price: 1, result: 'ticket_enh15' });
    expect(bad.status).toBe(400);
    expect((await walletState(pl.p.accountId)).balance).toBe(1000);
  });

  it('동시 요청: 잔액 100에 서로 다른 request_id 2개 -> 1개만 성공, 같은 request_id 2개 -> 차감 1번', async () => {
    const pl = await newPlayer(app, 100);
    const rs = await Promise.allSettled([
      sealed.pull(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), count: 1 }),
      sealed.pull(pl.p.accountId, pl.hero.id, { request_id: randomUUID(), count: 1 }),
    ]);
    expect(rs.filter((r) => r.status === 'fulfilled')).toHaveLength(1);
    const rej = rs.find((r): r is PromiseRejectedResult => r.status === 'rejected');
    expect((rej?.reason as { code?: string }).code).toBe('NOT_ENOUGH_STARS');
    expect(await walletState(pl.p.accountId)).toMatchObject({ balance: 0 });
    expect(await ledgerCount(pl.p.accountId, 'sealed_pull')).toBe(1);

    const q = await newPlayer(app, 1000);
    const rid = randomUUID();
    const same = await Promise.all([
      sealed.pull(q.p.accountId, q.hero.id, { request_id: rid, count: 1 }),
      sealed.pull(q.p.accountId, q.hero.id, { request_id: rid, count: 1 }),
    ]);
    expect(same[0].body.data).toEqual(same[1].body.data);
    expect(await ledgerCount(q.p.accountId, 'sealed_pull')).toBe(1);
    expect((await walletState(q.p.accountId)).balance).toBe(900);
    await expectWalletConsistent(pl.p.accountId);
    await expectWalletConsistent(q.p.accountId);
  });

  it('확률표 버전이 다르면 409 RATES_CHANGED', async () => {
    const pl = await newPlayer(app, 100);
    const r = await (await import('./economyHelpers')).post(app, pl.hero, '/starshop/sealed/pull', { count: 1, rates_version: 'old' });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('RATES_CHANGED');
  });
});
