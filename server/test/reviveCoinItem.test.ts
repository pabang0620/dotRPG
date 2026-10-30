// 0056 부활 코인 아이템: 가방의 item_revive_coin 사용 -> 캐릭터 부활 코인 +1(보유 상한 100, 일일 지급은 5까지),
// 별조각 상품(부활 코인 묶음) 구매, 환불 결과물 회수
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { resetClock, setNowAt } from './auctionHelpers';
import { countOf, get, post, seedItem, type Hero } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { adminApp, adminPost, makeAdmin, rid } from './opsHelpers';
import { expectWalletConsistent, heroOf, mock, newPayer, payApp, purchase, resetPay, seedStars, sync, walletState } from './payHelpers';

const COIN = 'item_revive_coin';
const app = payApp();
const admin = adminApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  resetClock();
  getRateLimitStore().clear();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const go = (h: Hero, path: string, body: Record<string, unknown>, requestId?: string) => {
  getRateLimitStore().clear();
  return post(app, h, path, body, requestId);
};
const useCoin = (h: Hero, requestId?: string) => go(h, '/items/use', { item_id: COIN }, requestId);
const coinsOf = async (h: Hero) =>
  (await getPool().query<{ revive_coins: number }>('SELECT revive_coins FROM characters WHERE id = $1', [h.dbId])).rows[0]!.revive_coins;
const setCoins = (h: Hero, coins: number, day: string | null) =>
  getPool().query('UPDATE characters SET revive_coins = $2, revive_coin_day = $3::date WHERE id = $1', [h.dbId, coins, day]);
const itemLogs = async (h: Hero) =>
  Number((await getPool().query<{ n: string }>("SELECT count(*) AS n FROM revive_log WHERE character_id = $1 AND context = 'item'", [h.dbId])).rows[0]!.n);
const newHeroP = async () => {
  const p = await newPayer(app);
  return { p, h: await heroOf(app, p) };
};

describe('부활 코인 아이템 사용', () => {
  beforeEach(() => setNowAt(new Date('2026-10-07T10:00:05Z'))); // 게임 일자 2026-10-07
  it('아이템 1개 -> 코인 +1, 원장 양쪽(item_ledger use_item, revive_log item)', async () => {
    const { h } = await newHeroP();
    await seedItem(h, COIN, 3, 'bag', 'account');
    const r = await useCoin(h);
    expect(r.status).toBe(200);
    expect(r.body.data.revive).toMatchObject({ coins: 2, max: 100 });
    expect(await coinsOf(h)).toBe(2);
    expect(await countOf(h, COIN)).toBe(2);
    expect(await itemLogs(h)).toBe(1);
    const led = await getPool().query("SELECT delta FROM item_ledger WHERE character_id = $1 AND item_key = $2 AND reason = 'use_item'", [h.dbId, COIN]);
    expect(led.rows).toEqual([{ delta: -1 }]);
    const st = await get(app, h, '/revive');
    expect(st.body.data).toMatchObject({ coins: 2, max: 100, daily_max: 5 });
  });

  it('같은 request_id 재전송은 한 번만 반영하고 같은 응답을 돌려준다', async () => {
    const { h } = await newHeroP();
    await seedItem(h, COIN, 3, 'bag', 'account');
    const id = randomUUID();
    const a = await useCoin(h, id);
    const b = await useCoin(h, id);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(b.body.data).toEqual(a.body.data);
    expect(await coinsOf(h)).toBe(2);
    expect(await countOf(h, COIN)).toBe(2);
    expect(await itemLogs(h)).toBe(1);
  });

  it('아이템 사용 request_id를 /revive에 다시 써도 충돌 없이 각자 처리된다', async () => {
    const { h } = await newHeroP();
    await seedItem(h, COIN, 2, 'bag', 'account');
    await getPool().query('UPDATE characters SET level = 20 WHERE id = $1', [h.dbId]);
    const id = randomUUID();
    const a = await useCoin(h, id);
    expect(a.status).toBe(200);
    const rv = await go(h, '/revive', { context: 'dungeon' }, id);
    expect(rv.status).toBe(200);
    expect(rv.body.data).toMatchObject({ free: false, coins: 1 });
    // 같은 id로 아이템을 다시 써도 첫 결과 그대로(재전송), 코인은 늘지 않는다
    const a2 = await useCoin(h, id);
    expect(a2.status).toBe(200);
    expect(a2.body.data).toEqual(a.body.data);
    expect(await coinsOf(h)).toBe(1);
  });

  it('동시 요청 2개에 아이템이 1개면 하나만 성공한다', async () => {
    const { h } = await newHeroP();
    await seedItem(h, COIN, 1, 'bag', 'account');
    getRateLimitStore().clear();
    const rs = await Promise.all([post(app, h, '/items/use', { item_id: COIN }), post(app, h, '/items/use', { item_id: COIN })]);
    expect(rs.filter((r) => r.status === 200)).toHaveLength(1);
    expect(await coinsOf(h)).toBe(2);
    expect(await countOf(h, COIN)).toBe(0);
    expect(await itemLogs(h)).toBe(1);
  });

  it('아이템이 없으면 422 NOT_ENOUGH_ITEMS, 코인은 그대로', async () => {
    const { h } = await newHeroP();
    const r = await useCoin(h);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    expect(await coinsOf(h)).toBe(1);
  });

  it('보유 100이면 409 REVIVE_COINS_FULL이고 아이템은 남는다. 99면 100까지 채운다', async () => {
    const { h } = await newHeroP();
    await seedItem(h, COIN, 2, 'bag', 'account');
    await setCoins(h, 99, '2026-10-07');
    expect((await useCoin(h)).status).toBe(200);
    expect(await coinsOf(h)).toBe(100);
    const r = await useCoin(h);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('REVIVE_COINS_FULL');
    expect(await coinsOf(h)).toBe(100);
    expect(await countOf(h, COIN)).toBe(1);
  });

  it('일일 무료 지급은 5까지만 채우고, 5보다 많이 가졌으면 그대로 둔다', async () => {
    const { h } = await newHeroP();
    await setCoins(h, 3, '2026-09-01');
    expect((await get(app, h, '/revive')).body.data.coins).toBe(5);
    await setCoins(h, 50, '2026-10-01');
    expect((await get(app, h, '/revive')).body.data.coins).toBe(50);
    await seedItem(h, COIN, 1, 'bag', 'account');
    await setCoins(h, 4, '2026-10-05'); // 2일 지남: 지급 4 -> 5, 아이템 +1 -> 6
    const r = await useCoin(h);
    expect(r.body.data.revive.coins).toBe(6);
  });

  it('잘못된 입력은 400(검증 실패)', async () => {
    const { h } = await newHeroP();
    const r = await go(h, '/items/use', { item_id: COIN, extra: 1 });
    expect(r.status).toBe(400);
  });
});

describe('별조각 상품: 부활 코인 묶음', () => {
  it('교환소 조회에 별조각 상품과 별조각 잔액이 있다', async () => {
    const { p, h } = await newHeroP();
    await seedStars(p.accountId, 500);
    const r = await get(app, h, '/starshop/exchange-shop');
    expect(r.status).toBe(200);
    expect(r.body.data.star_balance).toBe(500);
    expect(r.body.data.star_goods.map((g: { id: string }) => g.id)).toEqual(['revive_coin_1', 'revive_coin_10', 'revive_coin_50']);
  });

  it('10개 묶음을 사면 별조각 270을 내고 가방에 계정 귀속 10개가 온다. 재전송은 한 번만', async () => {
    const { p, h } = await newHeroP();
    await seedStars(p.accountId, 1000);
    const id = randomUUID();
    const a = await go(h, '/starshop/item-buy', { good_id: 'revive_coin_10' }, id);
    expect(a.status).toBe(200);
    expect(a.body.data).toMatchObject({ good_id: 'revive_coin_10', item_id: COIN, count: 10, price: 270, balance: 730 });
    const b = await go(h, '/starshop/item-buy', { good_id: 'revive_coin_10' }, id);
    expect(b.body.data).toEqual(a.body.data);
    expect(await countOf(h, COIN)).toBe(10);
    const rows = await getPool().query("SELECT bind, count FROM character_items WHERE character_id = $1 AND item_key = $2", [h.dbId, COIN]);
    expect(rows.rows).toEqual([{ bind: 'account', count: 10 }]);
    expect((await walletState(p.accountId)).balance).toBe(730);
    const led = await getPool().query("SELECT delta::int AS delta, ref FROM star_ledger WHERE account_id = $1 AND reason = 'item_buy'", [p.accountId]);
    expect(led.rows).toEqual([{ delta: -270, ref: 'revive_coin_10' }]);
    const il = await getPool().query("SELECT delta FROM item_ledger WHERE character_id = $1 AND reason = 'star_item_buy'", [h.dbId]);
    expect(il.rows).toEqual([{ delta: 10 }]);
    await expectWalletConsistent(p.accountId);
  });

  it('구매 한도 없음: 50개 묶음 세 번(150개)도 산다. 보유 상한을 넘는 몫은 가방에 남는다', async () => {
    const { p, h } = await newHeroP();
    await seedStars(p.accountId, 3600);
    for (let i = 0; i < 3; i++) expect((await go(h, '/starshop/item-buy', { good_id: 'revive_coin_50' })).status).toBe(200);
    expect(await countOf(h, COIN)).toBe(150);
    await setCoins(h, 100, null);
    expect((await useCoin(h)).status).toBe(409);
    expect(await countOf(h, COIN)).toBe(150);
  });

  it('별조각이 모자라면 422 NOT_ENOUGH_STARS, 없는 상품은 404, 형식이 틀리면 400', async () => {
    const { p, h } = await newHeroP();
    await seedStars(p.accountId, 20);
    const r = await go(h, '/starshop/item-buy', { good_id: 'revive_coin_1' });
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('NOT_ENOUGH_STARS');
    expect((await go(h, '/starshop/item-buy', { good_id: 'nope' })).status).toBe(404);
    expect((await go(h, '/starshop/item-buy', { good_id: 'Bad Id!' })).status).toBe(400);
    expect(await countOf(h, COIN)).toBe(0);
  });

  it('동시 구매 2개에 별조각이 한 번 몫이면 하나만 성공한다', async () => {
    const { p, h } = await newHeroP();
    await seedStars(p.accountId, 300);
    getRateLimitStore().clear();
    const rs = await Promise.all([
      post(app, h, '/starshop/item-buy', { good_id: 'revive_coin_10' }),
      post(app, h, '/starshop/item-buy', { good_id: 'revive_coin_10' }),
    ]);
    expect(rs.map((r) => r.status).sort()).toEqual([200, 422]);
    expect(await countOf(h, COIN)).toBe(10);
    expect((await walletState(p.accountId)).balance).toBe(30);
  });

  it('환불 결과물 회수: 산 코인 중 가방에 남은 만큼 걷고 쓴 만큼은 모자란 수량', async () => {
    const owner = await makeAdmin('owner');
    const { p, h } = await newHeroP();
    const o = await purchase(app, p, 'stars_300');
    expect((await go(h, '/starshop/item-buy', { good_id: 'revive_coin_10' })).status).toBe(200);
    expect((await useCoin(h)).status).toBe(200);
    mock.setStatus(o.steam_order_id, 'Refunded');
    await sync(app, p, o.id);
    const path = `/admin/payments/orders/${o.id}/revoke-outcomes`;
    const inc = { cosmetics: true, gear: true, gauge: true };
    const pv = await adminPost(admin, owner, path, { mode: 'preview', include: inc, note: '확정 부정' });
    expect(pv.status).toBe(200);
    expect(pv.body.data.targets.gear).toEqual([{ character_id: h.id, item_key: COIN, count: 10 }]);
    const ap = await adminPost(admin, owner, path, { mode: 'apply', include: inc, preview_hash: pv.body.data.preview_hash, note: '확정 부정' }, rid());
    expect(ap.status).toBe(200);
    expect(ap.body.data).toMatchObject({ gear_removed: 9, gear_shortfall: 1 });
    expect(await countOf(h, COIN)).toBe(0);
  });
});
