// 2026-10-29 요일 던전 난이도 악몽(4)·지옥(5)·심연(6): Lv30·35·40 장비 단계, 요일 던전만, 레벨 입장, 소탕(Docs/PLAN_WEEKDAY_TIERS_2026-10-29.md)
import { getPool } from '../src/db/pool';
import {
  clearXpBase,
  diffOf,
  FIRST_WEEKDAY_ONLY,
  LORD_DIFFICULTY,
  rollGear,
  rollOneCard,
  shownDifficulties,
  type DiffNumbers,
} from '../src/domains/dungeons/dungeonRules';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { tierOfLevel } from '../src/utils/gearTier';
import { setRng, type Rng } from '../src/utils/rng';
import { fakeRng, get, newHero, post, seedLevel, type Hero } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { MONDAY, seedClear, seedTickets, sweepRun } from './sweepHelpers';

const enter = (app: ReturnType<typeof buildApp>, h: Hero, dungeon: string, difficulty: number) =>
  post(app, h, '/dungeon-runs', { dungeon_id: dungeon, difficulty });

/** 결정적 난수(선형 합동). 굴림 분포를 넓게 훑을 때 쓴다 */
function lcg(seed: number): Rng {
  let x = seed >>> 0;
  const next = (): number => {
    x = (Math.imul(x, 1664525) + 1013904223) >>> 0;
    return x / 4294967296;
  };
  return { int: (min, max) => (max <= min ? min : min + Math.floor(next() * (max - min))), unit: () => next() };
}

beforeEach(async () => {
  await resetDb();
  setClockOverride(() => new Date(MONDAY));
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  buildApp();
  await shutdown();
});

describe('새 난이도 데이터', () => {
  it('영웅과 군주 사이 3단계, 권장 레벨의 장비 단계가 Lv30·35·40이고 Lv1~27 난이도는 그대로다', () => {
    buildApp();
    const diffs = getGameData().economy.dungeons.difficulties;
    expect(diffs).toHaveLength(LORD_DIFFICULTY + 1);
    expect(diffs.slice(0, 4).map((d) => [d.recommendedLevel, d.hpMul, d.rewardMul, d.minGearRarity, d.ticketWeight, d.jackpotPerMille])).toEqual([
      [5, 1, 1, 'Common', 0, 3],
      [12, 1.9, 1.6, 'Uncommon', 0, 4],
      [20, 7.7, 2.4, 'Rare', 0, 5],
      [27, 12, 3.4, 'Epic', 6, 6],
    ]);
    const tiers = diffs.slice(FIRST_WEEKDAY_ONLY, LORD_DIFFICULTY);
    expect(tiers.map((d) => d.recommendedLevel)).toEqual([32, 37, 40]);
    expect(tiers.map((d) => tierOfLevel(d.recommendedLevel))).toEqual([5, 6, 7]);
    for (const d of tiers) {
      expect(d.monsterLevel).toBe(d.recommendedLevel - 5);
      expect(d.minGearRarity).toBe('Epic');
    }
    // 난이도가 오를수록 체력 배율·보상 배율·권장 레벨이 줄지 않는다(군주까지)
    for (let i = 1; i < diffs.length; i++) {
      const a = diffs[i - 1] as (typeof diffs)[number];
      const b = diffs[i] as (typeof diffs)[number];
      expect(b.recommendedLevel).toBeGreaterThan(a.recommendedLevel);
      expect(b.hpMul).toBeGreaterThanOrEqual(a.hpMul - 0.2); // 심연 18.1 -> 군주 18(레벨 5 높아 실제 체력은 더 크다)
      expect(b.rewardMul).toBeGreaterThanOrEqual(a.rewardMul);
      expect(b.monsterLevel).toBeGreaterThan(a.monsterLevel);
    }
    expect(diffs[LORD_DIFFICULTY]?.recommendedLevel).toBe(45);
  });

  it('요일 던전에만 있고(일반 던전·레이드는 없다), 클리어 경험치 하한은 영웅 이하라 1판 총 경험치가 늘지 않는다', () => {
    buildApp();
    const eco = getGameData().economy;
    const gold = eco.dungeons.byId.get('gold_vein');
    const normal = eco.dungeons.byId.get('normal_canyon');
    const raid = eco.dungeons.byId.get('raid_grah');
    if (!gold || !normal || !raid) throw new Error('데이터 없음');
    for (let i = FIRST_WEEKDAY_ONLY; i < LORD_DIFFICULTY; i++) {
      expect(diffOf(eco, gold, i)).not.toBeNull();
      expect(diffOf(eco, normal, i)).toBeNull();
      expect(diffOf(eco, raid, i)).toBeNull();
      expect(gold.referenceSeconds[i]).toBe(gold.referenceSeconds[3]);
      expect(gold.clearXpFloor[i]).toBeLessThanOrEqual(gold.clearXpFloor[3] as number);
      expect(clearXpBase(gold, diffOf(eco, gold, i) as DiffNumbers, i)).toBeGreaterThan(0);
    }
    expect(shownDifficulties(eco, gold, false)).toBe(LORD_DIFFICULTY);
    expect(shownDifficulties(eco, gold, true)).toBe(LORD_DIFFICULTY + 1);
    expect(shownDifficulties(eco, normal, true)).toBe(FIRST_WEEKDAY_ONLY);
  });

  it('카드 장비는 그 난이도의 단계(Lv30·35·40) 에픽·유니크만, 레전더리는 나오지 않는다', () => {
    buildApp();
    const eco = getGameData().economy;
    const gold = eco.dungeons.byId.get('gold_vein');
    if (!gold) throw new Error('gold_vein 없음');
    for (const [i, tier] of [[4, 5], [5, 6], [6, 7]] as const) {
      const diff = diffOf(eco, gold, i) as DiffNumbers;
      const rarities = new Set<string>();
      for (const cls of ['warrior', 'mage', 'rogue']) {
        const rng = lcg(1000 + i);
        for (let n = 0; n < 400; n++) {
          const e = eco.shop.equipment.get(rollGear(eco, cls, diff.minGearRarity, rng, tierOfLevel(diff.recommendedLevel)).split('+')[0] as string);
          expect(e?.levelTier).toBe(tier);
          rarities.add(e?.rarity as string);
        }
        const r2 = lcg(77 + i);
        for (let n = 0; n < 400; n++) {
          const card = rollOneCard(eco, gold, diff, cls, r2);
          const e = eco.shop.equipment.get(card.item_key.split('+')[0] as string);
          if (e) expect(e.levelTier).toBe(tier);
        }
      }
      expect([...rarities].sort()).toEqual(['Epic', 'Unique']);
    }
  });
});

describe('입장과 소탕', () => {
  it('권장 레벨 - 5부터 들어간다(악몽 27, 지옥 32, 심연 35). 기록은 난이도 번호 그대로', async () => {
    const app = buildApp();
    const h = await newHero(app);
    for (const [diff, need] of [[4, 27], [5, 32], [6, 35]] as const) {
      await seedLevel(h, need - 1);
      const low = await enter(app, h, 'gold_vein', diff);
      expect(low.status).toBe(422);
      expect(low.body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need });
    }
    await seedLevel(h, 35);
    const ok = await enter(app, h, 'gold_vein', 6);
    expect(ok.status).toBe(201);
    const run = await getPool().query('SELECT difficulty FROM dungeon_runs WHERE character_id = $1', [h.dbId]);
    expect(run.rows).toEqual([{ difficulty: 6 }]);
  });

  it('일반 던전에는 악몽이 없다(422), 목록에는 요일 던전 7개·일반 던전 4개', async () => {
    const app = buildApp();
    const h = await newHero(app);
    await seedLevel(h, 40);
    const res = await enter(app, h, 'normal_canyon', 4);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('DUNGEON_UNKNOWN');
    const list = await get(app, h, '/dungeons');
    const byId = (id: string) => list.body.data.dungeons.find((d: { id: string }) => d.id === id);
    expect(byId('gold_vein').difficulties).toHaveLength(7);
    expect(byId('normal_canyon').difficulties).toHaveLength(4);
  });

  it('심연을 S등급으로 직접 클리어했으면 Lv40에서 소탕된다(경험치 = 심연 하한)', async () => {
    // 3장이 닫힌 서버는 Lv40이 만렙이라 경험치를 버린다: 하한 값을 보려고 3장을 연다
    const app = buildApp({ SWEEP_ENABLED: 'true', CHAPTER3_OPEN: 'true' });
    const h = await newHero(app);
    await seedLevel(h, 40);
    await seedClear(h, 'gold_vein', 6, 2);
    await seedTickets(h, { normal: 1 });
    const res = await sweepRun(app, h, 'gold_vein', 6);
    expect(res.status).toBe(201);
    const gold = getGameData().economy.dungeons.byId.get('gold_vein');
    expect(res.body.data.sweeps[0]).toMatchObject({ dungeon_id: 'gold_vein', difficulty: 6, xp: gold?.clearXpFloor[6] });
  });
});
