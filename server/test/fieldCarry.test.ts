// 9단계 6절: 필드 캐리 감쇠 기본값(0.15/0.02), 하드 격차(경험치 1·드롭 0), 처치 골드 감쇠
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { getConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { isHardGap, xpFactor } from '../src/domains/fieldsessions/xpFactor';
import { getGameData } from '../src/gamedata/loader';
import { monsterXp, rollKillDrops } from '../src/domains/kills/killRules';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { fakeRng, seedLevel } from './economyHelpers';
import { buildApp, DATA_DIR, rehashDataDir, resetDb, shutdown } from './helpers';
import { formParty, newHero, post, type Hero } from './partyHelpers';

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

describe('xpFactor와 하드 격차(순수)', () => {
  it('기본 정책: d 6~11은 0.85, 0.70, 0.55, 0.40, 0.25, 0.10, 12~14는 하한 0.02, d<=5는 1', () => {
    buildApp();
    const p = getConfig().field;
    expect([p.carrySlack, p.carryStep, p.carryMin, p.carryHardGap, p.carryHardDropMul, p.carryGoldScale]).toEqual([5, 0.15, 0.02, 15, 0, true]);
    const f = (d: number) => xpFactor(1 + d, 1, p);
    expect([5, 6, 7, 8, 9, 10, 11, 12, 14].map(f)).toEqual([1, 0.85, 0.7, 0.55, 0.4, 0.25, 0.1, 0.02, 0.02]);
    expect(f(0)).toBe(1);
    expect(isHardGap(15 + 1, 1, p)).toBe(true);
    expect(isHardGap(14 + 1, 1, p)).toBe(false);
    expect(isHardGap(40, 1, p)).toBe(true);
    expect(isHardGap(40, 40, p)).toBe(false);
  });

  it('rollKillDrops: goldMul은 기본 골드 더미에만(최소 1), 배율 0은 난수가 0이어도 재료·장비가 나오지 않는다', () => {
    const eco = getGameData().economy;
    const def = eco.monsters.get('skeleton') as NonNullable<ReturnType<typeof eco.monsters.get>>;
    const rng = fakeRng({ unit: 0, int: (min) => min });
    const gold = (mul: number) => rollKillDrops(eco, def, 'warrior', 0, rng, 1, 1, mul).find((d) => d.itemKey === 'gold')?.count;
    expect(gold(1)).toBe(8);
    expect(gold(0.5)).toBe(4);
    expect(gold(0.02)).toBe(1);
    expect(gold(0)).toBe(1);
    const hard = rollKillDrops(eco, def, 'warrior', 0, rng, 0, 1, 0);
    expect(hard).toEqual([{ itemKey: 'gold', count: 1 }]);
    expect(rollKillDrops(eco, def, 'warrior', 0, rng, 1, 1, 1).length).toBeGreaterThan(1);
  });
});

/** forest 스폰 레벨을 바꾼 데이터로 앱을 만든다(고정 경험치는 지워 레벨 공식으로 본다) */
function appWithLevel(level: number, env: Record<string, string> = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-'));
  for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
  const maps = JSON.parse(fs.readFileSync(path.join(dir, 'maps.json'), 'utf8')) as { maps: { id: string; fieldSpawns: { level?: number; xp?: number }[] }[] };
  const spawn = (maps.maps.find((m) => m.id === 'forest') as { fieldSpawns: { level?: number; xp?: number }[] }).fieldSpawns[0] as { level?: number; xp?: number };
  spawn.level = level;
  delete spawn.xp;
  fs.writeFileSync(path.join(dir, 'maps.json'), JSON.stringify(maps));
  rehashDataDir(dir);
  return buildApp({ GAME_DATA_DIR: dir, ...env });
}

async function pair(a: ReturnType<typeof buildApp>, levelB: number): Promise<{ A: Hero; B: Hero; sid: string }> {
  const [A, B] = [await newHero(a), await newHero(a)];
  await formParty(a, A, [B]);
  if (levelB > 1) await seedLevel(B, levelB);
  const sid = (await post(a, A, '/field-sessions/enter', { map_id: 'forest' })).body.data.session.id as string;
  await post(a, B, '/field-sessions/enter', { map_id: 'forest' });
  return { A, B, sid };
}
const killAs = (a: ReturnType<typeof buildApp>, h: Hero, sid: string, ref = 1) => post(a, h, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: sid, monster_ref: ref });

describe('통합: 하드 격차와 골드 감쇠', () => {
  it('d >= 15: 경험치는 정확히 1, 재료·장비 드롭 없음, 기본 골드 1. 격차 14는 배율 0.02', async () => {
    setRng(fakeRng({ unit: 0, int: (min) => min }));
    const high = appWithLevel(40);
    const base = monsterXp(getGameData().economy, getGameData().economy.monsters.get('skeleton') as never, 40);
    const { A, B, sid } = await pair(high, 26); // A: d=39(하드), B: d=14(0.02)
    const ka = await killAs(high, A, sid);
    expect(ka.status).toBe(200);
    expect(ka.body.data.granted_xp).toBe(1);
    expect(ka.body.data.drops).toEqual([expect.objectContaining({ item_key: 'gold', count: 1 })]);
    expect(Number((await getPool().query('SELECT xp_factor, xp_granted FROM kill_log WHERE character_id = $1', [A.dbId])).rows[0]?.xp_factor)).toBe(0.02);
    expect((await getPool().query('SELECT xp_granted FROM kill_log WHERE character_id = $1', [A.dbId])).rows[0]?.xp_granted).toBe(1);
    const kb = await killAs(high, B, sid);
    expect(kb.body.data.field.xp_factor).toBe(0.02);
    expect(kb.body.data.granted_xp).toBe(Math.max(1, Math.round(base * 0.02)));
    // 하드 격차 25: 레벨 25 멤버(d=15)도 하드
    await seedLevel(B, 25);
    expect((await killAs(high, B, sid, 2)).body.data.granted_xp).toBe(1);
  });

  it('처치 골드도 감쇠한다(0.25배 -> 2, 최소 1). FIELD_CARRY_GOLD_SCALE=false면 이전과 같다. 솔로와 비슷한 레벨의 파티는 영향 없음', async () => {
    setRng(fakeRng({ unit: 1, int: (min) => min }));
    const high = appWithLevel(40);
    const { B, sid } = await pair(high, 30); // B: d=10 -> 0.25
    const kb = await killAs(high, B, sid);
    expect(kb.body.data.field.xp_factor).toBe(0.25);
    expect(kb.body.data.drops).toEqual([expect.objectContaining({ item_key: 'gold', count: 2 })]);
    await resetDb();
    const plain = appWithLevel(40, { FIELD_CARRY_GOLD_SCALE: 'false' });
    const p = await pair(plain, 30);
    const kp = await killAs(plain, p.B, p.sid);
    expect(kp.body.data.field.xp_factor).toBe(0.25);
    expect(kp.body.data.drops).toEqual([expect.objectContaining({ item_key: 'gold', count: 8 })]);
    // 혼자 사냥(세션 2인 미만)은 변하지 않는다
    await resetDb();
    const solo = await newHero(plain);
    const ks = await post(plain, solo, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect(ks.body.data.drops).toEqual([expect.objectContaining({ item_key: 'gold', count: 8 })]);
    expect(ks.body.data.field).toBeUndefined();
    // 격차 5 이하 파티는 배율 1
    await resetDb();
    const near = appWithLevel(6);
    const n = await pair(near, 1);
    const kn = await killAs(near, n.B, n.sid);
    expect(kn.body.data.field.xp_factor).toBe(1);
    expect(kn.body.data.drops).toEqual([expect.objectContaining({ item_key: 'gold', count: 8 })]);
    buildApp();
  });
});
