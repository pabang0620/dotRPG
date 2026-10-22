// 챕터 3 레이드(Docs/ch3/RAIDS.md, IMPLEMENTATION_ORDER.md 4단계): 지바크(중간, Lv45)·카마츠(최종, Lv50)
// 잠금(CHAPTER3_OPEN 0이면 목록·입장·상점이 닫힘), 해금·레벨·요일, 혼자(AI) 클리어 보상과 열쇠, 상점 상자, 보상 규칙
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import type { DungeonDef } from '../src/gamedata/economyData';
import { rollRaidGear, rollRaidGold } from '../src/domains/dungeons/dungeonRules';
import { setClockOverride } from '../src/utils/clock';
import { parseItemKey } from '../src/utils/itemKey';
import { setRng, type Rng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, fakeRng, seedClaims, seedItem, seedLevel } from './economyHelpers';
import { get, newHero, post, roomsOf, stats, type Hero } from './partyHelpers';

let app = buildApp();
const WED = '2026-10-07T03:00:00Z'; // 수요일 12:00 KST: 중간 레이드 열림
const SUN = '2026-10-11T03:00:00Z'; // 일요일: 최종 레이드 열림
let fixed = new Date(WED);
const at = (iso: string) => {
  fixed = new Date(iso);
};
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeEach(async () => {
  await resetDb();
  app = buildApp({ CHAPTER3_OPEN: 'true' });
  at(WED);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const eco = () => getGameData().economy;
const raid = (id: string): DungeonDef => eco().dungeons.byId.get(id) as DungeonDef;
const raidHero = async (level: number, claims: string[]): Promise<Hero> => {
  const h = await newHero(app);
  await seedLevel(h, level);
  await seedClaims(h, claims);
  return h;
};
const enter = (h: Hero, id: string, over: Record<string, unknown> = {}) => post(app, h, '/dungeon-runs', { dungeon_id: id, difficulty: 0, ...over });

/** 재현 가능한 난수(mulberry32) */
function seeded(seed: number): Rng {
  let a = seed >>> 0;
  const next = (): number => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return { int: (min, max) => (max <= min ? min : min + Math.floor(next() * (max - min))), unit: () => next() };
}

describe('데이터', () => {
  it('두 레이드: chapter 3, 권장 레벨·몬스터 레벨·해금 퀘스트·열쇠 규칙·방이 문서대로', () => {
    const x = raid('raid_xibak');
    const k = raid('raid_kamatz');
    expect(x).toMatchObject({ isRaid: true, raidTier: 'Mid', chapter: 3, unlockQuest: 'c3_xibak', keyMin: 20, keyMax: 50, keyCost: 0, clearXp: 1200, bossRoom: 2 });
    expect(k).toMatchObject({ isRaid: true, raidTier: 'Final', chapter: 3, unlockQuest: 'c3_kamatz', keyCost: 60, clearXp: 2000, bossRoom: 1 });
    expect(x.raidNumbers).toMatchObject({ recommendedLevel: 45, recommendedPower: 13000, hpMul: 5.2, damageMul: 1.8, monsterLevel: 40, minGearRarity: 'Epic' });
    expect(k.raidNumbers).toMatchObject({ recommendedLevel: 50, recommendedPower: 15500, hpMul: 5, damageMul: 1.9, monsterLevel: 45, minGearRarity: 'Epic' });
    expect(roomsOf('raid_xibak').map((r) => r.map)).toEqual(['dgn_xibak_1', 'dgn_xibak_2', 'dgn_xibak_boss']);
    expect(roomsOf('raid_xibak')[2]?.kills).toEqual([['boss_xibak', 1]]);
    expect(roomsOf('raid_kamatz')[0]?.kills).toEqual([['grey_knight', 5], ['grey_priest', 2], ['grey_ranger', 2]]);
    expect(roomsOf('raid_kamatz')[1]?.kills).toEqual([['boss_kamatz', 1]]);
    expect(x.openDays).toEqual(['Wednesday', 'Saturday', 'Sunday']);
    expect(k.openDays).toEqual(['Sunday']);
  });

  it('보상: 확정 골드 범위, 카드 표는 그라흐와 같은 구조, 장비 카드는 Lv45(레전더리 없음 -> 유니크)와 Lv50 단계', () => {
    for (const [id, lo, hi, mat] of [['raid_xibak', 10000, 15000, 'mat_raid_xibak'], ['raid_kamatz', 16000, 24000, 'mat_raid_kamatz']] as const) {
      const d = raid(id);
      const rr = d.raidReward as NonNullable<DungeonDef['raidReward']>;
      expect(rr.materialItem).toBe(mat);
      expect(rr.cards.map((c) => [c.itemId, c.min, c.weight])).toEqual([
        ['gear', 1, 12], [mat, 1, 12], [mat, 2, 30], [mat, 4, 28], [mat, 8, 9], ['mat_essence', 16, 6], ['ticket_protect', 1, 3],
      ]);
      const rng = seeded(11);
      for (let i = 0; i < 2000; i++) {
        const g = rollRaidGold(d, rng);
        expect(g).toBeGreaterThanOrEqual(lo);
        expect(g).toBeLessThanOrEqual(hi);
      }
    }
    const tierOf = (key: string) => eco().shop.equipmentList.find((e) => e.id === parseItemKey(key)?.base);
    const rarities = { raid_xibak: new Set<string>(), raid_kamatz: new Set<string>() } as Record<string, Set<string>>;
    for (const id of ['raid_xibak', 'raid_kamatz']) {
      for (let seed = 1; seed <= 3000; seed++) {
        const key = rollRaidGear(eco(), raid(id), 'warrior', seeded(seed));
        const e = tierOf(key);
        expect(e?.levelTier).toBe(id === 'raid_xibak' ? 8 : 9);
        rarities[id]?.add(e?.rarity as string);
      }
    }
    expect(rarities.raid_xibak?.has('Legendary')).toBe(false); // Lv45 레전더리는 없다: 한 등급 내려 유니크
    expect([...(rarities.raid_kamatz ?? [])].sort()).toEqual(['Epic', 'Legendary', 'Unique']);
  });
});

describe('잠금(CHAPTER3_OPEN 0)', () => {
  it('목록에 없고, 입장·상점 구매는 422 CHAPTER_LOCKED', async () => {
    app = buildApp();
    const h = await raidHero(40, ['c3_xibak']);
    const raids = (await get(app, h, '/raids')).body.data.raids as { id: string }[];
    expect(raids.map((r) => r.id).sort()).toEqual(['raid_grah', 'raid_skeleton_king']);
    const res = await enter(h, 'raid_xibak');
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'CHAPTER_LOCKED', kind: 'dungeon', id: 'raid_xibak' });
    const shop = (await get(app, h, '/raid-shop')).body.data.products as { id: string }[];
    expect(shop.some((p) => p.id.startsWith('xibak_') || p.id.startsWith('kamatz_'))).toBe(false);
    await seedItem(h, 'mat_raid_xibak', 200, 'bag', 'character');
    const buy = await post(app, h, '/raid-shop/buy', { product_id: 'xibak_epic_weapon' });
    expect(buy.body.errors.code).toBe('CHAPTER_LOCKED');
    expect(await countOf(h, 'mat_raid_xibak')).toBe(200);
  });
});

describe('열린 서버', () => {
  it('GET /raids: 지바크(중간, 열쇠 조각 20~50)와 카마츠(최종, 열쇠 60)가 해금 퀘스트로 열린다', async () => {
    const h = await raidHero(45, []);
    const list = async () => (await get(app, h, '/raids')).body.data.raids as { id: string; unlocked: boolean; tier: string; open_today: boolean; key: Record<string, unknown> }[];
    const before = await list();
    expect(before.map((r) => r.id).sort()).toEqual(['raid_grah', 'raid_kamatz', 'raid_skeleton_king', 'raid_xibak']);
    expect(before.find((r) => r.id === 'raid_xibak')).toMatchObject({ unlocked: false, tier: 'mid', open_today: true, key: { drop_min: 20, drop_max: 50 } });
    expect(before.find((r) => r.id === 'raid_kamatz')).toMatchObject({ unlocked: false, tier: 'final', open_today: false, key: { cost: 60 } });
    await seedClaims(h, ['c3_xibak', 'c3_kamatz']);
    const after = await list();
    expect(after.filter((r) => r.unlocked).map((r) => r.id).sort()).toEqual(['raid_kamatz', 'raid_xibak']);
  });

  it('입장: 해금·레벨(45 / 50)·요일을 본다. 카마츠는 열쇠가 모자라면 연습판', async () => {
    expect((await enter(await raidHero(45, []), 'raid_xibak')).body.errors.code).toBe('RAID_LOCKED');
    expect((await enter(await raidHero(44, ['c3_xibak']), 'raid_xibak')).body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: 45 });
    const ok = await enter(await raidHero(45, ['c3_xibak']), 'raid_xibak', { ai_count: 3 });
    expect(ok.status).toBe(201);
    expect(ok.body.data.run).toMatchObject({ reward_locked: false, ai_count: 3 });
    const fin = await raidHero(50, ['c3_kamatz']);
    expect((await enter(fin, 'raid_kamatz')).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    at(SUN);
    expect((await enter(await raidHero(49, ['c3_kamatz']), 'raid_kamatz')).body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: 50 });
    const practice = await enter(fin, 'raid_kamatz');
    expect(practice.status).toBe(201);
    expect(practice.body.data.run).toMatchObject({ reward_locked: true, lock_reason: 'KEYS_MISSING' });
  });

  it('지바크 혼자(AI 3명) 클리어: 일일 청구 1, 열쇠 조각 20~50, 카드 4장, 원장 일치', async () => {
    const h = await raidHero(45, ['c3_xibak']);
    const run = await enter(h, 'raid_xibak', { ai_count: 3 });
    const runId = run.body.data.run.id as string;
    const t0 = fixed.getTime();
    for (const [room, def] of roomsOf('raid_xibak').entries()) {
      for (const [monster, n] of def.kills) {
        for (let i = 0; i < n; i++) {
          advance(20);
          const k = await post(app, h, '/kills', { map_id: def.map, monster_id: monster, run_id: runId, room_index: room });
          expect(k.status).toBe(200);
        }
      }
    }
    at(new Date(t0 + 600_000).toISOString());
    const res = await post(app, h, `/dungeon-runs/${runId}/result`, { outcome: 'cleared', stats: stats({ elapsed_ms: 600_000, max_combo: 300 }) });
    expect(res.status).toBe(200);
    expect(res.body.data.result).toBe('cleared');
    expect(res.body.data.card_count).toBe(4);
    expect(res.body.data.raid).toMatchObject({ reward_locked: false });
    const keys = await countOf(h, 'key_seal');
    expect(keys).toBeGreaterThanOrEqual(20);
    expect(keys).toBeLessThanOrEqual(50);
    const claims = await getPool().query('SELECT period_kind FROM raid_claims WHERE character_id = $1', [h.dbId]);
    expect(claims.rows).toEqual([{ period_kind: 'daily' }]);
    await expectLedgerConsistent(h);
  });

  it('레이드 상점: 상자 4개 추가(지바크 레전더리는 Lv50), 구매하면 재료가 빠지고 내 직업 무기', async () => {
    const h = await newHero(app, 'warrior');
    const products = (await get(app, h, '/raid-shop')).body.data.products as { id: string; tier_level: number; material: { price: number; item_key: string }; outcomes: { item_key: string }[] }[];
    const byId = new Map(products.map((p) => [p.id, p]));
    expect([...byId.keys()].sort()).toEqual([
      'grah_epic_weapon', 'grah_legend_weapon', 'kamatz_epic_weapon', 'kamatz_legend_weapon',
      'king_epic_weapon', 'king_legend_weapon', 'xibak_epic_weapon', 'xibak_legend_weapon',
    ]);
    expect(byId.get('xibak_epic_weapon')).toMatchObject({ tier_level: 45, material: { item_key: 'mat_raid_xibak', price: 45 } });
    expect(byId.get('xibak_epic_weapon')?.outcomes.map((o) => o.item_key)).toEqual(['eq_sword_45_e', 'eq_sword_45_un']);
    expect(byId.get('xibak_legend_weapon')).toMatchObject({ tier_level: 50, material: { price: 120 }, outcomes: [{ item_key: 'eq_sword_50_l' }] });
    expect(byId.get('kamatz_epic_weapon')).toMatchObject({ tier_level: 50, material: { item_key: 'mat_raid_kamatz', price: 20 } });
    expect(byId.get('kamatz_legend_weapon')).toMatchObject({ tier_level: 50, material: { price: 60 }, outcomes: [{ item_key: 'eq_sword_50_l' }] });
    await seedItem(h, 'mat_raid_kamatz', 25, 'bag', 'character');
    const r = await post(app, h, '/raid-shop/buy', { product_id: 'kamatz_epic_weapon' });
    expect(r.status).toBe(200);
    expect(['eq_sword_50_e', 'eq_sword_50_un']).toContain(r.body.data.item_key);
    expect(await countOf(h, 'mat_raid_kamatz')).toBe(5);
    await expectLedgerConsistent(h);
  });
});
