// 13단계 레이드 상점(시험 19~24): 조회, 구매, 멱등, 동시성, 결과 규칙, 입력 검증, 귀속
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { rollShopRarity } from '../src/domains/raidshop/raidShopService';
import { setRng } from '../src/utils/rng';
import { auth, buildApp, resetDb, shutdown, ver } from './helpers';
import { listReq } from './auctionHelpers';
import { anomalyKinds, countOf, expectLedgerConsistent, fakeRng, get, newHero, post, seedItem, type Hero } from './economyHelpers';

const app = buildApp();
beforeEach(async () => {
  await resetDb();
  setRng(null);
});
afterAll(async () => {
  setRng(null);
  await shutdown();
});

const buy = (h: Hero, product: string, requestId?: string, extra: Record<string, unknown> = {}) =>
  post(app, h, '/raid-shop/buy', { product_id: product, ...extra }, requestId);
const shopProduct = (id: string) => (getGameData().raidShop as NonNullable<ReturnType<typeof getGameData>['raidShop']>).products.get(id) as NonNullable<ReturnType<NonNullable<ReturnType<typeof getGameData>['raidShop']>['products']['get']>>;
const ledger = async (h: Hero, reason: string) =>
  (await getPool().query<{ delta: number; item_key: string; request_id: string }>('SELECT delta, item_key, request_id FROM item_ledger WHERE character_id = $1 AND reason = $2 ORDER BY id', [h.dbId, reason])).rows;
const purchases = async (h: Hero) => (await getPool().query('SELECT * FROM raid_shop_purchases WHERE character_id = $1', [h.dbId])).rows;

describe('19. 조회', () => {
  it('상품 4개, have는 가방의 재료 수, outcomes의 장비는 caller 직업', async () => {
    const w = await newHero(app, 'warrior');
    const m = await newHero(app, 'mage');
    await seedItem(w, 'mat_raid_king', 95, 'bag', 'character');
    const rw = await get(app, w, '/raid-shop');
    expect(rw.status).toBe(200);
    expect(rw.body.data.rates_version).toBe('raidshop-1');
    expect(rw.body.data.products.map((p: { id: string }) => p.id).sort()).toEqual(['grah_epic_weapon', 'grah_legend_weapon', 'king_epic_weapon', 'king_legend_weapon']);
    const kingEpic = rw.body.data.products.find((p: { id: string }) => p.id === 'king_epic_weapon');
    expect(kingEpic).toMatchObject({ raid_id: 'raid_skeleton_king', tier_level: 20, affordable: true, material: { item_key: 'mat_raid_king', price: 90, have: 95 } });
    expect(kingEpic.outcomes).toEqual([
      { rarity: 'Epic', percent: 88, item_key: 'eq_sword_20_e' },
      { rarity: 'Unique', percent: 12, item_key: 'eq_sword_20_un' },
    ]);
    const grahLegend = rw.body.data.products.find((p: { id: string }) => p.id === 'grah_legend_weapon');
    expect(grahLegend).toMatchObject({ tier_level: 40, affordable: false, material: { have: 0, price: 100 }, outcomes: [{ rarity: 'Legendary', percent: 100, item_key: 'eq_sword_40_l' }] });
    const rm = await get(app, m, '/raid-shop');
    expect(rm.body.data.products.find((p: { id: string }) => p.id === 'king_legend_weapon').outcomes[0].item_key).toBe('eq_staff_20_l');
    expect((await request(app).get(`/characters/${w.id}/raid-shop`).set(ver())).status).toBe(401);
    const stranger = await newHero(app);
    expect((await request(app).get(`/characters/${w.id}/raid-shop`).set(auth(stranger.s))).status).toBe(404);
  });
});

describe('20. 구매', () => {
  it('재료 부족이면 422 NOT_ENOUGH_MATERIAL(need, have)이고 아무것도 안 바뀐다', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_raid_king', 89, 'bag', 'character');
    const r = await buy(h, 'king_epic_weapon');
    expect(r.status).toBe(422);
    expect(r.body.errors).toMatchObject({ code: 'NOT_ENOUGH_MATERIAL', need: 90, have: 89 });
    expect(await countOf(h, 'mat_raid_king')).toBe(89);
    expect(await ledger(h, 'raid_shop_cost')).toEqual([]);
    expect(await purchases(h)).toEqual([]);
    // 다른 레이드의 재료로는 살 수 없다
    await seedItem(h, 'mat_raid_grah', 500, 'bag', 'character');
    expect((await buy(h, 'king_epic_weapon')).body.errors.code).toBe('NOT_ENOUGH_MATERIAL');
  });

  it('성공: 재료가 정확히 가격만큼 빠지고 캐릭터 귀속 장비 +1, 원장 두 행(같은 request_id), 구매 기록 한 줄. 같은 request_id 재전송은 같은 본문', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_raid_king', 100, 'bag', 'character');
    const r = randomUUID();
    const a = await buy(h, 'king_epic_weapon', r);
    expect(a.status).toBe(200);
    const { item_key: key, rarity } = a.body.data as { item_key: string; rarity: string };
    expect(['eq_sword_20_e', 'eq_sword_20_un']).toContain(key);
    expect(a.body.data).toMatchObject({ product_id: 'king_epic_weapon', cost: { item_key: 'mat_raid_king', count: 90 }, material_left: 10 });
    expect(a.body.data.delta.stacks).toEqual(expect.arrayContaining([expect.objectContaining({ item_key: key, bind: 'character', count: 1 })]));
    expect(await countOf(h, 'mat_raid_king')).toBe(10);
    expect(await countOf(h, key)).toBe(1);
    const bind = await getPool().query("SELECT bind FROM character_items WHERE character_id = $1 AND item_key = $2", [h.dbId, key]);
    expect(bind.rows).toEqual([{ bind: 'character' }]);
    expect(await ledger(h, 'raid_shop_cost')).toEqual([{ delta: -90, item_key: 'mat_raid_king', request_id: r }]);
    expect(await ledger(h, 'raid_shop_result')).toEqual([{ delta: 1, item_key: key, request_id: r }]);
    const p = await purchases(h);
    expect(p).toHaveLength(1);
    expect(p[0]).toMatchObject({ product_id: 'king_epic_weapon', material_key: 'mat_raid_king', material_cost: 90, result_key: key, result_rarity: rarity, rates_version: 'raidshop-1' });

    // 속도 감시: raid_shop_* 는 집계 버킷에 잡히지 않는다(재료 공급이 상한이라 이중 계산이 된다)
    const meter = await getPool().query("SELECT coalesce(sum(item_value), 0) AS v, coalesce(sum(epic_plus), 0) AS e, coalesce(sum(unique_plus), 0) AS u FROM income_hourly WHERE character_id = $1", [h.dbId]);
    expect(meter.rows[0]).toEqual({ v: '0', e: '0', u: '0' });

    const replay = await buy(h, 'king_epic_weapon', r);
    expect(replay.status).toBe(200);
    expect(replay.body).toEqual(a.body);
    expect(await countOf(h, 'mat_raid_king')).toBe(10);
    expect(await ledger(h, 'raid_shop_cost')).toHaveLength(1);
    expect(await purchases(h)).toHaveLength(1);
    // 같은 request_id에 다른 상품은 재사용 오류
    expect((await buy(h, 'king_legend_weapon', r)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    await expectLedgerConsistent(h);
  });
});

describe('21. 동시성', () => {
  it('재료가 한 번 살 만큼만 있을 때 서로 다른 request_id로 동시에 두 번 사면 하나만 성공', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_raid_grah', 30, 'bag', 'character');
    const rs = await Promise.all([buy(h, 'grah_epic_weapon'), buy(h, 'grah_epic_weapon')]);
    expect(rs.map((x) => x.status).sort()).toEqual([200, 422]);
    expect(rs.find((x) => x.status === 422)?.body.errors.code).toBe('NOT_ENOUGH_MATERIAL');
    expect(await countOf(h, 'mat_raid_grah')).toBe(0);
    expect(await purchases(h)).toHaveLength(1);
    expect(await ledger(h, 'raid_shop_result')).toHaveLength(1);
    await expectLedgerConsistent(h);
  });
});

describe('22. 결과 규칙', () => {
  it('레전더리 상품은 항상 caller 직업의 해당 단계 레전더리 무기(전사 검, 마법사 지팡이)', async () => {
    const w = await newHero(app, 'warrior');
    const m = await newHero(app, 'mage');
    await seedItem(w, 'mat_raid_king', 320, 'bag', 'character');
    await seedItem(m, 'mat_raid_grah', 100, 'bag', 'character');
    const a = await buy(w, 'king_legend_weapon');
    expect(a.body.data).toMatchObject({ item_key: 'eq_sword_20_l', rarity: 'Legendary', material_left: 0 });
    const b = await buy(m, 'grah_legend_weapon');
    expect(b.body.data).toMatchObject({ item_key: 'eq_staff_40_l', rarity: 'Legendary' });
    expect(await countOf(m, 'eq_staff_40_l')).toBe(1);
  });

  it('에픽+ 상품은 10만 회 표본에서 유니크 12% +-1%p이고 레전더리가 나오지 않는다', () => {
    for (const id of ['king_epic_weapon', 'grah_epic_weapon']) {
      const p = shopProduct(id);
      const counts: Record<string, number> = {};
      for (let i = 0; i < 100_000; i++) {
        const r = rollShopRarity(p, { int: (lo, hi) => lo + Math.floor(Math.random() * (hi - lo)), unit: Math.random });
        counts[r] = (counts[r] ?? 0) + 1;
      }
      expect(counts.Legendary ?? 0).toBe(0);
      expect(Math.abs(((counts.Unique ?? 0) / 100_000) * 100 - 12)).toBeLessThan(1);
    }
  });

  it('난수 주입: 굴림 값이 유니크 구간이면 유니크 장비를 받는다', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_raid_king', 90, 'bag', 'character');
    setRng(fakeRng({ int: (_min, max) => max - 1 }));
    const r = await buy(h, 'king_epic_weapon');
    expect(r.body.data).toMatchObject({ item_key: 'eq_sword_20_un', rarity: 'Unique' });
  });
});

describe('23. 입력 검증과 정지', () => {
  it('price/count/class/rarity 같은 여분 필드는 400, 모르는 product_id는 422, 경제 정지면 403', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_raid_king', 500, 'bag', 'character');
    for (const extra of [{ price: 1 }, { count: 2 }, { class: 'mage' }, { rarity: 'Legendary' }]) {
      expect((await buy(h, 'king_epic_weapon', undefined, extra)).status).toBe(400);
    }
    expect((await post(app, h, '/raid-shop/buy', {})).status).toBe(400);
    expect((await post(app, h, '/raid-shop/buy', { request_id: 'x', product_id: 'king_epic_weapon' })).status).toBe(400);
    expect((await buy(h, 'King-Epic')).status).toBe(400);
    const unknown = await buy(h, 'no_such_product');
    expect(unknown.status).toBe(422);
    expect(unknown.body.errors.code).toBe('UNKNOWN_PRODUCT');
    expect(await countOf(h, 'mat_raid_king')).toBe(500);

    const acct = (await getPool().query<{ account_id: string }>('SELECT account_id FROM characters WHERE id = $1', [h.dbId])).rows[0]?.account_id;
    await getPool().query(
      `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence)
       VALUES ($1, $2, 'velocity', 'active', '24h', now() - interval '1 day', now() + interval '1 hour', '{"metric":"xp","value":1,"cap":1}'::jsonb)`,
      [acct, h.dbId],
    );
    const held = await buy(h, 'king_epic_weapon');
    expect(held.status).toBe(403);
    expect(held.body.errors.code).toBe('ECONOMY_HOLD');
    expect(await countOf(h, 'mat_raid_king')).toBe(500);
    expect(await anomalyKinds(h)).not.toContain('raid_shop');
  });
});

describe('24. 귀속', () => {
  it('상점으로 산 장비와 전용 재료는 경매에 등록할 수 없다', async () => {
    const h = await newHero(app);
    await seedItem(h, 'mat_raid_king', 90, 'bag', 'character');
    const r = await buy(h, 'king_epic_weapon');
    const key = r.body.data.item_key as string;
    const gear = await listReq(app, h, { item_key: key, count: 1, buyout: 1000, hours: 12 });
    expect(gear.status).toBe(422);
    expect(gear.body.errors.code).toBe('ITEM_BOUND');
    await seedItem(h, 'mat_raid_king', 5, 'bag', 'character');
    const mat = await listReq(app, h, { item_key: 'mat_raid_king', count: 1, buyout: 1000, hours: 12 });
    expect(mat.status).toBe(422);
    expect(['ITEM_BOUND', 'NOT_TRADABLE']).toContain(mat.body.errors.code);
    expect((await getPool().query("SELECT 1 FROM auction_listings WHERE seller_character_id = $1", [h.dbId])).rowCount).toBe(0);
  });
});
