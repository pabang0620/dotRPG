// 장비 계승(POST /characters/{uuid}/inherit): 경로 제한, 유지·실패 분기(난수 주입), 누적 깎임, 비용, 시작 장비 거부, 멱등,
// 보호권(강화 보정) 이력 이전, 키 형식("id+N^k"), 강화·승급이 계승 횟수를 지키는지, 계승한 장비의 경매 등록
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { vendorUnitOf } from '../src/domains/auction/auctionPricing';
import { keyAt, parseItemKey } from '../src/utils/itemKey';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import {
  countOf,
  expectLedgerConsistent,
  fakeRng,
  goldOf,
  newHero,
  post,
  seedGold,
  seedItem,
  seedLevel,
  seedWorn,
  wornOf,
  type Hero,
} from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
afterAll(async () => {
  setRng(null);
  await shutdown();
});
afterEach(() => setRng(null));

/** 주사위를 고정한다(rng.int(0, 100)의 결과). 0 = 언제나 유지, 99 = 100%가 아니면 실패 */
const roll = (n: number) => setRng(fakeRng({ int: () => n }));
const inherit = (h: Hero, body: Record<string, unknown>, rid?: string) => post(app, h, '/inherit', body, rid);
const bag = (key: string) => ({ target: { bag_key: key } });

// enhance.json inheritTiers 골드(무기 0.25 x 새 레벨대 기본 골드 x 단위 차이, 무기 아닌 장비 80%)
const GOLD_10_TO_20_P12 = 9973; // Lv20 기본 880
const GOLD_20_TO_30_P12 = 28673; // Lv30 기본 2530
const GOLD_PLATE_20_TO_30_P11 = 6072; // 7590 x 0.8

async function hero(gold = 1_000_000): Promise<Hero> {
  const h = await newHero(app);
  const diff = gold - (await goldOf(h));
  if (diff !== 0) await seedGold(h, diff);
  return h;
}

async function pityOf(h: Hero, key: string): Promise<number | null> {
  const r = await getPool().query('SELECT pity FROM character_enhance_pity WHERE character_id = $1 AND item_key = $2', [h.dbId, key]);
  return r.rows[0]?.pity ?? null;
}

describe('아이템 키 형식', () => {
  it('계승 횟수는 "^k"로 붙고 그때는 +0도 적는다. 기존 키는 그대로', () => {
    expect(parseItemKey('eq_sword_20_r')).toEqual({ base: 'eq_sword_20_r', level: 0, inherit: 0 });
    expect(parseItemKey('eq_sword_20_r+12')).toEqual({ base: 'eq_sword_20_r', level: 12, inherit: 0 });
    expect(parseItemKey('eq_sword_20_r+0^1')).toEqual({ base: 'eq_sword_20_r', level: 0, inherit: 1 });
    expect(parseItemKey('eq_sword_30_r+12^2')).toEqual({ base: 'eq_sword_30_r', level: 12, inherit: 2 });
    for (const bad of ['eq_sword_20_r^1', 'eq_sword_20_r+0', 'eq_sword_20_r+1^0', 'eq_sword_20_r+12^10', 'eq_sword_20_r+05^1', 'eq_sword_20_r+3^1^2']) {
      expect(parseItemKey(bad)).toBeNull();
    }
    expect(keyAt('eq_sword_20_r', 0, 1)).toBe('eq_sword_20_r+0^1');
    expect(keyAt('eq_sword_20_r', 5, 0)).toBe('eq_sword_20_r+5');
    expect(keyAt('eq_sword_20_r', 0)).toBe('eq_sword_20_r');
    // '+' 앞은 언제나 기본 id(기존 split 처리와 맞는다)
    expect('eq_sword_30_r+12^2'.split('+')[0]).toBe('eq_sword_30_r');
  });
});

describe('POST /characters/:id/inherit', () => {
  it('유지: Lv10 +12 -> Lv20 +12^1, 골드가 빠지고 원장 사유는 inherit', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_10_r+12', 1);
    roll(79); // 유지 80%
    const res = await inherit(h, bag('eq_sword_10_r+12'));
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ old_key: 'eq_sword_10_r+12', new_key: 'eq_sword_20_r+12^1', kept: true, cost: { gold: GOLD_10_TO_20_P12 } });
    expect(await countOf(h, 'eq_sword_10_r+12')).toBe(0);
    expect(await countOf(h, 'eq_sword_20_r+12^1')).toBe(1);
    expect(await goldOf(h)).toBe(1_000_000 - GOLD_10_TO_20_P12);
    const g = await getPool().query("SELECT delta FROM gold_ledger WHERE character_id = $1 AND reason = 'inherit'", [h.dbId]);
    expect(g.rows.map((r) => Number(r.delta))).toEqual([-GOLD_10_TO_20_P12]);
    const it = await getPool().query("SELECT item_key, delta FROM item_ledger WHERE character_id = $1 AND reason = 'inherit' ORDER BY id", [h.dbId]);
    expect(it.rows.map((r) => [r.item_key, Number(r.delta)])).toEqual([['eq_sword_10_r+12', -1], ['eq_sword_20_r+12^1', 1]]);
    await expectLedgerConsistent(h);
  });

  it('실패: 유지 확률 밖이면 1단계 내려간다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_10_r+12', 1);
    roll(80);
    const res = await inherit(h, bag('eq_sword_10_r+12'));
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ new_key: 'eq_sword_20_r+11^1', kept: false, cost: { gold: GOLD_10_TO_20_P12 } });
    await expectLedgerConsistent(h);
  });

  it('누적 깎임: 이미 계승한 장비는 유지해도 2, 실패하면 3 내려간다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_20_r+12^1', 2);
    roll(0);
    const a = await inherit(h, bag('eq_sword_20_r+12^1'));
    expect(a.body.data).toMatchObject({ new_key: 'eq_sword_30_r+10^2', kept: true, cost: { gold: GOLD_20_TO_30_P12 } });
    roll(99);
    const b = await inherit(h, bag('eq_sword_20_r+12^1'));
    expect(b.body.data).toMatchObject({ new_key: 'eq_sword_30_r+9^2', kept: false });
    await expectLedgerConsistent(h);
  });

  it('+10 이하는 공짜·확정 유지, +0도 계승 횟수가 붙는다. 0 아래로는 내려가지 않는다', async () => {
    const h = await hero(1000);
    await seedItem(h, 'eq_sword_10_r+5', 1);
    await seedItem(h, 'eq_sword_10_r', 1);
    await seedItem(h, 'eq_sword_20_r+1^1', 1);
    roll(99);
    expect((await inherit(h, bag('eq_sword_10_r+5'))).body.data).toMatchObject({ new_key: 'eq_sword_20_r+5^1', kept: true, cost: { gold: 0 } });
    expect((await inherit(h, bag('eq_sword_10_r'))).body.data).toMatchObject({ new_key: 'eq_sword_20_r+0^1', kept: true });
    expect((await inherit(h, bag('eq_sword_20_r+1^1'))).body.data).toMatchObject({ new_key: 'eq_sword_30_r+0^2' });
    expect(await goldOf(h)).toBe(1000);
    await expectLedgerConsistent(h);
  });

  it('무기가 아닌 장비는 골드 80%', async () => {
    const h = await hero();
    await seedItem(h, 'eq_plate_20_r+11', 1);
    roll(0);
    const res = await inherit(h, bag('eq_plate_20_r+11'));
    expect(res.body.data).toMatchObject({ new_key: 'eq_plate_30_r+11^1', cost: { gold: GOLD_PLATE_20_TO_30_P11 } });
  });

  it('경로 제한: Lv1·15·40, 다음 레벨대에 같은 등급이 없는 전설, 필드 보스 장신구, 시작 장비는 422', async () => {
    const h = await hero();
    const keys = ['eq_sword_1_r+3', 'eq_sword_15_r+3', 'eq_sword_40_r+3', 'eq_sword_20_l+3', 'eq_ring_root+3', 'eq_sword_wood+5', 'mat_bone'];
    for (const k of keys) await seedItem(h, k, 1);
    for (const k of keys) {
      const res = await inherit(h, bag(k));
      expect([res.status, res.body.errors.code, k]).toEqual([422, 'INHERIT_INVALID_TARGET', k]);
    }
    expect((await inherit(h, bag('eq_sword_10_r+3'))).body.errors.code).toBe('INHERIT_INVALID_TARGET'); // 가방에 없음
    expect((await inherit(h, { target: { worn_slot: 3 } })).body.errors.code).toBe('INHERIT_INVALID_TARGET'); // 빈 칸
    expect(await goldOf(h)).toBe(1_000_000);
  });

  it('계승 횟수 상한(4)에 닿은 장비는 422 INHERIT_MAX_COUNT', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_30_r+5^4', 1);
    expect((await inherit(h, bag('eq_sword_30_r+5^4'))).body.errors.code).toBe('INHERIT_MAX_COUNT');
  });

  it('골드 부족: 422 INHERIT_NOT_ENOUGH, 아무것도 바뀌지 않는다', async () => {
    const h = await hero(GOLD_10_TO_20_P12 - 1);
    await seedItem(h, 'eq_sword_10_r+12', 1);
    const res = await inherit(h, bag('eq_sword_10_r+12'));
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'INHERIT_NOT_ENOUGH', need: { gold: GOLD_10_TO_20_P12 } });
    expect(await countOf(h, 'eq_sword_10_r+12')).toBe(1);
    expect(await goldOf(h)).toBe(GOLD_10_TO_20_P12 - 1);
  });

  it('착용 중: 새 장비 레벨이 안 되면 422 INHERIT_LEVEL, 되면 같은 칸에서 바뀐다', async () => {
    const h = await hero();
    await seedWorn(h, 0, 'eq_sword_10_r+3');
    await seedLevel(h, 19);
    expect((await inherit(h, { target: { worn_slot: 0 } })).body.errors.code).toBe('INHERIT_LEVEL');
    await seedLevel(h, 20);
    roll(0);
    const res = await inherit(h, { target: { worn_slot: 0 } });
    expect(res.status).toBe(200);
    expect(await wornOf(h, 0)).toBe('eq_sword_20_r+3^1');
    await expectLedgerConsistent(h);
  });

  it('같은 request_id는 한 번만 처리하고 같은 응답을 돌려준다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_10_r+12', 2);
    const rid = randomUUID();
    roll(0);
    const a = await inherit(h, bag('eq_sword_10_r+12'), rid);
    roll(99);
    const b = await inherit(h, bag('eq_sword_10_r+12'), rid);
    expect(a.status).toBe(200);
    expect(b.body).toEqual(a.body);
    expect(await countOf(h, 'eq_sword_10_r+12')).toBe(1);
    expect(await countOf(h, 'eq_sword_20_r+12^1')).toBe(1);
    expect(await goldOf(h)).toBe(1_000_000 - GOLD_10_TO_20_P12);
  });

  it('보호권(강화 보정) 이력은 결과 장비로 옮긴다. 같은 키 사본이 남으면 옛 키 이력도 남긴다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_10_r+11', 2);
    await getPool().query("INSERT INTO character_enhance_pity (character_id, item_key, pity) VALUES ($1, 'eq_sword_10_r+11', 30)", [h.dbId]);
    roll(0);
    const a = await inherit(h, bag('eq_sword_10_r+11'));
    expect(a.body.data).toMatchObject({ new_key: 'eq_sword_20_r+11^1', pity: 30 });
    expect(await pityOf(h, 'eq_sword_20_r+11^1')).toBe(30);
    expect(await pityOf(h, 'eq_sword_10_r+11')).toBe(30); // 한 자루 남음
    roll(0);
    await inherit(h, bag('eq_sword_10_r+11'));
    expect(await pityOf(h, 'eq_sword_10_r+11')).toBeNull(); // 마지막 사본이 넘어감
    expect(await pityOf(h, 'eq_sword_20_r+11^1')).toBe(30);
  });

  it('강화·승급은 계승 횟수를 지킨다', async () => {
    const h = await hero();
    await seedItem(h, 'eq_sword_20_c+3^1', 1);
    await seedItem(h, 'mat_bone', 100);
    await seedItem(h, 'mat_ore', 100);
    await seedItem(h, 'mat_core', 5);
    roll(0);
    const e = await post(app, h, '/enhance', bag('eq_sword_20_c+3^1'));
    expect(e.body.data).toMatchObject({ outcome: 'success', new_key: 'eq_sword_20_c+4^1' });
    const p = await post(app, h, '/promote', bag('eq_sword_20_c+4^1'));
    expect(p.body.data).toMatchObject({ new_key: 'eq_sword_20_u+4^1' });
    await expectLedgerConsistent(h);
  });

  it('계승한 장비도 경매에 올릴 수 있다(키 형식 검사)', async () => {
    const h = await hero();
    await seedLevel(h, 30);
    const key = 'eq_sword_20_r+3^1';
    await seedItem(h, key, 1);
    const unit = vendorUnitOf(key) ?? 100;
    const res = await post(app, h, '/auction/listings', { item_key: key, count: 1, buyout: unit * 5, hours: 12 });
    expect(res.status).toBe(201);
    const r = await getPool().query('SELECT item_key, item_base, enhance FROM auction_listings WHERE uuid = $1', [res.body.data.listing.id]);
    expect(r.rows[0]).toMatchObject({ item_key: key, item_base: 'eq_sword_20_r', enhance: 3 });
  });
});
