// 챕터 3 잠금 0단계(Docs/ch3/IMPLEMENTATION_ORDER.md): CHAPTER3_OPEN 플래그와 레벨 상한(levelCap), 밴드 밖 레벨 처리
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { bandOf } from '../src/domains/antiabuse/velocity';
import { tierOf } from '../src/domains/minidungeon/miniDungeonRules';
import { getIncomeCaps } from '../src/gamedata/antiAbuseData';
import { assertOpen, CHAPTER2_LEVEL_CAP, CHAPTER3_LEVEL_CAP, isChapter3, levelCap } from '../src/gamedata/chapterGate';
import { bandFor, getDailyQuests } from '../src/gamedata/dailyQuests';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { fakeRng, newHero, post, seedLevel } from './economyHelpers';
import { buildApp, DATA_DIR, rehashDataDir, resetDb, shutdown } from './helpers';

const forestKill = { map_id: 'forest', monster_id: 'skeleton' };

/** progression.maxLevel을 50으로 늘린 데이터(1단계 데이터를 흉내 낸다: 부족한 xpToNext 칸은 마지막 값으로 채운다) */
function dataWithMax50(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-ch3-'));
  for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
  const file = path.join(dir, 'progression.json');
  const prog = JSON.parse(fs.readFileSync(file, 'utf8')) as { maxLevel: number; xpToNext: number[] };
  prog.maxLevel = CHAPTER3_LEVEL_CAP;
  while (prog.xpToNext.length < CHAPTER3_LEVEL_CAP - 1) prog.xpToNext.push(prog.xpToNext[prog.xpToNext.length - 1] as number);
  fs.writeFileSync(file, JSON.stringify(prog));
  rehashDataDir(dir);
  return dir;
}

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

  it('플래그 1이어도 데이터가 40이면 40(상한 = min(데이터, 챕터))', () => {
    buildApp({ CHAPTER3_OPEN: 'true' });
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
  it('수입 상한: Lv41+는 마지막 대역(34~40)을 쓴다(상한이 없어지지 않는다)', () => {
    buildApp();
    const caps = getIncomeCaps()!;
    expect(caps).not.toBeNull();
    const last = bandOf(caps, CHAPTER2_LEVEL_CAP);
    expect(last.maxLevel).toBe(CHAPTER2_LEVEL_CAP);
    expect(bandOf(caps, 45)).toBe(last);
    expect(bandOf(caps, CHAPTER3_LEVEL_CAP)).toBe(last);
  });

  it('일일·주간 의뢰: 마지막 밴드는 36~40, Lv40은 그 밴드, Lv41+도 그 밴드로 떨어진다', () => {
    buildApp();
    for (const board of [getDailyQuests().day, getDailyQuests().week]) {
      const last = board.bands[board.bands.length - 1]!;
      expect(last.maxLevel).toBe(CHAPTER2_LEVEL_CAP);
      expect(bandFor(board, CHAPTER2_LEVEL_CAP)).toBe(last);
      expect(bandFor(board, 45)).toBe(last);
    }
  });

  it('미니 던전 티어: Lv39는 30, 상한 레벨과 표에 없는 티어(40)는 입장 불가', () => {
    expect(tierOf(39, CHAPTER2_LEVEL_CAP)).toBe(30);
    expect(tierOf(CHAPTER2_LEVEL_CAP, CHAPTER2_LEVEL_CAP)).toBeNull();
    expect(tierOf(45, CHAPTER3_LEVEL_CAP)).toBeNull();
  });
});

describe('assertOpen 뼈대', () => {
  it('태그 없는 기존 콘텐츠는 항상 열림', () => {
    buildApp();
    expect(isChapter3('quest', 'c1_intro')).toBe(false);
    expect(isChapter3('map', 'forest')).toBe(false);
    expect(() => assertOpen('map', 'forest')).not.toThrow();
  });
});
