import { getPool } from '../src/db/pool';
import * as auctionRepo from '../src/domains/auction/auctionRepository';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import type { Hero } from './economyHelpers';
import {
  advance,
  bidReq,
  buyoutReq,
  cancelReq,
  expectConserved,
  goldNow,
  HOUR,
  listIron,
  listingRow,
  mailList,
  MIN,
  mk,
  resetClock,
} from './auctionHelpers';
import { buildApp, resetDb, shutdown } from './helpers';

const app = buildApp();
beforeEach(async () => {
  resetClock();
  await resetDb();
});
afterEach(() => jest.restoreAllMocks());
afterAll(async () => {
  resetClock();
  await shutdown();
});

const many = (n: number, gold = 100_000): Promise<Hero[]> => Promise.all(Array.from({ length: n }, () => mk(app, gold)));

describe('S11 동시 즉시 구매', () => {
  it('정확히 1건 성공, 나머지는 골드 차감 없이 LISTING_NOT_ACTIVE', async () => {
    const seller = await mk(app);
    const buyers = await many(6);
    const id = await listIron(app, seller, { buyout: 1000 });
    const res = await Promise.all(buyers.map((b) => buyoutReq(app, b, id)));
    const ok = res.filter((r) => r.status === 200);
    expect(ok).toHaveLength(1);
    for (const r of res.filter((x) => x.status !== 200)) {
      expect(r.status).toBe(409);
      expect(r.body.errors.code).toBe('LISTING_NOT_ACTIVE');
    }
    const golds = await Promise.all(buyers.map(goldNow));
    expect(golds.filter((g) => g === 99_000)).toHaveLength(1);
    expect(golds.filter((g) => g === 100_000)).toHaveLength(5);
    const trades = await getPool().query('SELECT count(*) AS n FROM auction_trades');
    expect(trades.rows[0]).toEqual({ n: '1' });
    expect(await getPool().query("SELECT 1 FROM mails WHERE kind = 'bought'").then((r) => r.rowCount)).toBe(1);
    await expectConserved();
  });
});

describe('S12 동시 입찰', () => {
  it('금액이 다른 N건: 최고 입찰이 최종 최고가, 나머지는 BID_TOO_LOW 또는 우편 반환, 최고 입찰자는 하나', async () => {
    const seller = await mk(app);
    const bidders = await many(6);
    const id = await listIron(app, seller, { buyout: 2500, start_bid: 25 });
    const amounts = [30, 60, 120, 240, 480, 960];
    const res = await Promise.all(bidders.map((b, i) => bidReq(app, b, id, amounts[i] as number)));
    for (const r of res) expect([200, 409]).toContain(r.status);
    for (const r of res.filter((x) => x.status === 409)) expect(r.body.errors.code).toBe('BID_TOO_LOW');
    expect(res[5]?.status).toBe(200); // 가장 높은 입찰은 언제 처리돼도 유효하다
    const row = await listingRow(id);
    expect(Number(row.current_bid)).toBe(960);
    const tops = await getPool().query("SELECT count(*) AS n FROM auction_bids WHERE state = 'top'");
    expect(tops.rows[0]).toEqual({ n: '1' });
    // 성공했다가 밀린 입찰자마다 우편 한 통(예치금), 실패한 입찰자는 골드가 그대로
    for (const [i, b] of bidders.entries()) {
      const mails = await mailList(app, b);
      if (res[i]?.status === 200 && i !== 5) {
        expect(mails).toEqual([expect.objectContaining({ kind: 'outbid', gold: amounts[i] })]);
        expect(await goldNow(b)).toBe(100_000 - (amounts[i] as number));
      } else if (res[i]?.status === 409) {
        expect(mails).toEqual([]);
        expect(await goldNow(b)).toBe(100_000);
      }
    }
    expect(await goldNow(bidders[5] as Hero)).toBe(100_000 - 960);
    await expectConserved();
  });

  it('같은 금액 두 입찰: 하나만 성공, 나머지는 BID_TOO_LOW', async () => {
    const seller = await mk(app);
    const [a, b] = await many(2);
    const id = await listIron(app, seller, { buyout: 2500, start_bid: 100 });
    const res = await Promise.all([bidReq(app, a as Hero, id, 100), bidReq(app, b as Hero, id, 100)]);
    expect(res.map((r) => r.status).sort()).toEqual([200, 409]);
    expect(res.find((r) => r.status === 409)?.body.errors).toMatchObject({ code: 'BID_TOO_LOW', min_bid: 105 });
    await expectConserved();
  });
});

describe('S13 입찰 vs 구매 / 취소 / 마감 정산', () => {
  it('입찰 vs 즉시 구매: 먼저 잠근 쪽이 이기고 손실·복제가 없다', async () => {
    for (let round = 0; round < 3; round++) {
      const seller = await mk(app);
      const [bidder, buyer] = await many(2);
      const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
      const [b, y] = await Promise.all([bidReq(app, bidder as Hero, id, 100), buyoutReq(app, buyer as Hero, id)]);
      expect(y.status).toBe(200);
      expect((await listingRow(id)).status).toBe('sold');
      if (b.status === 200) {
        expect((await mailList(app, bidder as Hero))[0]).toMatchObject({ kind: 'outbid', gold: 100 });
      } else {
        expect(b.body.errors.code).toBe('LISTING_NOT_ACTIVE');
        expect(await goldNow(bidder as Hero)).toBe(100_000);
      }
      await expectConserved();
    }
  });

  it('입찰 vs 취소: 한쪽만 성공한다', async () => {
    for (let round = 0; round < 3; round++) {
      const seller = await mk(app);
      const bidder = await mk(app);
      const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
      const [b, c] = await Promise.all([bidReq(app, bidder, id, 100), cancelReq(app, seller, id)]);
      if (b.status === 200) {
        expect(c.status).toBe(409);
        expect(c.body.errors.code).toBe('HAS_BIDS');
        expect((await listingRow(id)).status).toBe('active');
      } else {
        expect(c.status).toBe(200);
        expect(b.body.errors.code).toBe('LISTING_NOT_ACTIVE');
        expect(await goldNow(bidder)).toBe(100_000);
      }
      await expectConserved();
    }
  });

  it('마감 이후 입찰은 틱이 아직 정산하지 않았어도 LISTING_ENDED, 틱과 동시에 와도 손실이 없다', async () => {
    const seller = await mk(app);
    const [late, late2] = await many(2);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    advance(12 * HOUR + MIN);
    const direct = await bidReq(app, late as Hero, id, 100);
    expect(direct.status).toBe(409);
    expect(direct.body.errors.code).toBe('LISTING_ENDED');
    const [b, t] = await Promise.all([bidReq(app, late2 as Hero, id, 100), runAuctionTick()]);
    expect(['LISTING_ENDED', 'LISTING_NOT_ACTIVE']).toContain(b.body.errors.code);
    expect(t.settled + (await runAuctionTick()).settled).toBe(1);
    expect((await listingRow(id)).status).toBe('expired');
    expect(await goldNow(late2 as Hero)).toBe(100_000);
    await expectConserved();
  });
});

describe('S16 정산 틱', () => {
  it('틱 두 개 동시: 한 번만 정산하고 우편은 한 통', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    await bidReq(app, bidder, id, 100);
    advance(12 * HOUR + MIN);
    const [a, b] = await Promise.all([runAuctionTick(), runAuctionTick()]);
    expect(a.settled + b.settled).toBe(1);
    expect(await mailList(app, seller)).toHaveLength(1);
    expect(await mailList(app, bidder)).toHaveLength(1);
    expect((await getPool().query('SELECT count(*) AS n FROM auction_trades')).rows[0]).toEqual({ n: '1' });
    await expectConserved();
  });

  it('정산 중 예외: 롤백되어 active로 남고 다음 틱이 다시 처리한다', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    await bidReq(app, bidder, id, 100);
    advance(12 * HOUR + MIN);
    jest.spyOn(auctionRepo, 'insertTrade').mockRejectedValueOnce(new Error('boom'));
    const first = await runAuctionTick();
    expect(first).toMatchObject({ settled: 0, failed: 1 });
    expect((await listingRow(id)).status).toBe('active');
    expect((await getPool().query('SELECT count(*) AS n FROM mails')).rows[0]).toEqual({ n: '0' });
    expect((await getPool().query("SELECT count(*) AS n FROM auction_bids WHERE state = 'top'")).rows[0]).toEqual({ n: '1' });
    await expectConserved();
    const second = await runAuctionTick();
    expect(second.settled).toBe(1);
    expect((await listingRow(id)).status).toBe('sold');
    await expectConserved();
  });
});
