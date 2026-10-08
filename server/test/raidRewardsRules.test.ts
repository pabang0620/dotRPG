// 13단계 레이드 보상: 순수 규칙과 데이터 정합(시험 1~7). DB가 필요한 시험은 raidRewards.test.ts
import fs from 'node:fs';
import path from 'node:path';
import { getGameData } from '../src/gamedata/loader';
import type { DungeonDef } from '../src/gamedata/economyData';
import { rollRaidCards, rollRaidGold, rollRaidGear } from '../src/domains/dungeons/dungeonRules';
import { parseItemKey } from '../src/utils/itemKey';
import type { Rng } from '../src/utils/rng';
import { buildApp, DATA_DIR, shutdown } from './helpers';

buildApp();
afterAll(shutdown);

const eco = () => getGameData().economy;
const raids = (): DungeonDef[] => [...eco().dungeons.byId.values()].filter((d) => d.isRaid);
const raid = (id: string): DungeonDef => eco().dungeons.byId.get(id) as DungeonDef;

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

const RARITY_RANK = ['Common', 'Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary'];
const equipOf = (key: string) => {
  const parsed = parseItemKey(key);
  return parsed ? eco().shop.equipmentList.find((e) => e.id === parsed.base) : undefined;
};

describe('1. rollRaidCards', () => {
  it('카드 4장, 모두 표에 있는 항목, 골드 항목 없음, 수량은 min..max 그대로(난이도 배율 없음)', () => {
    for (const d of raids()) {
      const rr = d.raidReward as NonNullable<DungeonDef['raidReward']>;
      expect(d.rewards).toEqual([]);
      expect(rr.cards.some((c) => c.itemId === 'gold')).toBe(false);
      for (const cls of ['warrior', 'mage']) {
        for (let seed = 1; seed <= 300; seed++) {
          const cards = rollRaidCards(eco(), d, cls, seeded(seed));
          expect(cards).toHaveLength(eco().dungeons.cards.count);
          expect(cards).toHaveLength(4);
          for (const c of cards) {
            expect(c.item_key).not.toBe('gold');
            // 같은 아이템이 수량별로 여러 줄이라, 수량이 들어맞는 줄이 하나라도 있어야 한다
            if (rr.cards.some((x) => x.itemId === c.item_key)) {
              expect(rr.cards.some((x) => x.itemId === c.item_key && x.min <= c.count && c.count <= x.max)).toBe(true);
            } else {
              expect(equipOf(c.item_key)).toBeDefined(); // gear 칸에서 나온 장비
              expect(c.count).toBe(1);
            }
          }
        }
      }
    }
  });
});

describe('2. 장비 카드', () => {
  it('해골왕은 _20_, 그라흐는 _40_ 단계의 +0 에픽 이상 장비이고 직업이 쓸 수 있다(전사, 마법사)', () => {
    for (const [id, tierText, tier] of [['raid_skeleton_king', '_20_', 3], ['raid_grah', '_40_', 7]] as const) {
      for (const cls of ['warrior', 'mage']) {
        const seen = new Set<string>();
        for (let seed = 1; seed <= 400; seed++) {
          const key = rollRaidGear(eco(), raid(id), cls, seeded(seed));
          seen.add(key);
          const e = equipOf(key) as NonNullable<ReturnType<typeof equipOf>>;
          expect(key).toContain(tierText);
          expect(parseItemKey(key)?.level).toBe(0);
          expect(e.levelTier).toBe(tier);
          expect(e.bossOnly).toBe(false);
          expect(RARITY_RANK.indexOf(e.rarity)).toBeGreaterThanOrEqual(RARITY_RANK.indexOf('Epic'));
          expect(e.classOnly === null || e.classOnly === cls).toBe(true);
        }
        expect(seen.size).toBeGreaterThan(5); // 부위 5종이 고르게 나온다
      }
    }
  });
});

describe('3. 10만 장 표본', () => {
  it.each(['raid_skeleton_king', 'raid_grah'])('%s: 카드 종류 비율은 표 가중치 +-0.8%p, 장비 등급 비율은 +-1%p', (id) => {
    const d = raid(id);
    const rr = d.raidReward as NonNullable<DungeonDef['raidReward']>;
    const total = rr.cards.reduce((a, c) => a + c.weight, 0);
    const rng = seeded(20261008);
    const byKind = new Map<string, number>();
    const rarity = { Epic: 0, Unique: 0, Legendary: 0 } as Record<string, number>;
    let gear = 0;
    let n = 0;
    for (let i = 0; i < 25_000; i++) {
      for (const c of rollRaidCards(eco(), d, 'warrior', rng)) {
        n++;
        const entry = rr.cards.find((x) => x.itemId === c.item_key && x.min <= c.count && c.count <= x.max);
        if (entry) byKind.set(`${entry.itemId}:${entry.min}`, (byKind.get(`${entry.itemId}:${entry.min}`) ?? 0) + 1);
        else {
          gear++;
          rarity[(equipOf(c.item_key) as { rarity: string }).rarity] = (rarity[(equipOf(c.item_key) as { rarity: string }).rarity] ?? 0) + 1;
        }
      }
    }
    expect(n).toBe(100_000);
    for (const c of rr.cards) {
      const got = c.itemId === 'gear' ? gear : (byKind.get(`${c.itemId}:${c.min}`) ?? 0);
      expect(Math.abs((got / n) * 100 - (c.weight * 100) / total)).toBeLessThan(0.8);
    }
    const rw = rr.gearRarityWeights as Record<string, number>;
    const rwTotal = Object.values(rw).reduce((a, b) => a + b, 0);
    for (const r of ['Epic', 'Unique']) expect(Math.abs(((rarity[r] ?? 0) / gear) * 100 - ((rw[r] ?? 0) * 100) / rwTotal)).toBeLessThan(1);
    // 레전더리는 표본이 작아 출현 여부와 상한만 본다
    expect(rarity.Legendary).toBeGreaterThan(0);
    expect(((rarity.Legendary ?? 0) / gear) * 100).toBeLessThan(3);
  });
});

describe('4. rollRaidGold', () => {
  it.each([['raid_skeleton_king', 3000, 4500], ['raid_grah', 12000, 18000]] as const)('%s: 범위 안, 100의 배수, 양 끝 값이 모두 나온다', (id, lo, hi) => {
    const rng = seeded(7);
    const seen = new Set<number>();
    for (let i = 0; i < 10_000; i++) {
      const g = rollRaidGold(raid(id), rng);
      expect(g).toBeGreaterThanOrEqual(lo);
      expect(g).toBeLessThanOrEqual(hi);
      expect(g % 100).toBe(0);
      seen.add(g);
    }
    expect(seen.has(lo)).toBe(true);
    expect(seen.has(hi)).toBe(true);
  });
});

describe('5. 데이터 정합(가격 목표)', () => {
  it('카드 가중치 합 100, 전용 재료 기대값으로 계산한 상자 도달 주 수가 목표 안', () => {
    const shop = getGameData().raidShop as NonNullable<ReturnType<typeof getGameData>['raidShop']>;
    const perWeek: Record<string, number> = { raid_skeleton_king: 3, raid_grah: 1 };
    const expected: Record<string, number> = { raid_skeleton_king: 10.6, raid_grah: 10.24 };
    for (const d of raids()) {
      const rr = d.raidReward as NonNullable<DungeonDef['raidReward']>;
      expect(rr.cards.reduce((a, c) => a + c.weight, 0)).toBe(100);
      const perCard = rr.cards.filter((c) => c.itemId === rr.materialItem).reduce((a, c) => a + (c.weight / 100) * ((c.min + c.max) / 2), 0);
      const m = perCard * eco().dungeons.cards.count;
      expect(m).toBeCloseTo(expected[d.id] as number, 6);
      for (const p of shop.products.values()) {
        if (p.raidId !== d.id) continue;
        const weeks = p.price / m / (perWeek[d.id] as number);
        if (p.rarityWeights.length === 1 && p.rarityWeights[0]?.rarity === 'Legendary') {
          expect(weeks).toBeGreaterThanOrEqual(8);
          expect(weeks).toBeLessThanOrEqual(12);
        } else {
          expect(weeks).toBeGreaterThanOrEqual(2);
          expect(weeks).toBeLessThanOrEqual(4);
        }
      }
    }
  });
});

describe('6. raid_shop.json 정합', () => {
  it('재료는 캐릭터 귀속·판매가 0, 직업마다 (단계, 부위, 등급) 장비가 하나, data_version에 포함', () => {
    const shop = getGameData().raidShop as NonNullable<ReturnType<typeof getGameData>['raidShop']>;
    expect(shop.products.size).toBe(4);
    for (const p of shop.products.values()) {
      expect(eco().items.get(p.materialItem)?.bind).toBe('character');
      expect(eco().shop.sellPrices.get(p.materialItem) ?? 0).toBe(0);
      for (const cls of ['warrior', 'mage']) {
        for (const w of p.rarityWeights) {
          const key = p.results[cls]?.[w.rarity] as string;
          const e = equipOf(key) as NonNullable<ReturnType<typeof equipOf>>;
          expect([e.levelTier, e.category, e.rarity]).toEqual([p.tier, 'Weapon', w.rarity]);
        }
      }
    }
    // 레전더리 상자는 전사 검, 마법사 지팡이
    expect(shop.products.get('king_legend_weapon')?.results.warrior?.Legendary).toBe('eq_sword_20_l');
    expect(shop.products.get('grah_legend_weapon')?.results.mage?.Legendary).toBe('eq_staff_40_l');
    const ver = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'data_version.json'), 'utf8')) as { files: Record<string, string> };
    expect(Object.keys(ver.files)).toContain('raid_shop.json');
  });
});

describe('7. income_caps', () => {
  it('raidMidGoldEq/raidFinalGoldEq가 레이드 확정 골드 최댓값 이상', () => {
    const caps = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'income_caps.json'), 'utf8')) as {
      bands: { perDay: { raidMidGoldEq: number }; perWeek: { raidFinalGoldEq: number } }[];
    };
    const mid = Math.max(...caps.bands.map((b) => b.perDay.raidMidGoldEq));
    const fin = Math.max(...caps.bands.map((b) => b.perWeek.raidFinalGoldEq));
    expect(mid).toBeGreaterThanOrEqual((raid('raid_skeleton_king').raidReward as { goldMax: number }).goldMax);
    expect(fin).toBeGreaterThanOrEqual((raid('raid_grah').raidReward as { goldMax: number }).goldMax);
  });
});
