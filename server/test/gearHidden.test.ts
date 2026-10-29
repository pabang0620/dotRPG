// 장비 히든 옵션(Docs/PLAN_GEAR_HIDDEN_2026-10-29.md): 키 형식 왕복, 2% 굴림(고정 난수), 대상 등급·부위,
// 화력 상한 여유, 레이드 상점 생성 시 굴림, 강화·계승·승급·경매·교환이 옵션을 지키는지
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { getConfig } from '../src/config/env';
import { factsOf } from '../src/domains/auction/auctionRules';
import { statsOf } from '../src/domains/achievements/achievementRepository';
import { computePower } from '../src/domains/characters/powerEstimate';
import { attackCap, gearAttack } from '../src/domains/kills/killRules';
import {
  HIDDEN_CHANCE_BP,
  hiddenAttackMul,
  hiddenPoolOf,
  hiddenValidFor,
  rollHidden,
  rollHiddenCards,
  setHiddenRng,
} from '../src/utils/hiddenOption';
import { ITEM_KEY_RE, keyAt, parseItemKey } from '../src/utils/itemKey';
import type { Rng } from '../src/utils/rng';
import { setRng } from '../src/utils/rng';
import { buildApp, mk, listReq } from './auctionHelpers';
import { countOf, expectLedgerConsistent, fakeRng, post, seedItem, type Hero } from './economyHelpers';
import { auth, resetDb, shutdown } from './helpers';

const app = buildApp();
beforeAll(resetDb);
afterEach(() => {
  setRng(null);
  setHiddenRng(null);
});
afterAll(async () => {
  setRng(null);
  setHiddenRng(null);
  await shutdown();
});

const eco = () => getGameData().economy;
/** 히든 굴림 난수: 첫 int(0, 10000)은 chance, 둘째 int(0, n)은 pick */
function hiddenRng(chance: number, pick = 0): Rng {
  let n = 0;
  return { int: (min, max) => (n++ % 2 === 0 ? Math.min(chance, max - 1) : Math.min(min + pick, max - 1)), unit: () => 1 };
}

describe('키 형식', () => {
  it('히든 옵션은 맨 뒤 "~글자", 이때 +0도 적는다. 계승 횟수와 같이 쓸 수 있다', () => {
    expect(keyAt('eq_sword_20_un', 0, 0, 'd')).toBe('eq_sword_20_un+0~d');
    expect(keyAt('eq_sword_30_un', 12, 2, 'c')).toBe('eq_sword_30_un+12^2~c');
    expect(keyAt('eq_sword_20_un', 5, 0, '')).toBe('eq_sword_20_un+5');
    for (const k of ['eq_sword_20_un+0~d', 'eq_sword_20_un+12~s', 'eq_sword_30_un+12^2~c', 'eq_plate_20_un+0^1~h']) {
      const p = parseItemKey(k);
      expect(p).not.toBeNull();
      expect(keyAt(p!.base, p!.level, p!.inherit, p!.hidden)).toBe(k);
      expect(k.split('+')[0]).toBe(p!.base); // split_part(key, '+', 1)은 그대로 기본 id
    }
    expect(parseItemKey('eq_sword_30_un+12^2~c')).toEqual({ base: 'eq_sword_30_un', level: 12, inherit: 2, hidden: 'c' });
    // 옵션이 없는 키는 hidden 속성 자체가 없다(기존 비교 그대로)
    expect(parseItemKey('eq_sword_20_r+12^1')).toEqual({ base: 'eq_sword_20_r', level: 12, inherit: 1 });
    for (const bad of ['eq_sword_20_un~d', 'eq_sword_20_un+0~', 'eq_sword_20_un+0~dd', 'eq_sword_20_un+0~D', 'eq_sword_20_un+0~d^1', 'eq_sword_20_un+05~d']) {
      expect(ITEM_KEY_RE.test(bad)).toBe(false);
    }
  });

  it('옵션 글자는 장비 부위에 맞아야 한다(경매 분류·관리자 지급 검사)', () => {
    expect(hiddenValidFor(eco(), 'eq_sword_20_un+0~d')).toBe(true);
    expect(hiddenValidFor(eco(), 'eq_sword_20_un+0~h')).toBe(false);
    expect(hiddenValidFor(eco(), 'eq_plate_20_un+0~g')).toBe(true);
    expect(hiddenValidFor(eco(), 'eq_ring_20_un+0~d')).toBe(false);
    expect(hiddenValidFor(eco(), 'eq_sword_20_e+0~d')).toBe(false);
    expect(factsOf('eq_sword_20_un+3~d')).toMatchObject({ base: 'eq_sword_20_un', enhance: 3, category: 'weapon' });
    expect(factsOf('eq_sword_20_un+3~h')).toBeNull();
    expect(factsOf('mat_ore+0~d')).toBeNull();
  });
});

describe('굴림', () => {
  it('대상: 유니크·레전더리 무기·상의·하의만(장신구·에픽 이하는 없음)', () => {
    const get = (id: string) => eco().shop.equipment.get(id);
    expect(hiddenPoolOf(get('eq_sword_20_un'))).toEqual(['d', 'r', 's', 'c']);
    expect(hiddenPoolOf(get('eq_sword_20_l'))).toEqual(['d', 'r', 's', 'c']);
    expect(hiddenPoolOf(get('eq_plate_20_un'))).toEqual(['h', 'g', 'm']);
    expect(hiddenPoolOf(get('eq_ring_20_un'))).toEqual([]);
    expect(hiddenPoolOf(get('eq_sword_20_e'))).toEqual([]);
    // 대상이 아니면 난수를 쓰지 않고 그대로
    const never = { int: () => { throw new Error('굴리면 안 된다'); }, unit: () => 1 };
    expect(rollHidden(eco(), 'eq_sword_20_e', never)).toBe('eq_sword_20_e');
    expect(rollHidden(eco(), 'eq_ring_20_un', never)).toBe('eq_ring_20_un');
    expect(rollHidden(eco(), 'mat_ore', never)).toBe('mat_ore');
    expect(rollHidden(eco(), 'eq_sword_20_un+0~d', never)).toBe('eq_sword_20_un+0~d'); // 이미 있으면 다시 굴리지 않는다
  });

  it('확률: 만분율 0~199(200칸 = 2%)만 붙고 200부터는 안 붙는다. 옵션은 같은 확률로 고른다', () => {
    expect(HIDDEN_CHANCE_BP).toBe(200);
    let hits = 0;
    for (let u = 0; u < 10_000; u++) if (rollHidden(eco(), 'eq_sword_20_un', hiddenRng(u)) !== 'eq_sword_20_un') hits++;
    expect(hits).toBe(200);
    expect(['d', 'r', 's', 'c'].map((_, i) => rollHidden(eco(), 'eq_sword_20_un', hiddenRng(199, i)))).toEqual([
      'eq_sword_20_un+0~d', 'eq_sword_20_un+0~r', 'eq_sword_20_un+0~s', 'eq_sword_20_un+0~c',
    ]);
    expect([0, 1, 2].map((i) => rollHidden(eco(), 'eq_plate_20_un', hiddenRng(0, i)))).toEqual([
      'eq_plate_20_un+0~h', 'eq_plate_20_un+0~g', 'eq_plate_20_un+0~m',
    ]);
  });

  it('카드 목록: 골드·재료 카드는 그대로, 장비 카드만 굴린다. 기본(테스트) 난수는 붙이지 않는다', () => {
    const cards = [{ item_key: 'gold', count: 50 }, { item_key: 'mat_ore', count: 2 }, { item_key: 'eq_sword_20_un', count: 1 }];
    expect(rollHiddenCards(eco(), cards)).toEqual(cards);
    setHiddenRng(hiddenRng(0, 3));
    expect(rollHiddenCards(eco(), cards).map((c) => c.item_key)).toEqual(['gold', 'mat_ore', 'eq_sword_20_un+0~c']);
  });
});

describe('화력 상한·전투력', () => {
  const pol = () => getConfig().policy;
  it('피해: 무기 공격 x1.3. 공격 속도 x1.15, 치명타 x1.05를 상한에 더한다(정상 장비가 걸리지 않게)', () => {
    const plain = 'eq_sword_20_un+10';
    expect(gearAttack(eco(), 'eq_sword_20_un+10~d')).toBeCloseTo(gearAttack(eco(), plain) * 1.3, 6);
    const cap = (k: string) => attackCap(eco(), pol(), 20, [k]);
    expect(cap('eq_sword_20_un+10~d')).toBeGreaterThan(cap(plain));
    expect(cap('eq_sword_20_un+10~s') / cap(plain)).toBeCloseTo(1.15, 6);
    expect(cap('eq_sword_20_un+10~c') / cap(plain)).toBeCloseTo(1.05, 6);
    expect(cap('eq_sword_20_un+10~r')).toBeCloseTo(cap(plain), 6);
    expect(hiddenAttackMul(['eq_sword_20_un+0~s', 'eq_plate_20_un+0~h'])).toBeCloseTo(1.15, 6);
    // 레전더리 + 피해 옵션 + 만강도 상한 계산이 그대로 이어진다
    expect(Number.isFinite(cap('eq_sword_20_l+20^1~d'))).toBe(true);
  });

  it('전투력: 체력 옵션은 방어구 체력 x1.3, 이동 옵션은 이동 +10', () => {
    const base = computePower('warrior', 20, ['eq_plate_20_un'], 0);
    expect(computePower('warrior', 20, ['eq_plate_20_un+0~h'], 0)).toBeGreaterThan(base);
    const p = eco().player.power;
    expect(computePower('warrior', 20, ['eq_plate_20_un+0~m'], 0)).toBe(base + 10 * p.speedWeight);
    expect(computePower('warrior', 20, ['eq_sword_20_un+5~d'], 0)).toBeGreaterThan(computePower('warrior', 20, ['eq_sword_20_un+5'], 0));
  });
});

describe('생성·유지(API)', () => {
  it('레이드 상점: 상자를 열 때 서버가 굴린다. 걸리면 결과 키·원장·구매 기록에 옵션이 남는다', async () => {
    const h = await mk(app);
    await seedItem(h, 'mat_raid_king', 1000, 'bag', 'character');
    setHiddenRng(hiddenRng(0, 1));
    const a = await post(app, h, '/raid-shop/buy', { product_id: 'king_legend_weapon' });
    expect(a.status).toBe(200);
    const key = a.body.data.item_key as string;
    expect(key).toMatch(/^eq_sword_20_lb?\+0~r$/);
    expect(await countOf(h, key)).toBe(1);
    const led = await getPool().query("SELECT item_key FROM item_ledger WHERE character_id = $1 AND reason = 'raid_shop_result'", [h.dbId]);
    expect(led.rows).toEqual([{ item_key: key }]);
    const pur = await getPool().query('SELECT result_key FROM raid_shop_purchases WHERE character_id = $1', [h.dbId]);
    expect(pur.rows).toEqual([{ result_key: key }]);
    // 안 걸리면(만분율 200) 옵션 없는 기본 키
    setHiddenRng(hiddenRng(200));
    const b = await post(app, h, '/raid-shop/buy', { product_id: 'king_legend_weapon' });
    expect(b.status).toBe(200);
    expect(b.body.data.item_key).toMatch(/^eq_sword_20_lb?$/);
    await expectLedgerConsistent(h);
  });

  it('강화는 옵션을 지킨다(성공 +1, 업적의 최고 강화 단계도 읽힌다)', async () => {
    const h = await mk(app);
    await seedItem(h, 'mat_bone', 500);
    await seedItem(h, 'mat_ore', 500);
    await seedItem(h, 'eq_sword_20_un+4~d', 1);
    setRng(fakeRng({ int: () => 0 }));
    const r = await post(app, h, '/enhance', { target: { bag_key: 'eq_sword_20_un+4~d' } });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ outcome: 'success', old_key: 'eq_sword_20_un+4~d', new_key: 'eq_sword_20_un+5~d' });
    expect(await countOf(h, 'eq_sword_20_un+5~d')).toBe(1);
    expect((await statsOf(getPool(), h.dbId)).enhance_best).toBe(5);
    await expectLedgerConsistent(h);
  });

  it('계승은 옵션을 지킨다(+12 유지 -> 다음 레벨대 ^1~옵션)', async () => {
    const h = await mk(app, 1_000_000);
    await seedItem(h, 'eq_sword_10_un+12~c', 1);
    setRng(fakeRng({ int: () => 0 }));
    const r = await post(app, h, '/inherit', { target: { bag_key: 'eq_sword_10_un+12~c' } });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ old_key: 'eq_sword_10_un+12~c', new_key: 'eq_sword_20_un+12^1~c', kept: true });
    expect(await countOf(h, 'eq_sword_20_un+12^1~c')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('승급은 옵션을 새로 굴리지 않고 그대로 옮긴다(유니크 -> 레전더리)', async () => {
    const h = await mk(app, 1_000_000);
    await seedItem(h, 'mat_core', 30);
    await seedItem(h, 'eq_sword_20_un+3~s', 1);
    setHiddenRng({ int: () => { throw new Error('승급은 굴리지 않는다'); }, unit: () => 1 });
    const r = await post(app, h, '/promote', { target: { bag_key: 'eq_sword_20_un+3~s' } });
    expect(r.status).toBe(200);
    expect(await countOf(h, 'eq_sword_20_l+3~s')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('경매: 옵션 키를 등록할 수 있다(DB 형식 검사 0057)', async () => {
    const h = await mk(app);
    const key = 'eq_sword_20_un+2~d';
    await seedItem(h, key, 1);
    const res = await listReq(app, h, { item_key: key, count: 1, buyout: 5_000, hours: 12 });
    expect(res.status).toBe(201);
    expect(res.body.data.listing.item_key).toBe(key);
    // 부위에 맞지 않는 옵션 글자는 모르는 아이템
    await seedItem(h, 'eq_sword_20_un+2~h', 1);
    const bad = await listReq(app, h, { item_key: 'eq_sword_20_un+2~h', count: 1, buyout: 5_000, hours: 12 });
    expect(bad.status).toBeGreaterThanOrEqual(400);
  });
});

describe('교환', () => {
  const base = (h: Hero) => `/characters/${h.id}/trades`;
  const send = (h: Hero, method: 'post' | 'put', path: string, body: Record<string, unknown> = {}) =>
    request(app)[method](`${base(h)}${path}`).set(auth(h.s)).send(body);
  async function online(h: Hero): Promise<void> {
    await getPool().query(
      `INSERT INTO online_sessions (account_id, character_id, family_id, map_id, map_since, map_changed_at, last_seen_at)
       VALUES ((SELECT account_id FROM characters WHERE id = $1), $1, $2, 'village', now(), now(), now())
       ON CONFLICT (account_id) DO UPDATE SET character_id = $1, ended_at = NULL, end_reason = NULL, last_seen_at = now()`,
      [h.dbId, randomUUID()],
    );
  }
  const nameOf = async (h: Hero): Promise<string> =>
    ((await getPool().query<{ name: string }>('SELECT name FROM characters WHERE id = $1', [h.dbId])).rows[0] as { name: string }).name;

  it('옵션 장비를 건네면 받는 쪽 가방에 같은 키로 들어간다', async () => {
    const a = await mk(app, 10_000);
    const b = await mk(app, 10_000);
    await online(a);
    await online(b);
    const key = 'eq_plate_20_un+6~g';
    await seedItem(a, key, 1);
    const c = await send(a, 'post', '', { request_id: randomUUID(), target_name: await nameOf(b) });
    expect(c.status).toBe(201);
    const id = c.body.data.trade.id as string;
    expect((await send(b, 'post', `/${id}/accept`)).status).toBe(200);
    const stack = (await getPool().query<{ uuid: string }>("SELECT uuid FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2", [a.dbId, key])).rows[0] as { uuid: string };
    const o = await send(a, 'put', `/${id}/offer`, { request_id: randomUUID(), version: 0, gold: 0, items: [{ item_uuid: stack.uuid, count: 1 }] });
    expect(o.status).toBe(200);
    const v = o.body.data.trade.version as number;
    expect((await send(a, 'post', `/${id}/lock`, { version: v })).status).toBe(200);
    expect((await send(b, 'post', `/${id}/lock`, { version: v })).status).toBe(200);
    expect((await send(a, 'post', `/${id}/confirm`, { request_id: randomUUID(), version: v })).status).toBe(200);
    const done = await send(b, 'post', `/${id}/confirm`, { request_id: randomUUID(), version: v });
    expect(done.body.data.trade.state).toBe('completed');
    expect(await countOf(a, key)).toBe(0);
    expect(await countOf(b, key)).toBe(1);
    await expectLedgerConsistent(a);
    await expectLedgerConsistent(b);
  });
});
