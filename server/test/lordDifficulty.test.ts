// 챕터 3 요일 던전 "군주" 난이도(4): 3장 잠금, Lv41 입장, 요일 던전만, 목록 노출, 추가 확정 재료(Docs/ch3/WORLD_AND_STORY.md 8절)
import { getPool } from '../src/db/pool';
import { diffOf, hasLord, LORD_DIFFICULTY, LORD_MIN_LEVEL, rollLordBonus, shownDifficulties } from '../src/domains/dungeons/dungeonRules';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { fakeRng, get, newHero, post, seedLevel, type Hero } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';

const MONDAY = '2026-10-05T03:00:00Z'; // 월요일 12:00 KST: gold_vein(월) 개방
const enter = (app: ReturnType<typeof buildApp>, h: Hero, dungeon: string, difficulty: number) =>
  post(app, h, '/dungeon-runs', { dungeon_id: dungeon, difficulty });

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

describe('군주 데이터', () => {
  it('요일 던전 5개에만 군주와 추가 확정 재료가 있다(월·수·금 침식 결정, 화·목 잿빛 인장)', () => {
    buildApp();
    const eco = getGameData().economy;
    expect(eco.dungeons.difficulties.length).toBe(LORD_DIFFICULTY + 1);
    const lord = [...eco.dungeons.byId.values()].filter((d) => hasLord(eco, d));
    expect(lord.map((d) => d.id).sort()).toEqual(['armory', 'gold_vein', 'mana_graveyard', 'smelter', 'training_forest']);
    for (const d of lord) {
      expect(d.lordSure).toBeDefined();
      expect(d.referenceSeconds.length).toBe(5);
      expect(d.clearXpFloor.length).toBe(5);
    }
    const material = (id: string) => eco.dungeons.byId.get(id)?.lordSure?.material;
    expect(['gold_vein', 'mana_graveyard', 'armory'].map(material)).toEqual(['mat_raid_xibak', 'mat_raid_xibak', 'mat_raid_xibak']);
    expect(['smelter', 'training_forest'].map(material)).toEqual(['mat_raid_kamatz', 'mat_raid_kamatz']);
    const normal = eco.dungeons.byId.get('normal_canyon');
    expect(normal && diffOf(eco, normal, LORD_DIFFICULTY)).toBeNull();
  });

  it('추가 확정 재료는 군주 판에서만 굴린다', () => {
    buildApp();
    const gold = getGameData().economy.dungeons.byId.get('gold_vein');
    if (!gold) throw new Error('gold_vein 없음');
    expect(rollLordBonus(gold, 3, fakeRng({ unit: 1 }))).toBeNull();
    const bonus = rollLordBonus(gold, LORD_DIFFICULTY, fakeRng({ unit: 1 }));
    expect(bonus?.item_key).toBe('mat_raid_xibak');
    expect(bonus?.count).toBeGreaterThanOrEqual(1);
    expect(bonus?.count).toBeLessThanOrEqual(2);
  });
});

describe('군주 입장과 목록', () => {
  it('3장이 닫혀 있으면 422 CHAPTER_LOCKED, 목록에는 난이도 4개만', async () => {
    const app = buildApp();
    const h = await newHero(app);
    await seedLevel(h, 45);
    const res = await enter(app, h, 'gold_vein', LORD_DIFFICULTY);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('CHAPTER_LOCKED');
    const list = await get(app, h, '/dungeons');
    const gold = list.body.data.dungeons.find((d: { id: string }) => d.id === 'gold_vein');
    expect(gold.difficulties).toHaveLength(LORD_DIFFICULTY);
    expect(shownDifficulties(getGameData().economy, getGameData().economy.dungeons.byId.get('gold_vein') as never, false)).toBe(4);
  });

  it('3장이 열리면 요일 던전 목록에 군주가 보이고, Lv41부터 들어간다', async () => {
    const app = buildApp({ CHAPTER3_OPEN: 'true' });
    const h = await newHero(app);
    const list = await get(app, h, '/dungeons');
    const gold = list.body.data.dungeons.find((d: { id: string }) => d.id === 'gold_vein');
    expect(gold.difficulties).toHaveLength(LORD_DIFFICULTY + 1);
    const normal = list.body.data.dungeons.find((d: { id: string }) => d.id === 'normal_canyon');
    expect(normal.difficulties).toHaveLength(LORD_DIFFICULTY);

    await seedLevel(h, LORD_MIN_LEVEL - 1);
    const low = await enter(app, h, 'gold_vein', LORD_DIFFICULTY);
    expect(low.status).toBe(422);
    expect(low.body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: LORD_MIN_LEVEL });

    await seedLevel(h, LORD_MIN_LEVEL);
    const ok = await enter(app, h, 'gold_vein', LORD_DIFFICULTY);
    expect(ok.status).toBe(201);
    const run = await getPool().query('SELECT difficulty FROM dungeon_runs WHERE character_id = $1', [h.dbId]);
    expect(run.rows).toEqual([{ difficulty: LORD_DIFFICULTY }]);
  });

  it('일반 던전·레이드에는 군주가 없다', async () => {
    const app = buildApp({ CHAPTER3_OPEN: 'true' });
    const h = await newHero(app);
    await seedLevel(h, 45);
    const normal = await enter(app, h, 'normal_canyon', LORD_DIFFICULTY);
    expect(normal.status).toBe(422);
    expect(normal.body.errors.code).toBe('DUNGEON_UNKNOWN');
  });

  it('난이도 5 이상은 입력 검증에서 거절된다', async () => {
    const app = buildApp({ CHAPTER3_OPEN: 'true' });
    const h = await newHero(app);
    const res = await enter(app, h, 'gold_vein', LORD_DIFFICULTY + 1);
    expect(res.status).toBe(400);
  });
});
