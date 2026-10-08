// 10단계 14절 A: 소탕 순수 함수(경험치, 카드 한 장, 클리어권 가격)와 직접 플레이 카드 굴림의 불변
import { getGameData } from '../src/gamedata/loader';
import type { DungeonDef } from '../src/gamedata/economyData';
import { f32, keyAt, roundHalfEven } from '../src/utils/itemKey';
import { tierOfLevel } from '../src/utils/gearTier';
import { rollCards, rollGear, rollJackpot, diffOf, type Card, type DiffNumbers } from '../src/domains/dungeons/dungeonRules';
import { rollSweepCard, sweepXp, ticketUnitPrice } from '../src/domains/sweep/sweepRules';
import type { Rng } from '../src/utils/rng';
import { buildApp, shutdown } from './helpers';

buildApp({ SWEEP_ENABLED: 'true' });
afterAll(shutdown);

const gd = () => getGameData();
const sweep = () => gd().sweep as NonNullable<ReturnType<typeof gd>['sweep']>;

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

const dungeon = (id: string): DungeonDef => gd().economy.dungeons.byId.get(id) as DungeonDef;

describe('A1 sweepXp', () => {
  it('설계 3.3 표의 소탕 경험치와 같다', () => {
    const eco = gd().economy;
    const expected: Record<string, number[]> = {
      gold_vein: [1102, 2771, 5913, 12474],
      smelter: [1153, 2849, 6036, 12624],
      mana_graveyard: [1270, 3077, 6423, 13308],
      training_forest: [1911, 4638, 9662, 20002],
      armory: [1322, 3198, 6663, 13796],
    };
    for (const [id, xs] of Object.entries(expected)) {
      const d = dungeon(id);
      xs.forEach((x, i) => expect(sweepXp(d, diffOf(eco, d, i) as DiffNumbers, i, sweep())).toBe(x));
    }
  });

  it('xpBonusPercent 0과 10의 차이(보너스는 데이터 한 값)', () => {
    const eco = gd().economy;
    const d = dungeon('gold_vein');
    const diff = diffOf(eco, d, 0) as DiffNumbers;
    expect(sweepXp(d, diff, 0, { xpBonusPercent: 0 })).toBe(1102);
    expect(sweepXp(d, diff, 0, { xpBonusPercent: 10 })).toBe(roundHalfEven(f32(1102 * f32(1.1))));
    expect(sweepXp(d, diff, 0, { xpBonusPercent: 10 })).toBe(1212);
  });
});

const isGear = (c: Card): boolean => gd().economy.shop.equipment.has(c.item_key.split('+')[0] as string);

describe('A2 rollSweepCard', () => {
  it('장비가 뽑히면 30%만 비장비 풀에서 다시 굴린다(난수 고정)', () => {
    const eco = gd().economy;
    const d = dungeon('gold_vein');
    const diff = diffOf(eco, d, 0) as DiffNumbers;
    // 첫 int(0, 합)가 가장 큰 값 -> 마지막 항목(gear). 다음 int(0, 100)이 99 -> 30% 구간(재굴림), 이어서 0 -> 비장비 첫 항목(gold)
    const seq = [103, 99, 0, 300];
    const rng: Rng = { unit: () => 1, int: (min) => (seq.length > 0 ? Math.max(min, seq.shift() as number) : min) };
    const c = rollSweepCard(eco, d, diff, 'warrior', rng, { gearKeepPercent: 70 });
    expect(c.item_key).toBe('gold');
    // 같은 첫 굴림 + int(0, 100)=0 -> 유지(장비)
    const seq2 = [103, 0];
    const rng2: Rng = { unit: () => 1, int: (min, max) => (seq2.length > 0 ? Math.min(max - 1, Math.max(min, seq2.shift() as number)) : min) };
    expect(isGear(rollSweepCard(eco, d, diff, 'warrior', rng2, { gearKeepPercent: 70 }))).toBe(true);
  });

  it('10만 장 표본: 장비 비율이 직접 플레이 한 장의 0.7배, 대박 없음, 영웅은 보호권 포함', () => {
    const eco = gd().economy;
    const d = dungeon('gold_vein');
    const diff = { ...(diffOf(eco, d, 0) as DiffNumbers), jackpotPerMille: 0 };
    const N = 100_000;
    let direct = 0;
    let sw = 0;
    const r1 = seeded(11);
    const r2 = seeded(22);
    for (let i = 0; i < N; i++) {
      if (isGear(rollCards({ ...eco, dungeons: { ...eco.dungeons, cards: { ...eco.dungeons.cards, count: 1 } } }, d, diff, 'warrior', r1)[0] as Card)) direct++;
      if (isGear(rollSweepCard(eco, d, diff, 'warrior', r2, { gearKeepPercent: 70 }))) sw++;
    }
    expect(direct / N).toBeGreaterThan(0.09);
    expect(direct / N).toBeLessThan(0.103);
    expect(sw / direct).toBeGreaterThan(0.66);
    expect(sw / direct).toBeLessThan(0.74);
    // 대박 확률 1000/1000이어도 소탕은 굴리지 않는다: 장비 비율이 그대로
    const forced = { ...diff, jackpotPerMille: 1000 };
    let swJack = 0;
    const r3 = seeded(33);
    for (let i = 0; i < N; i++) if (isGear(rollSweepCard(eco, d, forced, 'warrior', r3, { gearKeepPercent: 70 }))) swJack++;
    expect(swJack / N).toBeLessThan(0.08);
    // 영웅(보호권 가중 6)
    const hero = diffOf(eco, d, 3) as DiffNumbers;
    const r4 = seeded(44);
    let ticket = 0;
    for (let i = 0; i < 20_000; i++) if (rollSweepCard(eco, d, hero, 'warrior', r4, { gearKeepPercent: 70 }).item_key === eco.enhance.ticketItem) ticket++;
    expect(ticket).toBeGreaterThan(0);
  });

  it('장비 항목이 하나도 없는 풀이면 장비로 둔다', () => {
    const eco = gd().economy;
    const d = { ...dungeon('gold_vein'), rewards: [{ itemId: 'gear', min: 1, max: 1, weight: 10 }] } as DungeonDef;
    const diff = diffOf(eco, dungeon('gold_vein'), 0) as DiffNumbers;
    const c = rollSweepCard(eco, d, diff, 'warrior', seeded(5), { gearKeepPercent: 0 });
    expect(isGear(c)).toBe(true);
  });
});

/** 10단계 이전의 rollCards(루프 본문 그대로). 분리 뒤 결과가 비트 단위로 같아야 한다 */
function legacyRollCards(eco: ReturnType<typeof gd>['economy'], d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng): Card[] {
  const table = [...d.rewards];
  if (diff.ticketWeight > 0) table.push({ itemId: eco.enhance.ticketItem, min: 1, max: 1, weight: diff.ticketWeight });
  const total = table.reduce((a, e) => a + Math.max(0, e.weight), 0);
  const tier = tierOfLevel(diff.recommendedLevel);
  const cards: Card[] = [];
  for (let i = 0; i < eco.dungeons.cards.count; i++) {
    let roll = rng.int(0, Math.max(1, total));
    let pick = table[0] as (typeof table)[number];
    for (const e of table) {
      if (roll < e.weight) {
        pick = e;
        break;
      }
      roll -= e.weight;
    }
    let card: Card;
    if (pick.itemId === 'gear') {
      card = { item_key: rollGear(eco, cls, diff.minGearRarity, rng, tier), count: 1 };
    } else {
      let n = rng.int(pick.min, pick.max + 1);
      if (pick.itemId !== eco.enhance.ticketItem) n = Math.max(1, roundHalfEven(f32(n * diff.rewardMul)));
      card = { item_key: pick.itemId, count: n };
    }
    if ((diff.jackpotPerMille ?? 0) > 0 && rng.int(0, 1000) < (diff.jackpotPerMille ?? 0)) {
      const jackpot = rollJackpot(eco, cls, rng, tier);
      if (jackpot) card = { item_key: jackpot, count: 1 };
    }
    cards.push(card);
  }
  return cards;
}

describe('A3 직접 플레이 rollCards 불변', () => {
  it('모든 던전·난이도·직업에서 같은 난수 열이 분리 전과 같은 카드를 낸다', () => {
    const eco = gd().economy;
    let compared = 0;
    for (const d of eco.dungeons.byId.values()) {
      // 13단계: 레이드는 rollRaidCards(카드 4장 모두 받기)로 갈라져 옛 루프와 비교하지 않는다(raidRewards.test.ts)
      if (d.isRaid) continue;
      const diffs = d.isRaid ? [d.raidNumbers as DiffNumbers] : eco.dungeons.difficulties.map((_x, i) => diffOf(eco, d, i) as DiffNumbers);
      for (const diff of diffs) {
        for (const cls of ['warrior', 'mage']) {
          for (let seed = 1; seed <= 150; seed++) {
            expect(rollCards(eco, d, diff, cls, seeded(seed))).toEqual(legacyRollCards(eco, d, diff, cls, seeded(seed)));
            compared++;
          }
        }
      }
    }
    expect(compared).toBeGreaterThan(3000);
  });
});

describe('A4 ticketUnitPrice', () => {
  it('장비 단계 경계(9->10, 14->15, 39->40)에서 가격 배수가 바뀐다', () => {
    const base = sweep().shop.basePrice;
    const at = (lv: number) => ticketUnitPrice(sweep(), lv);
    expect(at(1)).toBe(base);
    expect(at(9)).toBe(base);
    expect(at(10)).toBe(base * 2);
    expect(at(14)).toBe(base * 2);
    expect(at(15)).toBe(base * 3);
    expect(at(39)).toBe(base * 7);
    expect(at(40)).toBe(base * 8);
  });
});
