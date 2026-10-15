// 2026-10-14 요일 던전 유니크 보너스: 판당 22%, 유니크 없이 7판 뒤 8판째 보장, 그 단계에 없는 부위를 먼저 준다
import { getGameData } from '../src/gamedata/loader';
import type { DungeonDef } from '../src/gamedata/economyData';
import { diffOf, rollUniqueBonus, uniqueBonusApplies, uniqueCategoriesOwned, UNIQUE_BONUS, type DiffNumbers } from '../src/domains/dungeons/dungeonRules';
import type { Rng } from '../src/utils/rng';
import { tierOfLevel } from '../src/utils/gearTier';
import { applyUniqueBonus } from '../src/domains/dungeons/uniquePity';
import { LUCK_CARDS } from '../src/domains/dungeons/dungeonRules';
import type { Queryable } from '../src/db/pool';
import { buildApp, shutdown } from './helpers';

buildApp();
afterAll(shutdown);

const gd = () => getGameData();

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

const weekday = (): DungeonDef => [...gd().economy.dungeons.byId.values()].find((d) => uniqueBonusApplies(d)) as DungeonDef;
const diffAt = (d: DungeonDef, i: number) => diffOf(gd().economy, d, i) as DiffNumbers;
const categoryOf = (key: string) => gd().economy.shop.equipmentList.find((e) => e.id === key.split('+')[0])?.category;

describe('요일 던전 유니크 보너스', () => {
  it('요일 던전만 대상이고 일반 던전·레이드는 아니다', () => {
    const all = [...gd().economy.dungeons.byId.values()];
    expect(all.some((d) => uniqueBonusApplies(d))).toBe(true);
    for (const d of all) if (d.isRaid || d.always) expect(uniqueBonusApplies(d)).toBe(false);
  });

  it('보장 없이 굴리면 판당 약 22%이고, 나오면 유니크다', () => {
    const eco = gd().economy;
    const d = weekday();
    const diff = diffAt(d, 1);
    const rng = seeded(7);
    const N = 50_000;
    let hit = 0;
    for (let i = 0; i < N; i++) {
      const r = rollUniqueBonus(eco, diff, 'warrior', rng, 0, new Set());
      if (r.key) {
        hit++;
        const e = eco.shop.equipmentList.find((x) => x.id === (r.key as string).split('+')[0]);
        expect(e?.rarity).toBe('Unique');
      }
    }
    expect(hit / N).toBeGreaterThan(UNIQUE_BONUS.chancePct / 100 - 0.01);
    expect(hit / N).toBeLessThan(UNIQUE_BONUS.chancePct / 100 + 0.01);
  });

  it('유니크 없이 7판이 지나면 8판째는 반드시 나오고, 나오면 0으로 돌아간다', () => {
    const eco = gd().economy;
    const diff = diffAt(weekday(), 1);
    const never: Rng = { int: (min, max) => (max <= min ? min : max - 1), unit: () => 0.999 }; // 확률 굴림은 늘 실패
    let misses = 0;
    for (let i = 0; i < UNIQUE_BONUS.pityRuns - 1; i++) {
      const r = rollUniqueBonus(eco, diff, 'mage', never, misses, new Set());
      expect(r.key).toBeNull();
      misses = r.misses;
    }
    const last = rollUniqueBonus(eco, diff, 'mage', never, misses, new Set());
    expect(last.key).not.toBeNull();
    expect(last.misses).toBe(0);
  });

  it('그 단계에 없는 부위의 유니크를 먼저 준다', () => {
    const eco = gd().economy;
    const diff = diffAt(weekday(), 1);
    const tier = tierOfLevel(diff.recommendedLevel);
    const pool = eco.shop.equipmentList.filter((e) => e.levelTier === tier && e.rarity === 'Unique' && !e.bossOnly && (e.classOnly === null || e.classOnly === 'warrior'));
    const cats = [...new Set(pool.map((e) => e.category))];
    // 하나만 빼고 다 가졌으면 늘 그 부위가 나온다
    const owned = new Set(cats.slice(1));
    const rng = seeded(3);
    for (let i = 0; i < 200; i++) {
      const r = rollUniqueBonus(eco, diff, 'warrior', rng, UNIQUE_BONUS.pityRuns, owned);
      expect(categoryOf(r.key as string)).toBe(cats[0]);
    }
    // 가진 키(강화 포함)에서 부위를 읽는다
    const some = pool[0] as (typeof pool)[number];
    expect(uniqueCategoriesOwned(eco, tier, [`${some.id}+7`]).has(some.category)).toBe(true);
  });
  it('보너스 유니크는 이미 대박(유니크·레전더리)인 운 카드를 덮어쓰지 않는다', async () => {
    const eco = gd().economy;
    const d = weekday();
    const diff = diffAt(d, 1);
    // 보장 직전(misses 7)이라 반드시 유니크가 나온다. DB는 필요한 질의만 흉내 낸다
    const db = {
      query: async (sql: string) => (sql.includes('SELECT misses') ? { rows: [{ misses: UNIQUE_BONUS.pityRuns - 1 }] } : { rows: [] }),
    } as unknown as Queryable;
    const jackpot = eco.shop.equipmentList.find((e) => e.rarity === 'Legendary' && !e.bossOnly) as { id: string };
    const sure = [{ item_key: 'gold', count: 100 }, { item_key: 'mat_bone', count: 5 }];
    const luck = Array.from({ length: LUCK_CARDS }, (_, i) => (i === LUCK_CARDS - 1 ? { item_key: jackpot.id, count: 1 } : { item_key: 'mat_ore', count: 2 }));
    const res = await applyUniqueBonus(db, 1, eco, d, diff, 'warrior', [...sure, ...luck], seeded(3));
    expect(res.unique).not.toBeNull();
    expect(res.cards.some((c) => c.item_key === jackpot.id)).toBe(true);
    expect(res.cards.some((c) => c.item_key === res.unique)).toBe(true);
    // 모두 대박이면 한 장을 더한다
    const allJackpot = Array.from({ length: LUCK_CARDS }, () => ({ item_key: jackpot.id, count: 1 }));
    const res2 = await applyUniqueBonus(db, 1, eco, d, diff, 'warrior', [...sure, ...allJackpot], seeded(4));
    expect(res2.cards.length).toBe(sure.length + LUCK_CARDS + 1);
    expect(res2.cards.filter((c) => c.item_key === jackpot.id).length).toBe(LUCK_CARDS);
  });
});

