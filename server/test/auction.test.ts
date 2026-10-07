import { getPool } from '../src/db/pool';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import { get, post, seedItem, seedLevel, seedWorn, expectLedgerConsistent, countOf } from './economyHelpers';
import {
  advance,
  setNowAt,
  bidReq,
  buyoutReq,
  cancelReq,
  claimReq,
  expectConserved,
  flagKinds,
  goldNow,
  HOUR,
  listIron,
  listReq,
  listingRow,
  mailList,
  MIN,
  mk,
  resetClock,
  secondChar,
} from './auctionHelpers';
import { buildApp, resetDb, shutdown } from './helpers';

const app = buildApp();
beforeEach(async () => {
  resetClock();
  await resetDb();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const bagRows = async (dbId: number, key: string) =>
  (
    await getPool().query<{ bind: string; count: number }>(
      "SELECT bind, count FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2 ORDER BY bind",
      [dbId, key],
    )
  ).rows;

describe('S1 등록 -> 즉시 구매 -> 우편 수령', () => {
  it('정상: 판매자 우편 = 대금 - 수수료 + 보증금, 구매 장비는 계정 귀속, 소각 기록, 보존식', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    const id = await listIron(app, seller, { buyout: 1000 });
    expect(await goldNow(seller)).toBe(100_000 - 10); // 보증금 10
    const res = await buyoutReq(app, buyer, id);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ result: 'bought', price: 1000, bind: 'account', delta: { gold: 99_000 } });
    expect(await goldNow(buyer)).toBe(99_000);

    const bm = await mailList(app, buyer);
    expect(bm).toHaveLength(1);
    expect(bm[0]).toMatchObject({ kind: 'bought', item: { item_key: 'eq_sword_10_u', count: 1, bind: 'account' }, gold: 0 });
    const sm = await mailList(app, seller);
    expect(sm).toHaveLength(1);
    expect(sm[0]).toMatchObject({ kind: 'sold', gold: 1000 - 50 + 10, item: null });
    const sink = await getPool().query("SELECT sum(amount) AS s FROM auction_sinks WHERE kind = 'fee'");
    expect(Number((sink.rows[0] as { s: string }).s)).toBe(50);
    await expectConserved();

    const c1 = await claimReq(app, buyer, (bm[0] as { id: string }).id);
    expect(c1.status).toBe(200);
    expect(c1.body.data.delta.stacks).toEqual([{ item_key: 'eq_sword_10_u', location: 'bag', bind: 'account', count: 1 }]);
    expect(await bagRows(buyer.dbId, 'eq_sword_10_u')).toEqual([{ bind: 'account', count: 1 }]);
    const c2 = await claimReq(app, seller, (sm[0] as { id: string }).id);
    expect(c2.body.data.delta.gold).toBe(100_000 - 10 + 960);
    await expectConserved();
    await expectLedgerConsistent(buyer);
    await expectLedgerConsistent(seller);
  });

  it('산 장비(계정 귀속)는 다시 등록할 수 없다', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    const id = await listIron(app, seller);
    await buyoutReq(app, buyer, id);
    await claimReq(app, buyer, ((await mailList(app, buyer))[0] as { id: string }).id);
    const res = await listReq(app, buyer, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('ITEM_BOUND');
  });

  it('골드 부족이면 아무것도 바뀌지 않는다(잔액 부족 422)', async () => {
    const seller = await mk(app);
    const poor = await mk(app, 100);
    const id = await listIron(app, seller, { buyout: 1000 });
    const res = await buyoutReq(app, poor, id);
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'NOT_ENOUGH_GOLD', need: 1000 });
    expect(await goldNow(poor)).toBe(100 + 0);
    expect((await listingRow(id)).status).toBe('active');
    // 보증금 부족
    const broke = await mk(app, 5);
    await seedItem(broke, 'eq_sword_10_u', 1);
    const dep = await listReq(app, broke, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 });
    expect(dep.status).toBe(422);
    expect(dep.body.errors).toMatchObject({ code: 'NOT_ENOUGH_GOLD', for: 'deposit' });
    await expectConserved();
  });
});

describe('S2 입찰 -> 더 높은 입찰 -> 마감 정산', () => {
  it('밀린 입찰자는 예치금 우편, 낙찰자는 아이템, 판매자는 대금', async () => {
    const seller = await mk(app);
    const b1 = await mk(app);
    const b2 = await mk(app);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    const r1 = await bidReq(app, b1, id, 100);
    expect(r1.status).toBe(200);
    expect(r1.body.data).toMatchObject({ result: 'bid', current_bid: 100, min_bid: 105, extended: false });
    expect(await goldNow(b1)).toBe(99_900);
    const low = await bidReq(app, b2, id, 104);
    expect(low.status).toBe(409);
    expect(low.body.errors).toMatchObject({ code: 'BID_TOO_LOW', min_bid: 105 });
    const r2 = await bidReq(app, b2, id, 105);
    expect(r2.status).toBe(200);
    const again = await bidReq(app, b2, id, 120);
    expect(again.body.errors.code).toBe('ALREADY_TOP_BIDDER');
    // 밀린 b1에게 우편으로 예치금이 돌아온다
    expect(await mailList(app, b1)).toEqual([expect.objectContaining({ kind: 'outbid', gold: 100 })]);
    await expectConserved();

    advance(12 * HOUR + MIN);
    // 지연 정산: 판매자가 우편을 보기만 해도 정산된다
    const sm = await mailList(app, seller);
    expect(sm).toEqual([expect.objectContaining({ kind: 'sold', gold: 105 - 6 + 10 })]);
    const wm = await mailList(app, b2);
    expect(wm).toEqual([expect.objectContaining({ kind: 'bought', item: expect.objectContaining({ item_key: 'eq_sword_10_u', bind: 'account' }) })]);
    expect((await listingRow(id)).status).toBe('sold');
    const bids = await getPool().query('SELECT state FROM auction_bids ORDER BY amount');
    expect(bids.rows.map((r: { state: string }) => r.state)).toEqual(['outbid', 'won']);
    await expectConserved();
  });
});

describe('S3 입찰 없이 마감', () => {
  it('판매자 우편에 아이템 반환, 보증금 소각', async () => {
    const seller = await mk(app);
    const id = await listIron(app, seller);
    advance(12 * HOUR + MIN);
    const t = await runAuctionTick();
    expect(t.settled).toBe(1);
    expect((await listingRow(id)).status).toBe('expired');
    const m = await mailList(app, seller);
    expect(m).toEqual([expect.objectContaining({ kind: 'expired', item: { item_key: 'eq_sword_10_u', count: 1, bind: 'none' }, gold: 0 })]);
    const sink = await getPool().query("SELECT amount FROM auction_sinks WHERE kind = 'deposit_forfeit'");
    expect(sink.rows).toEqual([{ amount: '10' }]);
    await claimReq(app, seller, (m[0] as { id: string }).id);
    expect(await bagRows(seller.dbId, 'eq_sword_10_u')).toEqual([{ bind: 'none', count: 1 }]);
    await expectConserved();
  });
});

describe('S4 취소', () => {
  it('입찰 없음: cancelled 우편과 보증금 몰수, 다시 취소하면 already, 입찰이 있으면 HAS_BIDS', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    const id = await listIron(app, seller, { start_bid: 100 });
    const ok = await cancelReq(app, seller, id);
    expect(ok.status).toBe(200);
    expect(ok.body.data).toMatchObject({ already: false, forfeited_deposit: 10 });
    expect((await mailList(app, seller))[0]).toMatchObject({ kind: 'cancelled', item: { item_key: 'eq_sword_10_u' } });
    const again = await cancelReq(app, seller, id);
    expect(again.status).toBe(200);
    expect(again.body.data.already).toBe(true);
    expect((await mailList(app, seller))).toHaveLength(1);
    const noBid = await bidReq(app, bidder, id, 100);
    expect(noBid.body.errors.code).toBe('LISTING_NOT_ACTIVE');

    const id2 = await listIron(app, seller, { start_bid: 100 });
    await bidReq(app, bidder, id2, 100);
    const hb = await cancelReq(app, seller, id2);
    expect(hb.status).toBe(409);
    expect(hb.body.errors.code).toBe('HAS_BIDS');
    // 남의 등록 취소는 404이고 의심 기록
    const stranger = await mk(app);
    const foreign = await cancelReq(app, stranger, id2);
    expect(foreign.status).toBe(404);
    expect(await flagKinds(stranger)).toEqual(['foreign_id']);
    await expectConserved();
  });
});

describe('S5 마감 연장', () => {
  it('마감 5분 안 입찰은 5분 연장, 7번째는 연장하지 않는다', async () => {
    const seller = await mk(app);
    const a = await mk(app, 10_000_000);
    const b = await mk(app, 10_000_000);
    const id = await listIron(app, seller, { buyout: 3100, start_bid: 31 });
    // 마감 4분 전으로 시계를 옮긴다
    const first = (await listingRow(id)).ends_at as Date;
    setNowAt(new Date(first.getTime() - 4 * MIN));
    let amount = 31; // the start bid
    for (let i = 0; i < 7; i++) {
      const who = i % 2 === 0 ? a : b;
      const res = await bidReq(app, who, id, amount);
      expect(res.status).toBe(200);
      expect(res.body.data.extended).toBe(i < 6);
      const row = await listingRow(id);
      expect(row.extend_count).toBe(Math.min(i + 1, 6));
      amount = res.body.data.min_bid as number;
      // 다음 입찰은 항상 마감 1분 전
      setNowAt(new Date((row.ends_at as Date).getTime() - MIN));
    }
    const row = await listingRow(id);
    expect(row.extend_count).toBe(6);
    expect((row.ends_at as Date).getTime() - first.getTime()).toBe(6 * 5 * MIN);
    await expectConserved();
  });
});

describe('S6 즉시 구매가 이상의 입찰', () => {
  it('즉시 구매로 처리되고 buyout 가격만 차감, 직전 입찰은 우편으로 반환', async () => {
    const seller = await mk(app);
    const b1 = await mk(app);
    const b2 = await mk(app);
    const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    await bidReq(app, b1, id, 100);
    const res = await bidReq(app, b2, id, 5000);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ result: 'bought', price: 1000 });
    expect(await goldNow(b2)).toBe(99_000);
    expect((await mailList(app, b1))[0]).toMatchObject({ kind: 'outbid', gold: 100 });
    const bids = await getPool().query('SELECT state FROM auction_bids');
    expect(bids.rows).toEqual([{ state: 'lost_to_buyout' }]);
    expect((await listingRow(id)).current_bid).toBeNull();
    await expectConserved();
  });
});

describe('S7 같은 계정 구매·입찰 금지', () => {
  it('같은 캐릭터와 같은 계정의 다른 캐릭터는 OWN_LISTING, 의심 기록', async () => {
    const seller = await mk(app);
    const alt = await secondChar(app, seller);
    const id = await listIron(app, seller, { start_bid: 100 });
    for (const who of [seller, alt]) {
      const buy = await buyoutReq(app, who, id);
      expect(buy.status).toBe(403);
      expect(buy.body.errors.code).toBe('OWN_LISTING');
      const bid = await bidReq(app, who, id, 100);
      expect(bid.status).toBe(403);
    }
    expect((await flagKinds(alt)).filter((k) => k === 'self_account')).toHaveLength(2);
    // 검색에서도 내 계정 매물은 빠진다
    const found = await get(app, alt, '/auction/search');
    expect(found.body.data.listings).toEqual([]);
    expect((await listingRow(id)).status).toBe('active');
  });
});

describe('S8 가격 한도', () => {
  it('콜드 스타트는 상점가 1~100배, 시작가에도 하한', async () => {
    const seller = await mk(app);
    await seedItem(seller, 'eq_sword_10_u', 4);
    const base = { item_key: 'eq_sword_10_u', count: 1, hours: 12 };
    const hi = await listReq(app, seller, { ...base, buyout: 3101 });
    expect(hi.status).toBe(422);
    expect(hi.body.errors).toMatchObject({ code: 'PRICE_OUT_OF_RANGE', min: 31, max: 3100, source: 'vendor' });
    const lo = await listReq(app, seller, { ...base, buyout: 30 });
    expect(lo.body.errors.code).toBe('PRICE_OUT_OF_RANGE');
    expect(await flagKinds(seller)).toEqual(['price_band', 'price_band']);
    const sb1 = await listReq(app, seller, { ...base, buyout: 1000, start_bid: 1000 });
    expect(sb1.body.errors.code).toBe('START_BID_INVALID');
    const sb2 = await listReq(app, seller, { ...base, buyout: 1000, start_bid: 30 });
    expect(sb2.body.errors.code).toBe('START_BID_INVALID');
    const ok = await listReq(app, seller, { ...base, buyout: 3100, start_bid: 31 });
    expect(ok.status).toBe(201);
    // 시세 응답이 같은 한도를 준다
    const p = await get(app, seller, '/auction/prices/eq_sword_10_u');
    expect(p.body.data).toMatchObject({ source: 'vendor', limits: { min: 31, max: 3100 }, avg7d: null, volume: 0 });
    // 강화 장비는 상점 판매가 공식(31 x (1 + 보너스 x 7))
    const p7 = await get(app, seller, '/auction/prices/eq_sword_10_u+7?count=1');
    expect(p7.body.data.source).toBe('vendor');
    expect(p7.body.data.limits.min).toBeGreaterThan(31);
  });
});

describe('S9 귀속과 거래 불가', () => {
  it('시작 장비, 보호권, 봉인 열쇠, 귀속 재고, 착용 중, 모르는 키', async () => {
    const h = await mk(app);
    const body = (item_key: string, count = 1) => ({ item_key, count, buyout: 1000, hours: 12 });
    await seedItem(h, 'eq_sword_wood', 1, 'bag', 'character');
    expect((await listReq(app, h, body('eq_sword_wood'))).body.errors).toMatchObject({ code: 'NOT_TRADABLE', reason: 'BASE_BOUND' });
    await seedItem(h, 'ticket_protect', 1, 'bag', 'character');
    expect((await listReq(app, h, body('ticket_protect'))).body.errors.code).toBe('NOT_TRADABLE');
    await seedItem(h, 'key_seal', 3, 'bag', 'character');
    expect((await listReq(app, h, body('key_seal', 1))).body.errors.code).toBe('NOT_TRADABLE');
    // 퀘스트 보상으로 받은 장비(캐릭터 귀속 행)
    await seedItem(h, 'eq_ring_10_un', 1, 'bag', 'character');
    expect((await listReq(app, h, body('eq_ring_10_un'))).body.errors.code).toBe('ITEM_BOUND');
    // 착용 중인 장비는 가방에 없다
    await seedWorn(h, 3, 'eq_ring_1_c');
    expect((await listReq(app, h, body('eq_ring_1_c'))).body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    expect((await listReq(app, h, body('no_such_item'))).body.errors.code).toBe('ITEM_UNKNOWN');
    expect((await listReq(app, h, body('mat_bone+3'))).body.errors.code).toBe('ITEM_UNKNOWN');
    // 같은 키의 귀속 재고와 거래 가능 재고가 섞여도 거래 가능한 것만 쓴다
    await seedItem(h, 'eq_ring_10_un', 1, 'bag', 'none');
    expect((await listReq(app, h, body('eq_ring_10_un'))).status).toBe(201);
    expect(await bagRows(h.dbId, 'eq_ring_10_un')).toEqual([{ bind: 'character', count: 1 }]);
    // 후보 목록은 귀속별로 줄을 나눠 이유를 준다
    const s = await get(app, h, '/auction/sellable');
    const row = (k: string, bind: string) => s.body.data.items.find((i: { item_key: string; bind: string }) => i.item_key === k && i.bind === bind);
    expect(row('eq_ring_10_un', 'character')).toMatchObject({ listable: false, reason: 'BOUND' });
    expect(row('key_seal', 'character')).toMatchObject({ listable: false, reason: 'NOT_TRADABLE' });
    expect(row('eq_sword_wood', 'character').reason).toBe('NOT_TRADABLE');
  });
});

describe('S10 등록 자격과 동시 등록 한도', () => {
  it('낮은 레벨·새 계정은 LISTING_GATE이고 구매는 가능, 21번째 등록은 LISTING_LIMIT', async () => {
    const seller = await mk(app);
    await seedItem(seller, 'eq_sword_10_u', 21);
    const buyer = await mk(app);
    const id = await listIron(app, seller);
    buildApp({ AUCTION_MIN_LEVEL: '10', AUCTION_MIN_ACCOUNT_AGE_DAYS: '7' });
    try {
      const gate = await listReq(app, seller, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 });
      expect(gate.status).toBe(422);
      expect(gate.body.errors).toMatchObject({ code: 'LISTING_GATE', need_level: 10, need_days: 7 });
      expect(await flagKinds(seller)).toContain('gate');
      await seedLevel(seller, 10); // 레벨만 올려도 계정 나이가 모자라 불가
      expect((await listReq(app, seller, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 })).status).toBe(422);
      expect((await buyoutReq(app, buyer, id)).status).toBe(200); // 구매는 자격과 무관
    } finally {
      buildApp();
    }
    // 한도: 지금까지 1건 등록, 19건을 더 등록해 20건
    // (위 구매 시도는 판매자 본인이 아니므로 성공했을 수 있다: 새 판매자로 한도를 센다)
    const lim = await mk(app);
    await seedItem(lim, 'eq_sword_10_u', 21);
    for (let i = 0; i < 20; i++) {
      const r = await listReq(app, lim, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 });
      expect(r.status).toBe(201);
    }
    const over = await listReq(app, lim, { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 });
    expect(over.status).toBe(409);
    expect(over.body.errors).toMatchObject({ code: 'LISTING_LIMIT', limit: 20 });
    expect(await countOf(lim, 'eq_sword_10_u')).toBe(1);
    await expectConserved();
  });
});

describe('조회 API', () => {
  it('검색: 이름·초성·분류·정렬·페이지, 응답에 내부 키가 없다', async () => {
    const seller = await mk(app);
    const viewer = await mk(app);
    await listIron(app, seller, { buyout: 900 });
    await listIron(app, seller, { buyout: 500 });
    await seedItem(seller, 'mat_bone', 30);
    const mb = await listReq(app, seller, { item_key: 'mat_bone', count: 30, buyout: 450, hours: 24 });
    expect(mb.status).toBe(201);
    const all = await get(app, viewer, '/auction/search?sort=price_asc&limit=2&page=1');
    expect(all.status).toBe(200);
    expect(all.body.meta).toEqual({ total: 3, page: 1, limit: 2 });
    expect(all.body.data.listings.map((l: { buyout: number }) => l.buyout)).toEqual([450, 500]);
    const one = all.body.data.listings[1];
    expect(one).toMatchObject({ item_key: 'eq_sword_10_u', category: 'weapon', rarity: 1, will_bind: 'account', min_bid: null, start_bid: null });
    expect(Object.keys(one)).not.toContain('seller_account_id');
    expect(typeof one.id).toBe('string');
    const byName = await get(app, viewer, `/auction/search?q=${encodeURIComponent('장검')}`);
    expect(byName.body.meta.total).toBe(2);
    const byInitial = await get(app, viewer, `/auction/search?q=${encodeURIComponent('ㅈㄱ')}`);
    expect(byInitial.body.meta.total).toBe(3); // 장검 2 + '뼈 조각'(ㅈㄱ)
    const bone = await get(app, viewer, '/auction/search?category=material');
    expect(bone.body.meta.total).toBe(1);
    expect(bone.body.data.listings[0]).toMatchObject({ will_bind: null, unit_price: 15 });
    const none = await get(app, viewer, `/auction/search?q=${encodeURIComponent('없는이름')}`);
    expect(none.body.data.listings).toEqual([]);
    const cls = await get(app, viewer, '/auction/search?class=mine&rarity=1,2');
    expect(cls.body.meta.total).toBe(2);
    const bad = await get(app, viewer, '/auction/search?enh_min=5&enh_max=2');
    expect(bad.status).toBe(400);
    expect((await get(app, viewer, '/auction/search?limit=21')).status).toBe(400);
    expect((await get(app, viewer, '/auction/search?evil=1')).status).toBe(400);
  });

  it('내 등록·입찰·요약: 정확한 마감 시각은 내 등록에만', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    const id = await listIron(app, seller, { start_bid: 100 });
    await bidReq(app, bidder, id, 100);
    const mine = await get(app, seller, '/auction/mine');
    expect(mine.body.data.listings[0]).toMatchObject({ id, deposit: 10, fee_pct: 5, extend_count: 0, current_bid: 100 });
    expect(typeof mine.body.data.listings[0].ends_at).toBe('string');
    const theirs = await get(app, bidder, '/auction/mine');
    expect(theirs.body.data.bids[0]).toMatchObject({ id, my_amount: 100, my_bid: true });
    expect(theirs.body.data.bids[0].ends_at).toBeUndefined();
    const sum = await get(app, seller, '/mail/summary');
    expect(sum.body.data).toMatchObject({ unclaimed: 0, active_listings: 1, my_top_bids: 0, new: [] });
    const sum2 = await get(app, bidder, '/mail/summary');
    expect(sum2.body.data).toMatchObject({ unclaimed: 0, active_listings: 0, my_top_bids: 1 });
    // 마감 후 판매자는 대금 우편이 생기고 since 이후 새 우편이 보인다
    advance(12 * HOUR + MIN);
    const s3 = await get(app, seller, `/mail/summary?since=${encodeURIComponent(new Date(Date.now() - 24 * HOUR).toISOString())}`);
    expect(s3.body.data.unclaimed).toBe(1);
    expect(s3.body.data.new[0]).toMatchObject({ kind: 'sold', gold: 100 - 5 + 10 });
  });

  it('시세: 체결 후 평균·최저·최고·일별', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    await buyoutReq(app, buyer, await listIron(app, seller, { buyout: 1000 }));
    await buyoutReq(app, buyer, await listIron(app, seller, { buyout: 2000 }));
    const p = await get(app, seller, '/auction/prices/eq_sword_10_u');
    expect(p.body.data).toMatchObject({ avg7d: 1500, min: 1000, max: 2000, unit_avg: 1500, volume: 2, source: 'vendor' });
    expect(p.body.data.daily).toHaveLength(1);
    expect((await get(app, seller, '/auction/prices/no_such_key')).body.errors.code).toBe('ITEM_UNKNOWN');
    expect((await get(app, seller, '/auction/prices/eq_sword_10_u?count=0')).status).toBe(400);
    expect(await post(app, seller, '/auction/listings', {})).toHaveProperty('status', 400);
  });
});
