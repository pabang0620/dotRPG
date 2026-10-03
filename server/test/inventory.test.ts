import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, newHero, post, seedItem, wornOf, type Hero } from './economyHelpers';

let app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

describe('POST /characters/:id/items/use', () => {
  it('정상: 1개 소모와 원장(use_item)', async () => {
    const h = await newHero(app); // 물약 3개
    const res = await post(app, h, '/items/use', { item_id: 'potion_hp' });
    expect(res.status).toBe(200);
    expect(res.body.data.delta.stacks).toEqual([{ item_key: 'potion_hp', location: 'bag', count: 2 }]);
    expect(await countOf(h, 'potion_hp')).toBe(2);
    await expectLedgerConsistent(h);
  });

  it('입력 오류와 사용 불가 아이템', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/items/use', { item_id: 'potion_hp', count: 5 })).status).toBe(400);
    expect((await post(app, h, '/items/use', {})).status).toBe(400);
    expect((await post(app, h, '/items/use', { item_id: 'ticket_protect' })).body.errors.code).toBe('NOT_USABLE');
    expect((await post(app, h, '/items/use', { item_id: 'mat_bone' })).body.errors.code).toBe('NOT_USABLE');
  });

  it('수량 부족: 422 NOT_ENOUGH_ITEMS', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/items/use', { item_id: 'carrot' });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('NOT_ENOUGH_ITEMS');
  });

  it('재전송: 같은 request_id는 한 번만 소모', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, h, '/items/use', { item_id: 'potion_hp' }, rid);
    const b = await post(app, h, '/items/use', { item_id: 'potion_hp' }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await countOf(h, 'potion_hp')).toBe(2);
  });

  it('동시 요청: 하나뿐인 주문서를 동시에 써도 한 번만', async () => {
    const h = await newHero(app); // 마을 귀환 주문서 1개
    const [a, b] = await Promise.all([
      post(app, h, '/items/use', { item_id: 'scroll_town' }),
      post(app, h, '/items/use', { item_id: 'scroll_town' }),
    ]);
    expect([a.status, b.status].sort()).toEqual([200, 422]);
    expect(await countOf(h, 'scroll_town')).toBe(0);
    await expectLedgerConsistent(h);
  });

  it('같은 아이템 연속 사용은 간격(기본 500ms) 안이면 429 ITEM_COOLDOWN', async () => {
    app = buildApp({ ITEM_USE_MIN_GAP_MS: '500' });
    const h = await newHero(app);
    expect((await post(app, h, '/items/use', { item_id: 'potion_hp' })).status).toBe(200);
    const fast = await post(app, h, '/items/use', { item_id: 'potion_hp' });
    expect(fast.status).toBe(429);
    expect(fast.body.errors.code).toBe('ITEM_COOLDOWN');
    // 다른 아이템은 막지 않는다
    expect((await post(app, h, '/items/use', { item_id: 'potion_mp' })).status).toBe(200);
    expect(await countOf(h, 'potion_hp')).toBe(2);
    await new Promise((r) => setTimeout(r, 520));
    expect((await post(app, h, '/items/use', { item_id: 'potion_hp' })).status).toBe(200);
    app = buildApp();
  });
});

describe('POST /characters/:id/storage/move', () => {
  it('정상: 가방 -> 창고, all, 창고 -> 가방. 원장 쌍', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/storage/move', {
      moves: [
        { item_key: 'potion_hp', to: 'storage', count: 2 },
        { item_key: 'potion_mp', to: 'storage', count: 'all' },
      ],
    });
    expect(res.status).toBe(200);
    expect(res.body.data.results).toEqual([
      { item_key: 'potion_hp', to: 'storage', moved: 2, error: null },
      { item_key: 'potion_mp', to: 'storage', moved: 2, error: null },
    ]);
    expect(await countOf(h, 'potion_hp', 'bag')).toBe(1);
    expect(await countOf(h, 'potion_hp', 'storage')).toBe(2);
    expect(await countOf(h, 'potion_mp', 'bag')).toBe(0);
    const back = await post(app, h, '/storage/move', { moves: [{ item_key: 'potion_hp', to: 'bag', count: 'all' }] });
    expect(back.body.data.results[0].moved).toBe(2);
    expect(await countOf(h, 'potion_hp', 'bag')).toBe(3);
    await expectLedgerConsistent(h);
  });

  it('입력 오류: 빈 목록, 61개, 골드, 0개', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/storage/move', { moves: [] })).status).toBe(400);
    const many = Array.from({ length: 61 }, () => ({ item_key: 'potion_hp', to: 'storage', count: 1 }));
    expect((await post(app, h, '/storage/move', { moves: many })).status).toBe(400);
    expect((await post(app, h, '/storage/move', { moves: [{ item_key: 'gold', to: 'storage', count: 1 }] })).status).toBe(400);
    expect((await post(app, h, '/storage/move', { moves: [{ item_key: 'potion_hp', to: 'storage', count: 0 }] })).status).toBe(400);
    expect((await post(app, h, '/storage/move', { moves: [{ item_key: 'potion_hp', to: 'mail', count: 1 }] })).status).toBe(400);
  });

  it('수량 부족과 창고 가득: 항목마다 독립이고 200 안의 error로 알린다', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 48; i++) await seedItem(h, `zz_filler_${i}`, 1, 'storage');
    const res = await post(app, h, '/storage/move', {
      moves: [
        { item_key: 'potion_hp', to: 'storage', count: 99 }, // 수량 부족
        { item_key: 'potion_hp', to: 'storage', count: 1 }, // 창고 가득(새 종류)
        { item_key: 'zz_filler_0', to: 'bag', count: 1 }, // 꺼내기는 된다
        { item_key: 'potion_mp', to: 'storage', count: 1 }, // 방금 한 칸이 비었으므로 들어간다
      ],
    });
    expect(res.status).toBe(200);
    expect(res.body.data.results.map((r: { error: string | null }) => r.error)).toEqual([
      'NOT_ENOUGH_ITEMS',
      'STORAGE_FULL',
      null,
      null,
    ]);
    await expectLedgerConsistent(h);
  });

  it('재전송과 동시 요청: 같은 물건을 두 번 옮기지 못한다', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const body = { moves: [{ item_key: 'potion_hp', to: 'storage', count: 3 }] };
    const a = await post(app, h, '/storage/move', body, rid);
    const b = await post(app, h, '/storage/move', body, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);

    const h2 = await newHero(app);
    const [c, d] = await Promise.all([post(app, h2, '/storage/move', body), post(app, h2, '/storage/move', body)]);
    const moved = [c, d].map((r) => r.body.data.results[0].moved).sort();
    expect(moved).toEqual([0, 3]);
    expect(await countOf(h2, 'potion_hp', 'storage')).toBe(3);
    await expectLedgerConsistent(h2);
  });
});

describe('POST /characters/:id/equipment/equip, unequip', () => {
  const equip = (h: Hero, body: Record<string, unknown>, rid?: string) => post(app, h, '/equipment/equip', body, rid);
  const unequip = (h: Hero, body: Record<string, unknown>, rid?: string) => post(app, h, '/equipment/unequip', body, rid);

  it('정상: 장착하면 이전 장비는 가방으로 돌아간다', async () => {
    const h = await newHero(app);
    await seedItem(h, 'eq_sword_iron+3', 1);
    const res = await equip(h, { item_key: 'eq_sword_iron+3' });
    expect(res.status).toBe(200);
    expect(await wornOf(h, 0)).toBe('eq_sword_iron+3');
    expect(await countOf(h, 'eq_sword_wood')).toBe(1);
    expect(await countOf(h, 'eq_sword_iron+3')).toBe(0);
    expect(res.body.data.delta.worn).toEqual([{ slot: 0, item_key: 'eq_sword_iron+3' }]);
    await expectLedgerConsistent(h);
    const off = await unequip(h, { slot: 0 });
    expect(off.status).toBe(200);
    expect(await wornOf(h, 0)).toBeNull();
    expect(await countOf(h, 'eq_sword_iron+3')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('마법사는 무기를 뺄 수 없다(C# FixSlots와 같게), 다른 무기로 바꾸는 것은 된다', async () => {
    const h = await newHero(app, 'mage');
    const off = await unequip(h, { slot: 0 });
    expect(off.status).toBe(422);
    expect(off.body.errors.code).toBe('WEAPON_REQUIRED');
    expect(await wornOf(h, 0)).not.toBeNull();
    await expectLedgerConsistent(h);
  });

  it('반지: 슬롯 지정 없으면 반지1 -> 반지2 -> 둘 다 차면 반지1 교체', async () => {
    const h = await newHero(app);
    await seedItem(h, 'eq_ring_copper', 1);
    await seedItem(h, 'eq_ring_wind', 1);
    await seedItem(h, 'eq_ring_ruby', 1);
    await equip(h, { item_key: 'eq_ring_copper' });
    await equip(h, { item_key: 'eq_ring_wind' });
    expect([await wornOf(h, 2), await wornOf(h, 3)]).toEqual(['eq_ring_copper', 'eq_ring_wind']);
    await equip(h, { item_key: 'eq_ring_ruby' });
    expect([await wornOf(h, 2), await wornOf(h, 3)]).toEqual(['eq_ring_ruby', 'eq_ring_wind']);
    expect(await countOf(h, 'eq_ring_copper')).toBe(1);
    // 슬롯 지정
    await seedItem(h, 'eq_ring_copper', 1);
    await equip(h, { item_key: 'eq_ring_copper', slot: 3 });
    expect(await wornOf(h, 3)).toBe('eq_ring_copper');
    await expectLedgerConsistent(h);
  });

  it('오류: 가방에 없음, 장비 아님, 직업 불일치, 슬롯 불일치, 입력 형식', async () => {
    const h = await newHero(app); // 전사
    expect((await equip(h, { item_key: 'eq_sword_iron' })).body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    expect((await equip(h, { item_key: 'potion_hp' })).body.errors.code).toBe('NOT_EQUIPMENT');
    await seedItem(h, 'eq_staff_crystal', 1);
    expect((await equip(h, { item_key: 'eq_staff_crystal' })).body.errors.code).toBe('CLASS_MISMATCH');
    await seedItem(h, 'eq_neck_leaf', 1);
    expect((await equip(h, { item_key: 'eq_neck_leaf', slot: 2 })).body.errors.code).toBe('SLOT_MISMATCH');
    expect((await equip(h, { item_key: 'eq_neck_leaf', slot: 5 })).status).toBe(400);
    expect((await equip(h, { item_key: 'eq_neck_leaf', extra: 1 })).status).toBe(400);
    expect(await countOf(h, 'eq_neck_leaf')).toBe(1);
  });

  it('해제 오류: 빈 슬롯은 422 SLOT_EMPTY, 범위 밖은 400', async () => {
    const h = await newHero(app);
    expect((await unequip(h, { slot: 1 })).body.errors.code).toBe('SLOT_EMPTY');
    expect((await unequip(h, { slot: 6 })).status).toBe(400);
  });

  it('재전송과 동시 요청: 같은 장비를 두 번 장착할 수 없다', async () => {
    const h = await newHero(app);
    await seedItem(h, 'eq_sword_iron', 1);
    const rid = randomUUID();
    const a = await equip(h, { item_key: 'eq_sword_iron' }, rid);
    const b = await equip(h, { item_key: 'eq_sword_iron' }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);

    const h2 = await newHero(app);
    await seedItem(h2, 'eq_sword_iron', 1);
    const [c, d] = await Promise.all([equip(h2, { item_key: 'eq_sword_iron' }), equip(h2, { item_key: 'eq_sword_iron' })]);
    expect([c.status, d.status].sort()).toEqual([200, 422]);
    const rows = await getPool().query(
      "SELECT count(*)::int AS n FROM character_items WHERE character_id = $1 AND item_key = 'eq_sword_iron'",
      [h2.dbId],
    );
    expect(rows.rows[0].n).toBe(1);
    await expectLedgerConsistent(h2);
  });
});
