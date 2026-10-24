// 0044 캐시 아이템 가치 보존: 외형·뽑기 장비 분해 -> 별의 결정, 별빛 교환소, 장비 도감, 현금화 차단(환불 회수·판매 금지)
import { randomUUID } from 'node:crypto';
import { getPool, withTransaction } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { creditCrystals } from '../src/domains/starshop/crystalWallet';
import { poolOf, slotTable } from '../src/domains/starshop/starshopService';
import {
  PULL_PRICE,
  RARITIES,
  RATE_SCALE,
  STAR_COSMETICS,
  dismantleCrystalsOf,
  dismantleStarsOf,
} from '../src/domains/starshop/starshopDefs';
import { get, post, seedItem } from './economyHelpers';
import { resetDb, shutdown } from './helpers';
import { heroOf, mock, newPayer, payApp, purchase, resetPay, seedStars, sync } from './payHelpers';

const app = payApp();
beforeAll(resetDb);
beforeEach(() => {
  resetPay();
  getRateLimitStore().clear();
});
afterAll(shutdown);

const go = (h: Parameters<typeof post>[1], path: string, body: Record<string, unknown>) => {
  getRateLimitStore().clear();
  return post(app, h, path, body);
};
const giveCosmetic = (accountId: number, itemId: string, copies = 0) =>
  getPool().query(
    `INSERT INTO account_cosmetics (account_id, item_id, source, copies) VALUES ($1, $2, 'grant', $3)
     ON CONFLICT (account_id, item_id) DO UPDATE SET copies = account_cosmetics.copies + EXCLUDED.copies`,
    [accountId, itemId, copies],
  );
const crystalsOf = async (accountId: number) => {
  const r = await getPool().query<{ balance: string; paid_balance: string; debt: string }>('SELECT balance, paid_balance, debt FROM crystal_wallets WHERE account_id = $1', [accountId]);
  const x = r.rows[0];
  return { balance: Number(x?.balance ?? 0), paid: Number(x?.paid_balance ?? 0), debt: Number(x?.debt ?? 0) };
};
const grantCrystals = (accountId: number, amount: number) =>
  withTransaction((c) => creditCrystals(c, { accountId, amount, reason: 'test_grant', ref: 'test', requestId: null }));

describe('0044 캐시 아이템 가치', () => {
  it('1. 외형 여분 분해: 별조각에 더해 결정, 등록한 컬렉션 세트의 외형은 결정 +20%', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await giveCosmetic(p.accountId, 'aura_rose', 3);
    const r = await go(hero, '/starshop/dismantle', { item_id: 'aura_rose', count: 2 });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ stars: 100, crystals: 24 });
    for (const id of ['aura_sunset', 'aura_violet', 'aura_frost', 'aura_jade']) await giveCosmetic(p.accountId, id);
    expect((await go(hero, '/starshop/collection', { set_id: 'col_starlight' })).status).toBe(200);
    const r2 = await go(hero, '/starshop/dismantle', { item_id: 'aura_rose', count: 1 });
    expect(r2.body.data).toMatchObject({ stars: 50, crystals: 14 });
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 38, paid: 0, debt: 0 });
  });

  it('2. 뽑기 장비는 상점에 팔 수 없고 분해하면 결정·재료, 드롭 장비는 분해할 수 없고 팔 수 있다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedStars(p.accountId, 1000);
    expect((await go(hero, '/starshop/pull', { count: 10, banner: 'weapon', tier: 0 })).status).toBe(200);
    const bag = await getPool().query<{ item_key: string; count: number }>(
      "SELECT item_key, count FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key LIKE 'eq\\_%' ORDER BY item_key",
      [hero.dbId],
    );
    const first = bag.rows[0] as { item_key: string; count: number };
    const sold = await go(hero, '/shop/sell', { item_key: first.item_key, count: 1 });
    expect([422]).toContain(sold.status);
    if (sold.body.errors.code !== 'NOT_SELLABLE') expect(sold.body.errors.code).toBe('GACHA_GEAR_NOT_SELLABLE');

    const view = await get(app, hero, '/starshop/exchange-shop');
    expect(view.status).toBe(200);
    expect(view.body.data.dismantlable.reduce((a: number, d: { count: number }) => a + d.count, 0)).toBe(11);

    const d = await go(hero, '/starshop/gear-dismantle', { items: [{ item_key: first.item_key, count: 1 }] });
    expect(d.status).toBe(200);
    expect(d.body.data.crystals).toBeGreaterThan(0);
    expect((await crystalsOf(p.accountId)).balance).toBe(d.body.data.crystals);

    await seedItem(hero, 'eq_sword_1_c', 1);
    const no = await go(hero, '/starshop/gear-dismantle', { items: [{ item_key: 'eq_sword_1_c', count: 1 }] });
    expect(no.status).toBe(422);
    expect(no.body.errors.code).toBe('NOT_GACHA_GEAR');
    expect((await go(hero, '/shop/sell', { item_key: 'eq_sword_1_c', count: 1 })).status).toBe(200);
  });

  it('3. 교환소: 결정으로 구매, 주간 재고, 결정 부족, 외형 고르기(이미 가진 것은 거절)', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await grantCrystals(p.accountId, 1000);
    for (let i = 0; i < 10; i++) expect((await go(hero, '/starshop/exchange-shop/buy', { good_id: 'essence10' })).status).toBe(200);
    const sold = await go(hero, '/starshop/exchange-shop/buy', { good_id: 'essence10' });
    expect(sold.status).toBe(422);
    expect(sold.body.errors.code).toBe('GOOD_SOLD_OUT');
    const ess = await getPool().query<{ n: string }>("SELECT coalesce(sum(count), 0) AS n FROM character_items WHERE character_id = $1 AND item_key = 'mat_essence'", [hero.dbId]);
    expect(Number((ess.rows[0] as { n: string }).n)).toBe(100);
    expect((await crystalsOf(p.accountId)).balance).toBe(600);

    const rare = STAR_COSMETICS.find((c) => c.rarity === 'rare' && !c.skin && !c.exchangeOnly) as { id: string };
    expect((await go(hero, '/starshop/exchange-shop/buy', { good_id: 'pick_rare', item_id: rare.id })).status).toBe(200);
    const again = await go(hero, '/starshop/exchange-shop/buy', { good_id: 'pick_rare', item_id: rare.id });
    expect(again.status).toBe(409);
    const wrong = await go(hero, '/starshop/exchange-shop/buy', { good_id: 'pick_epic_aura', item_id: 'aura_dew' });
    expect(wrong.body.errors.code).toBe('CLAIM_NOT_ALLOWED');
    expect((await go(hero, '/starshop/exchange-shop/buy', { good_id: 'ex_aura_crystal' })).status).toBe(200);
    const poor = await go(hero, '/starshop/exchange-shop/buy', { good_id: 'ex_skin_starwarden' });
    expect(poor.status).toBe(422);
    expect(poor.body.errors.code).toBe('NOT_ENOUGH_CRYSTALS');
  });

  it('4. 유료 별조각으로 뽑은 외형의 결정은 주문 몫으로 쌓이고, 환불 때 남은 몫 회수·쓴 몫은 부채(교환소 거절)', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    const o = await purchase(app, p); // 300 유료
    for (let i = 0; i < 3; i++) expect((await go(hero, '/starshop/pull', { count: 1, banner: 'aura' })).status).toBe(200);
    await giveCosmetic(p.accountId, 'aura_rainbow', 1);
    expect((await go(hero, '/starshop/dismantle', { item_id: 'aura_rainbow', count: 1 })).status).toBe(200);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 60, paid: 60 });
    const lot = await getPool().query<{ granted: string; remaining: string }>('SELECT l.granted, l.remaining FROM crystal_paid_lots l JOIN star_orders o ON o.id = l.order_id WHERE o.uuid = $1', [o.id]);
    expect(lot.rows[0]).toMatchObject({ granted: '60', remaining: '60' });
    expect((await go(hero, '/starshop/exchange-shop/buy', { good_id: 'essence10' })).status).toBe(200);
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 20, paid: 20 });

    mock.setStatus(o.steam_order_id, 'Refunded');
    expect((await sync(app, p, o.id)).body.data.order).toMatchObject({ state: 'refunded' });
    expect(await crystalsOf(p.accountId)).toMatchObject({ balance: 0, paid: 0, debt: 40 });
    const denied = await go(hero, '/starshop/exchange-shop/buy', { good_id: 'essence10' });
    expect(denied.status).toBe(403);
    expect(denied.body.errors.code).toBe('CRYSTAL_DEBT');
  });

  it('5. 외형 뽑기 1회를 모두 분해하면 별조각 기준 40% 이상 남는다(결정 1 = 별조각 2, 직업별 실제 칸 표)', () => {
    for (const banner of ['aura', 'skin'] as const) {
      for (const cls of ['warrior', 'mage', 'rogue']) {
        let value = 0;
        for (const row of slotTable(banner, cls)) {
          for (const c of row.items) value += (row.weight / RATE_SCALE / row.items.length) * (dismantleStarsOf(c) + 2 * dismantleCrystalsOf(c));
        }
        expect({ banner, cls, ok: value / PULL_PRICE >= 0.4 }).toEqual({ banner, cls, ok: true });
      }
    }
  });

  it('6. 교환소 전용·retired 외형은 뽑기·합성 풀에 없다', () => {
    const only = STAR_COSMETICS.filter((c) => c.exchangeOnly).map((c) => c.id);
    const retired = STAR_COSMETICS.filter((c) => c.retired).map((c) => c.id);
    expect(only.length).toBe(5); // 2026-10-23 도적 별빛 그림자 추가
    for (const r of RARITIES) {
      for (const cls of ['warrior', 'mage', 'rogue']) {
        for (const b of ['aura', 'skin'] as const) {
          for (const hidden of [false, true]) {
            for (const c of poolOf(b, r, cls, hidden)) {
              expect(only).not.toContain(c.id);
              expect(retired).not.toContain(c.id);
            }
          }
        }
      }
    }
    void randomUUID;
  });
  const credit = (charId: number, base: string, n: number) =>
    getPool().query(
      `INSERT INTO gacha_gear_credits (character_id, base_id, count) VALUES ($1, $2, $3)
       ON CONFLICT (character_id, base_id) DO UPDATE SET count = gacha_gear_credits.count + EXCLUDED.count`,
      [charId, base, n],
    );
  const rowsOf = async (charId: number, key: string) => {
    const r = await getPool().query<{ bind: string; count: number }>(
      "SELECT bind, count FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2 ORDER BY bind",
      [charId, key],
    );
    return Object.fromEntries(r.rows.map((x) => [x.bind, x.count]));
  };

  it('7. 뽑기 사본(계정)과 드롭 사본(없음)이 함께 있으면 판매는 드롭 사본만 빼고, 뽑기 사본은 팔 수 없다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedItem(hero, 'eq_sword_1_r', 1, 'bag', 'account');
    await credit(hero.dbId, 'eq_sword_1_r', 1);
    await seedItem(hero, 'eq_sword_1_r', 1, 'bag', 'none');
    const sold = await go(hero, '/shop/sell', { item_key: 'eq_sword_1_r', count: 1 });
    expect(sold.status).toBe(200);
    expect(await rowsOf(hero.dbId, 'eq_sword_1_r')).toEqual({ account: 1 });
    const again = await go(hero, '/shop/sell', { item_key: 'eq_sword_1_r', count: 1 });
    expect(again.status).toBe(422);
    expect(again.body.errors.code).toBe('GACHA_GEAR_NOT_SELLABLE');
    // 뽑기 몫은 그대로 남아 뽑기 사본을 분해할 수 있다
    const d = await go(hero, '/starshop/gear-dismantle', { items: [{ item_key: 'eq_sword_1_r', count: 1 }] });
    expect(d.status).toBe(200);
    expect(await rowsOf(hero.dbId, 'eq_sword_1_r')).toEqual({});
  });

  it('8. 분해는 뽑기 사본(계정 귀속)만 빼고, 같은 id의 퀘스트 보상(캐릭터 귀속)은 남긴다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await seedItem(hero, 'eq_sword_1_r', 1, 'bag', 'character');
    await seedItem(hero, 'eq_sword_1_r', 1, 'bag', 'account');
    await credit(hero.dbId, 'eq_sword_1_r', 1);
    const d = await go(hero, '/starshop/gear-dismantle', { items: [{ item_key: 'eq_sword_1_r', count: 1 }] });
    expect(d.status).toBe(200);
    expect(await rowsOf(hero.dbId, 'eq_sword_1_r')).toEqual({ character: 1 });
  });

  it('9. 뽑기 사본을 승급하면 뽑기 몫도 새 기본 id로 옮겨 가 상점에 팔 수 없다', async () => {
    const p = await newPayer(app);
    const hero = await heroOf(app, p);
    await getPool().query('UPDATE characters SET gold = 100000 WHERE id = $1', [hero.dbId]);
    await seedItem(hero, 'mat_core', 5);
    await seedItem(hero, 'eq_sword_1_c', 1, 'bag', 'account');
    await credit(hero.dbId, 'eq_sword_1_c', 1);
    const r = await go(hero, '/promote', { target: { bag_key: 'eq_sword_1_c' } });
    expect(r.status).toBe(200);
    const c = await getPool().query<{ base_id: string; count: number }>(
      'SELECT base_id, count FROM gacha_gear_credits WHERE character_id = $1 AND count > 0 ORDER BY base_id',
      [hero.dbId],
    );
    expect(c.rows).toEqual([{ base_id: 'eq_sword_1_u', count: 1 }]);
    const sold = await go(hero, '/shop/sell', { item_key: 'eq_sword_1_u', count: 1 });
    expect(sold.status).toBe(422);
  });
});
