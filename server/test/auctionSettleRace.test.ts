// 경매 마감 경계 동시성: 입찰·정산 틱·취소가 겹쳐도 하나만 성립하고 골드가 새지 않는다
import { getPool } from '../src/db/pool';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import {
  advance,
  bidReq,
  buyoutReq,
  cancelReq,
  claimAllReq,
  expectConserved,
  HOUR,
  listIron,
  listingRow,
  mk,
  resetClock,
  secondChar,
} from './auctionHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { flagKinds } from './auctionHelpers';

const app = buildApp();
beforeEach(async () => {
  resetClock();
  await resetDb();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const sinkSum = async (): Promise<number> =>
  Number(((await getPool().query('SELECT coalesce(sum(amount), 0) AS v FROM auction_sinks')).rows[0] as { v: string }).v);
/** 경매·우편 수령 원장의 증감 합(시드 지급은 제외) */
const ledgerSum = async (ids: number[]): Promise<number> =>
  Number(
    ((
      await getPool().query(
        "SELECT coalesce(sum(delta), 0) AS v FROM gold_ledger WHERE character_id = ANY($1::bigint[]) AND (reason LIKE 'auction\\_%' OR reason = 'mail_claim')",
        [ids],
      )
    ).rows[0] as { v: string }).v,
  );

describe('마감 직전 입찰 / 정산 틱 / 취소 동시', () => {
  it('입찰과 취소 중 하나만 성립하고, 정산 뒤 구매자+판매자 골드 원장 합과 소각 합이 0이다', async () => {
    for (let round = 0; round < 3; round++) {
      const seller = await mk(app);
      const bidder = await mk(app);
      const id = await listIron(app, seller, { buyout: 3000, start_bid: 1000 });
      advance(12 * HOUR - 2_000); // 마감 2초 전
      const [b, c, t] = await Promise.all([bidReq(app, bidder, id, 1000), cancelReq(app, seller, id), runAuctionTick()]);
      expect(t.failed).toBe(0);
      expect([b.status === 200, c.status === 200].filter(Boolean)).toHaveLength(1);
      advance(2 * HOUR); // 마감 지남(마감 임박 입찰의 연장 시간도 지난다)
      await runAuctionTick();
      await claimAllReq(app, seller);
      await claimAllReq(app, bidder);
      const row = await listingRow(id);
      expect(row.status).toBe(b.status === 200 ? 'sold' : 'cancelled');
      // 닫힌 등록 하나에 정산은 한 번: 체결 기록은 입찰이 이긴 때만 1건
      const trades = await getPool().query('SELECT count(*) AS n FROM auction_trades WHERE listing_id = $1', [row.id]);
      expect(Number((trades.rows[0] as { n: string }).n)).toBe(b.status === 200 ? 1 : 0);
      await expectConserved();
    }
    const all = await getPool().query<{ id: string }>('SELECT id FROM characters');
    const ids = all.rows.map((r) => Number(r.id));
    expect((await ledgerSum(ids)) + (await sinkSum())).toBe(0);
  });

  it('같은 계정의 다른 캐릭터가 입찰·구매하면 OWN_LISTING', async () => {
    const seller = await mk(app);
    const alt = await secondChar(app, seller);
    const id = await listIron(app, seller, { buyout: 3000, start_bid: 1000 });
    const bid = await bidReq(app, alt, id, 1000);
    expect(bid.status).toBe(403);
    expect(bid.body.errors.code).toBe('OWN_LISTING');
    expect((await buyoutReq(app, alt, id)).body.errors.code).toBe('OWN_LISTING');
    expect(await flagKinds(alt)).toContain('self_account');
    expect((await listingRow(id)).current_bid).toBeNull();
  });

  it('금액 0인 소각 행이 생기지 않는다(무입찰 마감, 골드 없는 우편 폐기)', async () => {
    const seller = await mk(app);
    const id = await listIron(app, seller, { buyout: 3000, start_bid: 1000 });
    advance(12 * HOUR + 60_000);
    expect((await runAuctionTick()).settled).toBe(1);
    expect((await listingRow(id)).status).toBe('expired');
    // 반환 우편(아이템만, 골드 0)이 수령 없이 기한을 넘기면 폐기되지만 소각 행은 만들지 않는다
    advance(400 * 24 * HOUR);
    const t = await runAuctionTick();
    expect(t.mailsExpired).toBeGreaterThan(0);
    const zero = await getPool().query('SELECT count(*) AS n FROM auction_sinks WHERE amount <= 0');
    expect(zero.rows[0]).toEqual({ n: '0' });
    const expiredSinks = await getPool().query("SELECT count(*) AS n FROM auction_sinks WHERE kind = 'mail_expire'");
    expect(expiredSinks.rows[0]).toEqual({ n: '0' });
    await expectConserved();
  });
});
