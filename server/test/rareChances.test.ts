// 2026-10-14 유니크·레전더리 확률 표시: rareChances(계산식)가 실제 굴림 함수(rollOneCard, rollRaidCards)의 표본과 맞는지 확인한다
import { getGameData } from '../src/gamedata/loader';
import type { DungeonDef } from '../src/gamedata/economyData';
import { diffOf, rareChances, rollOneCard, rollRaidCards, rollClearRewards, LUCK_CARDS, type DiffNumbers } from '../src/domains/dungeons/dungeonRules';
import type { Rng } from '../src/utils/rng';
import { buildApp, shutdown } from './helpers';

buildApp();
afterAll(shutdown);

const gd = () => getGameData();

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
  return {
    int: (min, max) => (max <= min ? min : min + Math.floor(next() * (max - min))),
    unit: () => next(),
  };
}

const rarityOf = (key: string): string | null => {
  const base = key.split('+')[0] as string;
  return gd().economy.shop.equipmentList.find((e) => e.id === base)?.rarity ?? null;
};

describe('rareChances', () => {
  it('요일 던전(영웅, 대박 포함): 카드 한 장의 유니크 확률이 rollOneCard 20만 장 표본과 맞다', () => {
    const eco = gd().economy;
    for (const id of ['armory', 'gold_vein', 'normal_canyon']) {
      const d = eco.dungeons.byId.get(id) as DungeonDef;
      for (const tier of [0, 3]) {
        const diff = diffOf(eco, d, tier) as DiffNumbers;
        const want = rareChances(eco, d, diff, 'warrior');
        const rng = seeded(7 + tier);
        const N = 200_000;
        let unique = 0;
        let legend = 0;
        for (let i = 0; i < N; i++) {
          const r = rarityOf(rollOneCard(eco, d, diff, 'warrior', rng).item_key);
          if (r === 'Unique') unique++;
          if (r === 'Legendary') legend++;
        }
        const sd = Math.sqrt((want.unique.card * (1 - want.unique.card)) / N);
        expect(Math.abs(unique / N - want.unique.card)).toBeLessThanOrEqual(5 * sd + 1e-4);
        expect(legend).toBe(0);
        expect(want.cards).toBe(LUCK_CARDS);
        expect(want.unique.clear).toBeCloseTo(1 - Math.pow(1 - want.unique.card, LUCK_CARDS), 10);
      }
    }
  });

  it('클리어 보상: 확정 재료·골드 2장 뒤에 운 카드가 LUCK_CARDS장', () => {
    const eco = gd().economy;
    const d = eco.dungeons.byId.get('smelter') as DungeonDef;
    const cards = rollClearRewards(eco, d, diffOf(eco, d, 0) as DiffNumbers, 'mage', seeded(3)) ?? [];
    expect(cards).toHaveLength(2 + LUCK_CARDS);
    expect(cards[0]?.item_key).toBe(d.sure?.material);
    expect(cards[1]?.item_key).toBe('gold');
  });

  it('레이드: 유니크·레전더리 카드 확률이 rollRaidCards 표본과 맞다', () => {
    const eco = gd().economy;
    const d = eco.dungeons.byId.get('raid_grah') as DungeonDef;
    const diff = diffOf(eco, d, 0) as DiffNumbers;
    const want = rareChances(eco, d, diff, 'mage');
    const rng = seeded(11);
    const N = 50_000;
    let unique = 0;
    let legend = 0;
    for (let i = 0; i < N; i++) {
      for (const c of rollRaidCards(eco, d, 'mage', rng)) {
        const r = rarityOf(c.item_key);
        if (r === 'Unique') unique++;
        if (r === 'Legendary') legend++;
      }
    }
    const n = N * want.cards;
    for (const [got, p] of [[unique, want.unique.card], [legend, want.legendary.card]] as const) {
      const sd = Math.sqrt((p * (1 - p)) / n);
      expect(Math.abs(got / n - p)).toBeLessThanOrEqual(5 * sd + 1e-5);
    }
  });
});
