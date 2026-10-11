import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import { countOf, expectLedgerConsistent, get, post, seedClaims, seedItem, type Hero } from './economyHelpers';
import {
  advance,
  bidReq,
  buyoutReq,
  cancelReq,
  claimAllReq,
  expectConserved,
  flagKinds,
  goldNow,
  HOUR,
  listIron,
  listReq,
  mailList,
  MIN,
  mk,
  resetClock,
} from './auctionHelpers';
import { secondChar } from './auctionHelpers';
import { auth, resetDb, shutdown } from './helpers';
import { buildApp } from './auctionHelpers';

// 쌍 한도는 넉넉히 두고(시세·속성 테스트), 쌍 한도 테스트가 직접 낮춘다
let app = buildApp({ AUCTION_PAIR_DAILY_TRADES: '10000' });
beforeEach(async () => {
  resetClock();
  await resetDb();
  app = buildApp({ AUCTION_PAIR_DAILY_TRADES: '10000' });
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

/** 판매자 s가 buyer b에게 iron 한 자루를 price에 판다(구매까지) */
async function trade(s: Hero, b: Hero, price: number): Promise<void> {
  const id = await listIron(app, s, { buyout: price });
  const r = await buyoutReq(app, b, id);
  if (r.status !== 200) throw new Error(`trade failed ${r.status} ${JSON.stringify(r.body)}`);
}

describe('S18 쌍 한도와 시세 중앙값', () => {
  it('같은 계정 쌍의 하루 체결 한도를 넘으면 PAIR_LIMIT, 의심 기록', async () => {
    app = buildApp({ AUCTION_PAIR_DAILY_TRADES: '2' });
    const s = await mk(app);
    const b = await mk(app);
    await trade(s, b, 1000);
    await trade(s, b, 1000);
    const id = await listIron(app, s, { buyout: 1000, hours: 48 });
    const r = await buyoutReq(app, b, id);
    expect(r.status).toBe(422);
    expect(r.body.errors).toMatchObject({ code: 'PAIR_LIMIT', limit: { trades: 2 } });
    expect(await flagKinds(b)).toEqual(['pair_limit']);
    expect(await goldNow(b)).toBe(100_000 - 2000);
    // 다음 게임 일(06:00 KST 경계)에는 다시 가능하다
    advance(25 * HOUR);
    expect((await buyoutReq(app, b, id)).status).toBe(200);
    await expectConserved();
  });

  it('기록이 모자라면 콜드 스타트, 충분하면 쌍 중복을 제한한 중앙값으로 한도가 정해진다', async () => {
    const [s1, s2, b1, b2, b3] = await Promise.all([mk(app), mk(app), mk(app), mk(app), mk(app)]) as [Hero, Hero, Hero, Hero, Hero];
    await trade(s1, b1, 1000);
    await trade(s1, b2, 1200);
    await trade(s1, b3, 1400);
    await trade(s2, b1, 1600);
    // 4건: 아직 콜드 스타트(상점가 1~100배)
    const cold = await get(app, s1, '/auction/prices/eq_sword_10_u');
    expect(cold.body.data).toMatchObject({ source: 'vendor', limits: { min: 31, max: 3100 } });
    await trade(s2, b2, 1800);
    // 5건, 구매 계정 3: 중앙값 1400 -> 하한 20%, 상한 500%
    const hist = await get(app, s1, '/auction/prices/eq_sword_10_u');
    expect(hist.body.data).toMatchObject({ source: 'history', limits: { min: 280, max: 7000 }, volume: 5 });
    // 같은 쌍이 극단 가격으로 거듭 거래해도 최근 2건만 반영된다
    await trade(s1, b1, 2500);
    await trade(s1, b1, 2500);
    await trade(s1, b1, 2500);
    const after = await get(app, s1, '/auction/prices/eq_sword_10_u');
    // 반영: (s1,b1) 2500 2500 / (s1,b2) 1200 / (s1,b3) 1400 / (s2,b1) 1600 / (s2,b2) 1800 -> 중앙값 1700
    expect(after.body.data.limits).toEqual({ min: 340, max: 8500 });
    // 한도 밖 등록은 새 기준으로 거절된다
    await seedItem(s1, 'eq_sword_10_u', 1);
    const low = await listReq(app, s1, { item_key: 'eq_sword_10_u', count: 1, buyout: 300, hours: 12 });
    expect(low.body.errors).toMatchObject({ code: 'PRICE_OUT_OF_RANGE', min: 340, source: 'history' });
    await expectConserved();
  });

  it('구매 계정이 둘뿐이면 건수가 많아도 콜드 스타트', async () => {
    const [s1, s2, s3, b1, b2] = await Promise.all([mk(app), mk(app), mk(app), mk(app), mk(app)]) as [Hero, Hero, Hero, Hero, Hero];
    await trade(s1, b1, 1000);
    await trade(s2, b1, 1000);
    await trade(s3, b1, 1000);
    await trade(s1, b2, 1000);
    await trade(s2, b2, 1000);
    await trade(s3, b2, 1000);
    const p = await get(app, s1, '/auction/prices/eq_sword_10_u');
    expect(p.body.data.source).toBe('vendor');
  });
});

describe('감사 보강: 쌍 한도(입찰 포함), 시세 일일 상승 상한, 골드 상한, 계정 단위 최고 입찰', () => {
  it('같은 계정이 한 판매자의 여러 등록에 동시에 최고 입찰을 걸어도 한도(3)까지만 받는다', async () => {
    app = buildApp({ AUCTION_PAIR_DAILY_TRADES: '3' });
    const seller = await mk(app);
    const bidder = await mk(app);
    const alt = await secondChar(app, bidder);
    const ids: string[] = [];
    for (let i = 0; i < 4; i++) ids.push(await listIron(app, seller, { buyout: 1000, start_bid: 100, hours: 48 }));
    // 순차(같은 캐릭터)
    const seq = [];
    for (const id of ids) seq.push(await bidReq(app, bidder, id, 100));
    expect(seq.map((r) => r.status)).toEqual([200, 200, 200, 422]);
    expect(seq[3]?.body.errors.code).toBe('PAIR_LIMIT');
    // 동시(같은 계정의 서로 다른 캐릭터): 쌍 락으로 직렬화되어 한도를 넘지 않는다
    const seller2 = await mk(app);
    const ids2: string[] = [];
    for (let i = 0; i < 4; i++) ids2.push(await listIron(app, seller2, { buyout: 1000, start_bid: 100, hours: 48 }));
    const res = await Promise.all(ids2.map((id, i) => bidReq(app, i % 2 ? alt : bidder, id, 100)));
    expect(res.filter((r) => r.status === 200)).toHaveLength(3);
    expect(res.filter((r) => r.body.errors?.code === 'PAIR_LIMIT')).toHaveLength(1);
    await expectConserved();
  });

  it('시세 중앙값은 직전 게임 일 값의 1.5배를 넘어 오르지 못한다', async () => {
    const [s1, s2, b1, b2, b3] = (await Promise.all([mk(app), mk(app), mk(app), mk(app), mk(app)])) as [Hero, Hero, Hero, Hero, Hero];
    const pairs: [Hero, Hero][] = [[s1, b1], [s1, b2], [s1, b3], [s2, b1], [s2, b2]];
    for (const [i, [s, b]] of pairs.entries()) await trade(s, b, 1000 + i * 200); // 중앙값 1400
    advance(25 * HOUR);
    for (const [s, b] of pairs) await trade(s, b, 2500); // 오늘 중앙값 2150 > 1400 x 1.5 = 2100
    const p = await get(app, s1, '/auction/prices/eq_sword_10_u');
    expect(p.body.data.source).toBe('history');
    expect(p.body.data.limits).toEqual({ min: 420, max: 10500 });
    // 상한을 올리면(환경변수) 실제 중앙값이 쓰인다
    app = buildApp({ AUCTION_PAIR_DAILY_TRADES: '10000', AUCTION_REF_DAILY_CAP_BPS: '30000' });
    const q = await get(app, s1, '/auction/prices/eq_sword_10_u');
    expect(q.body.data.limits).toEqual({ min: 430, max: 10750 });
  });

  it('골드 획득 경로에서도 상한을 넘기지 않는다(상점 판매)', async () => {
    const h = await mk(app, 2_147_483_600);
    await seedItem(h, 'potion_hp', 10);
    const r = await post(app, h, '/shop/sell', { item_key: 'potion_hp', count: 10 });
    expect(r.status).toBe(200);
    expect(await goldNow(h)).toBe(2_147_483_647);
    expect(r.body.data.delta.gold).toBe(2_147_483_647);
    const led = await getPool().query("SELECT delta FROM gold_ledger WHERE character_id = $1 AND reason = 'shop_sell'", [h.dbId]);
    expect(led.rows).toEqual([{ delta: '47' }]);
    await expectLedgerConsistent(h);
  });

  it('같은 계정의 다른 캐릭터가 이미 최고 입찰자면 ALREADY_TOP_BIDDER', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    const alt = await secondChar(app, bidder);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    expect((await bidReq(app, bidder, id, 100)).status).toBe(200);
    const r = await bidReq(app, alt, id, 200);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('ALREADY_TOP_BIDDER');
  });

  it('취소의 request_id는 원장에 연결된다', async () => {
    const seller = await mk(app);
    const id = await listIron(app, seller);
    const rid = randomUUID();
    expect((await cancelReq(app, seller, id, rid)).status).toBe(200);
    const led = await getPool().query("SELECT request_id FROM item_ledger WHERE reason = 'auction_return'");
    expect(led.rows).toEqual([{ request_id: rid }, { request_id: rid }]);
    expect((await request(app).delete(`/characters/${seller.id}/auction/listings/${id}?request_id=bad`).set(auth(seller.s))).status).toBe(400);
  });
});

describe('S19 속성 테스트: 임의 연산열 뒤에도 골드·아이템 보존식이 성립한다', () => {
  it('등록, 입찰, 즉시 구매, 취소, 시간 경과·정산, 수령을 섞어도 어긋나지 않는다', async () => {
    // 고정 시드 난수(재현 가능)
    let seed = 20261004;
    const rnd = (n: number): number => {
      seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
      return seed % n;
    };
    const heroes = await Promise.all(Array.from({ length: 5 }, () => mk(app, 50_000)));
    for (const h of heroes) {
      await seedItem(h, 'eq_sword_10_u', 6);
      await seedItem(h, 'mat_bone', 60);
      await seedItem(h, 'potion_hp', 40);
    }
    const live: string[] = [];
    const pick = <T>(xs: T[]): T => xs[rnd(xs.length)] as T;
    for (let step = 0; step < 70; step++) {
      const h = pick(heroes);
      const op = rnd(10);
      if (op <= 2) {
        const kind = rnd(3);
        const body =
          kind === 0
            ? { item_key: 'eq_sword_10_u', count: 1, buyout: 100 + rnd(2000), hours: pick([12, 24, 48]) }
            : kind === 1
              ? { item_key: 'mat_bone', count: 1 + rnd(5), buyout: 0, hours: 12 }
              : { item_key: 'potion_hp', count: 1 + rnd(3), buyout: 0, hours: 24 };
        if (kind === 1) body.buyout = body.count * (20 + rnd(200));
        if (kind === 2) body.buyout = body.count * (20 + rnd(200));
        const withBid = rnd(2) === 0 ? { ...body, start_bid: Math.max(25, Math.floor(body.buyout / 2)) } : body;
        const r = await listReq(app, h, withBid);
        if (r.status === 201) live.push(r.body.data.listing.id as string);
      } else if (op <= 4 && live.length) {
        await bidReq(app, h, pick(live), 30 + rnd(1500));
      } else if (op === 5 && live.length) {
        await buyoutReq(app, h, pick(live));
      } else if (op === 6 && live.length) {
        await cancelReq(app, h, pick(live));
      } else if (op === 7) {
        advance((1 + rnd(20)) * HOUR);
        await runAuctionTick();
      } else if (op === 8) {
        await claimAllReq(app, h);
      } else {
        const mails = await mailList(app, h);
        if (mails.length) await post(app, h, `/mail/${(pick(mails) as { id: string }).id}/claim`, {});
      }
      if (step % 10 === 9) await expectConserved();
    }
    advance(60 * 24 * HOUR);
    await runAuctionTick();
    await expectConserved();
    for (const h of heroes) {
      await claimAllReq(app, h);
      await claimAllReq(app, h);
    }
    await expectConserved();
    for (const h of heroes) await expectLedgerConsistent(h);
    const trades = await getPool().query('SELECT count(*) AS n FROM auction_trades');
    expect(Number((trades.rows[0] as { n: string }).n)).toBeGreaterThan(0);
  });
});

describe('S20 캐릭터 삭제 거절과 클라이언트 값 조작', () => {
  const del = (h: Hero) => request(app).delete(`/characters/${h.id}`).set(auth(h.s));

  it('진행 중 등록, 최고 입찰, 미수령 우편이 있으면 CHARACTER_HAS_AUCTION', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    const id = await listIron(app, seller, { start_bid: 100 });
    expect((await del(seller)).body.errors.code).toBe('CHARACTER_HAS_AUCTION');
    expect((await del(seller)).status).toBe(409);
    await bidReq(app, bidder, id, 100);
    expect((await del(bidder)).status).toBe(409);
    advance(12 * HOUR + MIN);
    await runAuctionTick();
    // 정산이 끝나도 받지 않은 우편이 있으면 삭제 불가
    expect((await del(seller)).status).toBe(409);
    expect((await del(bidder)).status).toBe(409);
    await claimAllReq(app, seller);
    await claimAllReq(app, bidder);
    expect((await del(seller)).status).toBe(200);
    expect((await del(bidder)).status).toBe(200);
    const stray = await mk(app);
    expect((await del(stray)).status).toBe(200);
  });

  it('요청 본문 조작: 알 수 없는 필드, 음수·소수·상한 초과, 잘못된 기간·수량', async () => {
    const h = await mk(app);
    await seedItem(h, 'eq_sword_10_u', 2);
    await seedItem(h, 'mat_bone', 2000);
    const ok = { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 };
    for (const extra of [{ fee: 0 }, { deposit: 0 }, { ends_at: 'x' }, { category: 'weapon' }, { bind: 'none' }, { seller: 'x' }]) {
      expect((await listReq(app, h, { ...ok, ...extra })).status).toBe(400);
    }
    for (const bad of [{ buyout: -5 }, { buyout: 0 }, { buyout: 10.5 }, { buyout: 1_000_000_001 }, { start_bid: 0 }, { count: 0 }, { hours: 'x' }, { item_key: 'gold' }]) {
      expect((await listReq(app, h, { ...ok, ...bad })).status).toBe(400);
    }
    expect((await listReq(app, h, { ...ok, hours: 7 })).body.errors.code).toBe('BAD_DURATION');
    expect((await listReq(app, h, { ...ok, count: 2 })).body.errors).toMatchObject({ code: 'BAD_COUNT', max: 1 });
    expect((await listReq(app, h, { item_key: 'mat_bone', count: 1000, buyout: 5000, hours: 12 })).body.errors).toMatchObject({ code: 'BAD_COUNT', max: 999 });
    const id = await listIron(app, await mk(app), { start_bid: 100 });
    for (const amount of [0, -1, 1.5, 1_000_000_001, '100']) {
      expect((await post(app, h, `/auction/listings/${id}/bids`, { amount })).status).toBe(400);
    }
    expect((await post(app, h, `/auction/listings/${id}/buyout`, { price: 1 })).status).toBe(400);
    expect((await post(app, h, `/auction/listings/${id}/bids`, { amount: 100, current_bid: 1 })).status).toBe(400);
    expect((await post(app, h, '/auction/listings/not-a-uuid/buyout', {})).status).toBe(400);
    expect((await post(app, h, `/auction/listings/${randomUUID()}/buyout`, {})).body.errors.code).toBe('LISTING_NOT_FOUND');
    expect(await countOf(h, 'eq_sword_10_u')).toBe(2);
    await expectConserved();
  });

  it('인증과 속도 제한: 토큰이 없으면 401, 남의 캐릭터 경로는 404, 등록은 분당 한도', async () => {
    const a = await mk(app);
    const b = await mk(app);
    expect((await request(app).get(`/characters/${a.id}/mail`).set({ 'X-Client-Version': '0.2.0' })).status).toBeGreaterThanOrEqual(400);
    expect((await request(app).get(`/characters/${a.id}/mail/summary`).set(auth(b.s))).status).toBe(404);
    expect((await request(app).get(`/characters/${a.id}/auction/mine`).set(auth(b.s))).status).toBe(404);
    // 등록은 분당 한도(RATE_AUCTION_LIST_PER_MIN)를 넘으면 429
    app = buildApp({ AUCTION_PAIR_DAILY_TRADES: '10000', RATE_AUCTION_LIST_PER_MIN: '2' });
    const body = { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 };
    expect((await listReq(app, b, body)).status).toBe(422);
    expect((await listReq(app, b, body)).status).toBe(422);
    const limited = await listReq(app, b, body);
    expect(limited.status).toBe(429);
    expect(limited.body.errors.code).toBe('RATE_LIMITED');
  });
});

describe('3단계 변경: 귀속이 재고 키에 들어간다', () => {
  it('퀘스트 보상 장비는 캐릭터 귀속, 창고 이동·장착·해제·강화가 귀속을 이어받는다', async () => {
    const h = await mk(app);
    await seedClaims(h, ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise']);
    await getPool().query(
      `INSERT INTO kill_stats (character_id, monster_id, kills) VALUES ($1, 'skeleton', 6)`,
      [h.dbId],
    );
    await getPool().query(
      "INSERT INTO site_deliveries (character_id, site_id, item_key, delivered) VALUES ($1, 'workshop', 'wood', 6), ($1, 'workshop', 'stone', 4)",
      [h.dbId],
    );
    const q = await post(app, h, '/quests/c1_rebuild/claim', {});
    expect(q.status).toBe(200);
    expect(q.body.data.delta.stacks).toEqual([{ item_key: 'eq_ring_1_r', location: 'bag', bind: 'character', count: 1 }]);
    // 같은 키의 거래 가능 재고는 따로 쌓인다
    await seedItem(h, 'eq_ring_1_r', 1);
    const rows = await getPool().query("SELECT bind, count FROM character_items WHERE character_id = $1 AND item_key = 'eq_ring_1_r' ORDER BY bind", [h.dbId]);
    expect(rows.rows).toEqual([{ bind: 'character', count: 1 }, { bind: 'none', count: 1 }]);
    // 소모는 강한 귀속부터: 창고로 1개 보내면 캐릭터 귀속 행이 간다
    const mv = await post(app, h, '/storage/move', { moves: [{ item_key: 'eq_ring_1_r', to: 'storage', count: 1 }] });
    expect(mv.body.data.delta.stacks).toEqual(expect.arrayContaining([{ item_key: 'eq_ring_1_r', location: 'storage', bind: 'character', count: 1 }]));
    const st = await getPool().query("SELECT bind FROM character_items WHERE character_id = $1 AND location = 'storage'", [h.dbId]);
    expect(st.rows).toEqual([{ bind: 'character' }]);
    // 장착은 가방에 남은 거래 가능 행을 쓰고, 착용 행이 귀속을 가진다. 해제하면 같은 귀속으로 돌아온다
    const eq = await post(app, h, '/equipment/equip', { item_key: 'eq_ring_1_r' });
    expect(eq.status).toBe(200);
    const worn = await getPool().query("SELECT bind FROM character_items WHERE character_id = $1 AND location = 'worn' AND item_key = 'eq_ring_1_r'", [h.dbId]);
    expect(worn.rows).toEqual([{ bind: 'none' }]);
    const un = await post(app, h, '/equipment/unequip', { slot: 2 });
    expect(un.status).toBe(200);
    expect(un.body.data.delta.stacks).toEqual([{ item_key: 'eq_ring_1_r', location: 'bag', bind: 'none', count: 1 }]);
    await expectLedgerConsistent(h);
  });

  it('강화 결과는 소모한 행의 귀속을 그대로 가진다', async () => {
    const h = await mk(app, 1_000_000);
    await seedItem(h, 'eq_sword_10_u', 1, 'bag', 'account');
    await seedItem(h, 'mat_bone', 50);
    await seedItem(h, 'mat_ore', 50);
    await seedItem(h, 'mat_essence', 50);
    const res = await post(app, h, '/enhance', { target: { bag_key: 'eq_sword_10_u' } });
    expect(res.status).toBe(200);
    const rows = await getPool().query("SELECT item_key, bind FROM character_items WHERE character_id = $1 AND item_key LIKE 'eq_sword_10_u%' AND location = 'bag'", [h.dbId]);
    for (const r of rows.rows) expect(r.bind).toBe('account');
    expect(res.body.data.delta.stacks.every((s: { bind: string }) => s.bind !== undefined)).toBe(true);
    await expectLedgerConsistent(h);
  });

  it('상점 판매는 강한 귀속 재고부터 소모한다', async () => {
    const h = await mk(app);
    await seedItem(h, 'potion_hp', 2, 'bag', 'account');
    const before = await countOf(h, 'potion_hp');
    const sell = await post(app, h, '/shop/sell', { item_key: 'potion_hp', count: before - 1 });
    expect(sell.status).toBe(200);
    const left = await getPool().query("SELECT bind, count FROM character_items WHERE character_id = $1 AND item_key = 'potion_hp' ORDER BY bind", [h.dbId]);
    expect(left.rows).toEqual([{ bind: 'none', count: 1 }]);
    await expectLedgerConsistent(h);
  });
});
