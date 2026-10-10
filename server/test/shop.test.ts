import { randomUUID } from 'node:crypto';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, goldOf, newHero, post, seedItem, starterCount } from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

describe('POST /characters/:id/shop/buy', () => {
  it('정상: 서버 가격으로 골드를 빼고 가방에 지급, 원장 두 줄', async () => {
    const h = await newHero(app); // 골드 100
    const res = await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 2 });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ item_id: 'potion_hp', count: 2, unit_price: 30, total: 60 });
    expect(res.body.data.delta.gold).toBe(40);
    expect(await goldOf(h)).toBe(40);
    expect(await countOf(h, 'potion_hp')).toBe(starterCount('potion_hp') + 2);
    await expectLedgerConsistent(h);
  });

  it('입력 오류: 가격·총액을 보내거나 수량이 범위 밖이면 400', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 1, price: 1 })).status).toBe(400);
    expect((await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 0 })).status).toBe(400);
    expect((await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 1000 })).status).toBe(400);
    expect((await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 1.5 })).status).toBe(400);
    expect((await post(app, h, '/shop/buy', { item_id: 'eq_sword_10_u', count: 1 })).body.errors.code).toBe('NOT_FOR_SALE');
  });

  it('골드 부족: 422 NOT_ENOUGH_GOLD(need/have)이고 아무것도 바뀌지 않는다', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/shop/buy', { item_id: 'ticket_protect', count: 1 });
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'NOT_ENOUGH_GOLD', need: 3000, have: 100 });
    expect(await goldOf(h)).toBe(100);
    expect(await countOf(h, 'ticket_protect')).toBe(0);
  });

  it('재전송: 같은 request_id는 한 번만 결제', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 1 }, rid);
    const b = await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 1 }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await goldOf(h)).toBe(70);
    const c = await post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 2 }, rid);
    expect(c.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });

  it('동시 요청: 잔액이 한 번 살 만큼뿐이면 한쪽만 성공하고 골드는 음수가 되지 않는다', async () => {
    const h = await newHero(app); // 골드 100(물약 3개 값 90)
    const [a, b] = await Promise.all([
      post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 3 }),
      post(app, h, '/shop/buy', { item_id: 'potion_hp', count: 3 }),
    ]);
    expect([a.status, b.status].sort()).toEqual([200, 422]);
    expect(await goldOf(h)).toBe(10);
    expect(await countOf(h, 'potion_hp')).toBe(starterCount('potion_hp') + 3);
    await expectLedgerConsistent(h);
  });
});

describe('POST /characters/:id/shop/sell', () => {
  it('정상: 재료는 표 가격, 장비는 강화 단계 보너스(.5는 올림)', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_bone', 10);
    const a = await post(app, h, '/shop/sell', { item_key: 'mat_bone', count: 4 });
    expect(a.status).toBe(200);
    expect(a.body.data).toMatchObject({ unit_price: 5, total: 20 });
    expect(await goldOf(h)).toBe(120);
    expect(await countOf(h, 'mat_bone')).toBe(6);

    // 고급검 기본가 31, +2 이면 31 * 1.5 = 46.5 -> 47
    await seedItem(h, 'eq_sword_10_u+2', 1);
    const b = await post(app, h, '/shop/sell', { item_key: 'eq_sword_10_u+2', count: 1 });
    expect(b.body.data).toMatchObject({ unit_price: 47, total: 47 });
    await expectLedgerConsistent(h);
  });

  it('입력 오류와 팔 수 없는 물건', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/shop/sell', { item_key: 'mat_bone', count: 0 })).status).toBe(400);
    expect((await post(app, h, '/shop/sell', { item_key: 'GOLD', count: 1 })).status).toBe(400);
    expect((await post(app, h, '/shop/sell', { item_key: 'gold', count: 1 })).status).toBe(400);
    expect((await post(app, h, '/shop/sell', { item_key: 'mat_bone', count: 1, total: 99 })).status).toBe(400);
    await seedItem(h, 'ticket_protect', 1);
    expect((await post(app, h, '/shop/sell', { item_key: 'ticket_protect', count: 1 })).body.errors.code).toBe('NOT_SELLABLE');
    expect((await post(app, h, '/shop/sell', { item_key: 'key_seal', count: 1 })).body.errors.code).toBe('NOT_SELLABLE');
  });

  it('수량 부족: 422 NOT_ENOUGH_ITEMS이고 아무것도 바뀌지 않는다. 착용 장비는 못 판다', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/shop/sell', { item_key: 'mat_bone', count: 1 });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    // 착용 중인 시작 무기는 가방에 없다
    expect((await post(app, h, '/shop/sell', { item_key: 'eq_sword_wood', count: 1 })).body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    expect(await goldOf(h)).toBe(100);
  });

  it('재전송과 동시 요청: 같은 물건을 두 번 팔 수 없다', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_ore', 1);
    const rid = randomUUID();
    const a = await post(app, h, '/shop/sell', { item_key: 'mat_ore', count: 1 }, rid);
    const b = await post(app, h, '/shop/sell', { item_key: 'mat_ore', count: 1 }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await goldOf(h)).toBe(120);

    const h2 = await newHero(app);
    await seedItem(h2, 'mat_ore', 1);
    const [c, d] = await Promise.all([
      post(app, h2, '/shop/sell', { item_key: 'mat_ore', count: 1 }),
      post(app, h2, '/shop/sell', { item_key: 'mat_ore', count: 1 }),
    ]);
    expect([c.status, d.status].sort()).toEqual([200, 422]);
    expect(await goldOf(h2)).toBe(120);
    await expectLedgerConsistent(h2);
  });
});
