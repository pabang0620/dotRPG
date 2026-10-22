// 챕터 3 잠금 0단계(Docs/ch3/IMPLEMENTATION_ORDER.md): CHAPTER3_OPEN 플래그와 레벨 상한(levelCap), 밴드 밖 레벨 처리
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { bandOf } from '../src/domains/antiabuse/velocity';
import { validateCareer, type CareerState } from '../src/domains/characters/careerRules';
import { tierOf } from '../src/domains/minidungeon/miniDungeonRules';
import { getIncomeCaps } from '../src/gamedata/antiAbuseData';
import { assertOpen, CHAPTER2_LEVEL_CAP, CHAPTER3_LEVEL_CAP, isChapter3, levelCap } from '../src/gamedata/chapterGate';
import { bandFor, getDailyQuests } from '../src/gamedata/dailyQuests';
import { rollEquipmentDrop, rollKillDrops, rollMapDrops } from '../src/domains/kills/killRules';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { fakeRng, get, newHero, post, seedLevel } from './economyHelpers';
import { buildApp, DATA_DIR, rehashDataDir, resetDb, shutdown } from './helpers';

const forestKill = { map_id: 'forest', monster_id: 'skeleton' };

/** progression.maxLevel을 바꾼 데이터 복사본(1단계부터 실제 데이터는 50이다. 40은 옛 데이터를 흉내 낸다) */
function dataWithMax(maxLevel: number): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-ch3-'));
  for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
  const file = path.join(dir, 'progression.json');
  const prog = JSON.parse(fs.readFileSync(file, 'utf8')) as { maxLevel: number; xpToNext: number[] };
  prog.maxLevel = maxLevel;
  while (prog.xpToNext.length < maxLevel - 1) prog.xpToNext.push(prog.xpToNext[prog.xpToNext.length - 1] as number);
  fs.writeFileSync(file, JSON.stringify(prog));
  rehashDataDir(dir);
  return dir;
}
const dataWithMax50 = (): string => dataWithMax(CHAPTER3_LEVEL_CAP);

async function levelXp(dbId: number): Promise<{ level: number; xp: number }> {
  const r = await getPool().query<{ level: number; xp: number }>('SELECT level, xp FROM characters WHERE id = $1', [dbId]);
  return r.rows[0] as { level: number; xp: number };
}

beforeEach(async () => {
  await resetDb();
  setClockOverride(() => new Date('2026-10-05T03:00:00Z'));
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  buildApp();
  await shutdown();
});

describe('레벨 상한(levelCap)', () => {
  it('플래그 0(기본): 상한 40, Lv40은 경험치를 받지 않는다', async () => {
    const app = buildApp();
    expect(levelCap()).toBe(CHAPTER2_LEVEL_CAP);
    const h = await newHero(app);
    await seedLevel(h, CHAPTER2_LEVEL_CAP);
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(200);
    expect(res.body.data.granted_xp).toBe(0);
    expect(await levelXp(h.dbId)).toEqual({ level: CHAPTER2_LEVEL_CAP, xp: 0 });
  });

  it('플래그 0이면 데이터가 50이어도 40에서 멈춘다', async () => {
    const app = buildApp({ GAME_DATA_DIR: dataWithMax50() });
    expect(getGameData().economy.progression.maxLevel).toBe(CHAPTER3_LEVEL_CAP);
    expect(levelCap()).toBe(CHAPTER2_LEVEL_CAP);
    const h = await newHero(app);
    await seedLevel(h, CHAPTER2_LEVEL_CAP);
    await post(app, h, '/kills', forestKill);
    expect((await levelXp(h.dbId)).level).toBe(CHAPTER2_LEVEL_CAP);
  });

  it('실제 데이터는 만렙 50(1단계), 플래그 0이면 상한 40', () => {
    buildApp();
    expect(getGameData().economy.progression.maxLevel).toBe(CHAPTER3_LEVEL_CAP);
    expect(getGameData().economy.progression.xpToNext.length).toBeGreaterThanOrEqual(CHAPTER3_LEVEL_CAP - 1);
    expect(levelCap()).toBe(CHAPTER2_LEVEL_CAP);
  });

  it('플래그 1이어도 데이터가 40이면 40(상한 = min(데이터, 챕터))', () => {
    buildApp({ GAME_DATA_DIR: dataWithMax(CHAPTER2_LEVEL_CAP), CHAPTER3_OPEN: 'true' });
    expect(levelCap()).toBe(CHAPTER2_LEVEL_CAP);
  });

  it('플래그 1 + 데이터 50: Lv40 캐릭터가 41이 된다', async () => {
    const app = buildApp({ GAME_DATA_DIR: dataWithMax50(), CHAPTER3_OPEN: 'true' });
    expect(levelCap()).toBe(CHAPTER3_LEVEL_CAP);
    const h = await newHero(app);
    await seedLevel(h, CHAPTER2_LEVEL_CAP);
    const need = getGameData().economy.progression.xpToNext[CHAPTER2_LEVEL_CAP - 1] as number;
    await getPool().query('UPDATE characters SET xp = $2 WHERE id = $1', [h.dbId, need - 1]);
    const res = await post(app, h, '/kills', forestKill);
    expect(res.status).toBe(200);
    expect(res.body.data.granted_xp).toBeGreaterThan(0);
    expect((await levelXp(h.dbId)).level).toBe(CHAPTER2_LEVEL_CAP + 1);
  });

  it('/meta가 chapter3_open과 level_cap을 알려 준다', async () => {
    const closed = await request(buildApp()).get('/meta');
    expect(closed.body.data).toMatchObject({ chapter3_open: false, level_cap: CHAPTER2_LEVEL_CAP });
    const open = await request(buildApp({ GAME_DATA_DIR: dataWithMax50(), CHAPTER3_OPEN: 'true' })).get('/meta');
    expect(open.body.data).toMatchObject({ chapter3_open: true, level_cap: CHAPTER3_LEVEL_CAP });
  });
});

describe('밴드 밖 레벨(Lv41+)', () => {
  it('수입 상한: 34~40은 그대로, 41~44·45~47·48~50 대역이 있고 범위 밖은 가장 가까운 대역', () => {
    buildApp();
    const caps = getIncomeCaps()!;
    expect(caps).not.toBeNull();
    expect(bandOf(caps, CHAPTER2_LEVEL_CAP)).toMatchObject({ minLevel: 34, maxLevel: CHAPTER2_LEVEL_CAP });
    expect(bandOf(caps, 41)).toMatchObject({ minLevel: 41, maxLevel: 44 });
    expect(bandOf(caps, 46)).toMatchObject({ minLevel: 45, maxLevel: 47 });
    const last = bandOf(caps, CHAPTER3_LEVEL_CAP);
    expect(last).toMatchObject({ minLevel: 48, maxLevel: CHAPTER3_LEVEL_CAP });
    expect(bandOf(caps, 60)).toBe(last);
  });

  it('일일·주간 의뢰: 36~40은 그대로, 41~45(sayab_low)·46~50(sayab_high), Lv40 의뢰는 바뀌지 않는다', () => {
    buildApp();
    for (const board of [getDailyQuests().day, getDailyQuests().week]) {
      expect(bandFor(board, CHAPTER2_LEVEL_CAP)).toMatchObject({ id: 'winter_peak', minLevel: 36, maxLevel: CHAPTER2_LEVEL_CAP });
      expect(bandFor(board, 41)).toMatchObject({ id: 'sayab_low' });
      expect(bandFor(board, CHAPTER3_LEVEL_CAP)).toMatchObject({ id: 'sayab_high' });
    }
    expect(bandFor(getDailyQuests().day, 41).gold).toBe(1400);
    expect(bandFor(getDailyQuests().day, 46).gold).toBe(1600);
  });

  it('미니 던전 티어: Lv39는 30, 닫힌 상한(40)에서는 없음, 열리면 Lv45는 티어 40(5단계), 만렙 50은 없음', () => {
    expect(tierOf(39, CHAPTER2_LEVEL_CAP)).toBe(30);
    expect(tierOf(CHAPTER2_LEVEL_CAP, CHAPTER2_LEVEL_CAP)).toBeNull();
    expect(tierOf(45, CHAPTER3_LEVEL_CAP)).toBe(40);
    expect(tierOf(CHAPTER3_LEVEL_CAP, CHAPTER3_LEVEL_CAP)).toBeNull();
  });
});

describe('assertOpen', () => {
  it('태그 없는 기존 콘텐츠는 항상 열림', () => {
    buildApp();
    expect(isChapter3('quest', 'c1_intro')).toBe(false);
    expect(isChapter3('map', 'forest')).toBe(false);
    expect(() => assertOpen('map', 'forest')).not.toThrow();
  });

  it('티어 8·9 장비와 3장 재료는 챕터 3 태그(강화 키도), Lv.40 장비는 아니다', () => {
    buildApp();
    expect(isChapter3('item', 'eq_sword_50_l')).toBe(true);
    expect(isChapter3('item', 'eq_sword_45_c+3')).toBe(true);
    expect(isChapter3('item', 'mat_raid_kamatz')).toBe(true);
    expect(isChapter3('item', 'mat_raid_xibak')).toBe(true);
    expect(isChapter3('item', 'eq_sword_40_l')).toBe(false);
    expect(() => assertOpen('item', 'eq_sword_50_l')).toThrow();
    buildApp({ CHAPTER3_OPEN: 'true' });
    expect(() => assertOpen('item', 'eq_sword_50_l')).not.toThrow();
  });

  it('닫혀 있으면 3장 장비 구매는 422 CHAPTER_LOCKED, 열리면 레벨 조건으로 넘어간다', async () => {
    const app = buildApp();
    const h = await newHero(app);
    const closed = await post(app, h, '/shop/buy', { item_id: 'eq_sword_45_c', count: 1 });
    expect(closed.status).toBe(422);
    expect(closed.body.errors.code).toBe('CHAPTER_LOCKED');
    const open = buildApp({ CHAPTER3_OPEN: 'true' });
    const h2 = await newHero(open);
    const res = await post(open, h2, '/shop/buy', { item_id: 'eq_sword_45_c', count: 1 });
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('LEVEL_TOO_LOW');
  });

  it('드롭 단계는 레벨 상한을 넘지 않는다: 닫히면 Lv53 몬스터도 Lv.40 단계', () => {
    buildApp();
    const eco = getGameData().economy;
    const rng = fakeRng({ unit: 0, int: () => 0 });
    const key = rollEquipmentDrop(eco, 'warrior', 1, rng, 53);
    expect(key).not.toBeNull();
    expect(eco.shop.equipment.get(key!.split('+')[0]!)?.levelTier).toBe(7);
    buildApp({ CHAPTER3_OPEN: 'true' });
    const open = rollEquipmentDrop(getGameData().economy, 'warrior', 1, rng, 53);
    expect(getGameData().economy.shop.equipment.get(open!.split('+')[0]!)?.levelTier).toBe(9);
  });

  it('챕터 3 업적(레벨 50·특화)은 닫혀 있으면 목록에 없고, 열리면 보인다', async () => {
    const closedApp = buildApp();
    const h = await newHero(closedApp);
    const closed = await get(closedApp, h, '/achievements');
    const ids = (closed.body.data.achievements as { id: string }[]).map((a) => a.id);
    expect(ids).toContain('level_40');
    expect(ids).not.toContain('level_50');
    const openApp = buildApp({ CHAPTER3_OPEN: 'true' });
    const open = await get(openApp, h, '/achievements');
    expect((open.body.data.achievements as { id: string }[]).map((a) => a.id)).toEqual(expect.arrayContaining(['level_50', 'spec_first', 'spec_all']));
  });
});

// [CH3] 3단계 세계: 메말라 가는 협곡·사얍 맵, 공허·잿빛 손 몬스터, 정찰대장(필드 정예)
describe('3장 세계 잠금·정예', () => {
  const dryKill = { map_id: 'dry_floor', monster_id: 'void_hound' };

  it('닫혀 있으면 3장 맵·몬스터 처치를 받지 않는다(422 CHAPTER_LOCKED)', async () => {
    const app = buildApp();
    const h = await newHero(app);
    await seedLevel(h, CHAPTER2_LEVEL_CAP);
    const res = await post(app, h, '/kills', dryKill);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('CHAPTER_LOCKED');
    // 기존 맵에 3장 몬스터 id를 붙여 와도 거부
    const mixed = await post(app, h, '/kills', { map_id: 'forest', monster_id: 'void_hound' });
    expect(mixed.status).toBe(422);
    expect(isChapter3('map', 'sayab')).toBe(true);
    expect(isChapter3('map', 'sayab_shop')).toBe(true);
    expect(isChapter3('monster', 'grey_captain')).toBe(true);
    expect(isChapter3('map', 'canyon')).toBe(false);
  });

  it('열리면 3장 사냥터 처치가 인정되고 Lv42 몬스터 경험치를 준다', async () => {
    const app = buildApp({ CHAPTER3_OPEN: 'true' });
    const h = await newHero(app);
    await seedLevel(h, 41);
    const res = await post(app, h, '/kills', dryKill);
    expect(res.status).toBe(200);
    expect(res.body.data.granted_xp).toBeGreaterThan(0);
  });

  it('정찰대장은 같은 맵 일반 처치 60마리마다 1마리만 인정한다', async () => {
    const app = buildApp({ CHAPTER3_OPEN: 'true' });
    const h = await newHero(app);
    await seedLevel(h, 45);
    const elite = { map_id: 'dry_stones', monster_id: 'grey_captain' };
    const early = await post(app, h, '/kills', elite);
    expect(early.status).toBe(422);
    expect(early.body.errors.code).toBe('KILL_REJECTED');
    const extra = getGameData().economy.mapExtra.get('dry_stones');
    expect(extra?.fieldElite?.everyKills).toBe(60);
    expect(getGameData().economy.monsters.get('grey_captain')?.sureGear).toBe(true);
  });

  it('정예는 장비 1개가 확정이다(일반 몬스터는 확률)', () => {
    buildApp({ CHAPTER3_OPEN: 'true' });
    const eco = getGameData().economy;
    const captain = eco.monsters.get('grey_captain')!;
    const knight = eco.monsters.get('grey_knight')!;
    const rng = fakeRng({ unit: 0.99, int: () => 0 });
    const gearOf = (rows: { itemKey: string }[]) => rows.filter((r) => r.itemKey.startsWith('eq_'));
    expect(gearOf(rollKillDrops(eco, captain, 'warrior', 1, rng, 1, 49))).toHaveLength(1);
    expect(gearOf(rollKillDrops(eco, knight, 'warrior', 1, rng, 1, 49))).toHaveLength(0);
  });

  it('사냥터 퀘스트 수집품: 그 맵·그 몬스터에서만, chance로 1개', () => {
    buildApp({ CHAPTER3_OPEN: 'true' });
    const eco = getGameData().economy;
    const hit = fakeRng({ unit: 0 });
    expect(rollMapDrops(eco, 'dry_floor', 'void_hound', hit).map((d) => d.itemKey)).toEqual(['qi_dry_hide', 'qi_dry_flower']);
    expect(rollMapDrops(eco, 'dry_floor', 'void_sentry', hit).map((d) => d.itemKey)).toEqual(['qi_dry_flower']);
    expect(rollMapDrops(eco, 'dry_stones', 'void_sentry', hit).map((d) => d.itemKey)).toEqual(['qi_void_essence', 'qi_ash_herb']);
    expect(rollMapDrops(eco, 'forest', 'skeleton', hit)).toEqual([]);
    expect(rollMapDrops(eco, 'dry_floor', 'void_hound', fakeRng({ unit: 0.99 }))).toEqual([]);
    expect(isChapter3('item', 'qi_void_essence')).toBe(true);
  });
});

// [CH3] 6단계 잠금 전수(Docs/ch3/IMPLEMENTATION_ORDER.md 6단계): 닫힘이면 3장 진입점이 전부 거부되고, 열리면 다음 검사로 넘어간다
describe('6단계: 닫힘은 전부 거부, 열림은 통과', () => {
  const locked = (res: { status: number; body: { errors?: { code?: string } } }): void => {
    expect(res.status).toBe(422);
    expect(res.body.errors?.code).toBe('CHAPTER_LOCKED');
  };
  const notLocked = (res: { body: { errors?: { code?: string } } }): void => {
    expect(res.body.errors?.code).not.toBe('CHAPTER_LOCKED');
  };
  const specState = (): CareerState => ({
    schema: 1, career: 0, nodes: [], training: [], refunded: 0, questStage: 0, awakened: false, specs: [{ skill: 'crush', side: 'a' }],
  } as CareerState);
  const checkSpec = (): void => validateCareer(specState(), null, { level: 45, class: 'warrior' }, [], () => false);

  async function probe(open: boolean) {
    const app = buildApp(open ? { CHAPTER3_OPEN: 'true' } : {});
    const h = await newHero(app);
    await seedLevel(h, CHAPTER2_LEVEL_CAP);
    return {
      quest: await post(app, h, '/quests/c3_depart/claim', {}),
      raid: await post(app, h, '/dungeon-runs', { dungeon_id: 'raid_xibak', difficulty: 0 }),
      field: await post(app, h, '/field-sessions/enter', { map_id: 'dry_floor' }),
      kill: await post(app, h, '/kills', { map_id: 'dry_floor', monster_id: 'void_hound' }),
      mini: await post(app, h, '/mini-dungeon/enter', { tier: 40 }),
      boss: await get(app, h, '/bosses'),
      h,
    };
  }

  it('닫힘(CHAPTER3_OPEN 0): Lv40 유지, 3장 퀘스트·레이드·사냥터·미니 40·특화 거부, 보스 Lv40', async () => {
    const r = await probe(false);
    locked(r.quest);
    locked(r.raid);
    locked(r.field);
    locked(r.kill);
    locked(r.mini);
    expect(() => checkSpec()).toThrow(expect.objectContaining({ code: 'CHAPTER_LOCKED' }));
    expect(r.boss.status).toBe(200);
    expect(r.boss.body.data.world.boss_level).toBe(CHAPTER2_LEVEL_CAP);
    expect(await levelXp(r.h.dbId)).toEqual({ level: CHAPTER2_LEVEL_CAP, xp: 0 });
    expect(isChapter3('quest', 'c3_depart')).toBe(true);
  });

  it('열림(CHAPTER3_OPEN 1): 같은 요청이 잠금 없이 다음 검사로 넘어가고 보스는 Lv50', async () => {
    const r = await probe(true);
    notLocked(r.quest);
    expect(r.quest.body.errors.code).toBe('QUEST_PREREQ'); // c2_end를 끝내지 않았다
    notLocked(r.raid);
    expect(r.field.status).toBe(200);
    expect(r.kill.status).toBe(200);
    expect(r.mini.status).toBeLessThan(300);
    expect(() => checkSpec()).not.toThrow(expect.objectContaining({ code: 'CHAPTER_LOCKED' }));
    expect(r.boss.body.data.world.boss_level).toBe(CHAPTER3_LEVEL_CAP);
  });
});
