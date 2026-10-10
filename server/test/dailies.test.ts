// 일일·주간 의뢰(Docs/server/phase14_daily_quests.md): 해금, 결정적 칸, 수락 멱등, 맵 기준 진행, 청구 1회, 이월과 만료, 만렙 골드, 주간 집계와 주 경계.
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { buildApp, resetDb, shutdown } from './helpers';
import { expectLedgerConsistent, get, goldOf, newHero, post, seedClaims, seedLevel, type Hero } from './economyHelpers';

// 2026-10-05 월요일 12:00 KST = 게임 날짜 2026-10-05
const T0 = Date.parse('2026-10-05T03:00:00Z');
const DAY = 24 * 60 * 60 * 1000;
let clock = T0;

let app: Express;
beforeEach(async () => {
  clock = T0;
  setClockOverride(() => new Date(clock));
  await resetDb();
  app = buildApp();
});
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

interface Slot {
  day: string;
  slot: number;
  template_id: string;
  type: string;
  map: string | null;
  monster: string | null;
  count: number;
  progress: number;
  state: string;
  reward: { xp: number; gold: number };
}

const list = async (h: Hero) => {
  const r = await get(app, h, '/dailies');
  expect(r.status).toBe(200);
  return r.body.data as {
    unlocked: boolean;
    level: number;
    game_day: string;
    next_reset_at: string;
    giver: string;
    slots: Slot[];
    weekly: { week_start: string; next_reset_at: string; slots: Slot[] };
  };
};
const accept = (h: Hero, day: string, slot: number, rid?: string) => post(app, h, '/dailies/accept', { day, slot }, rid);
const claim = (h: Hero, day: string, slot: number, rid?: string) => post(app, h, '/dailies/claim', { day, slot }, rid);
const acceptW = (h: Hero, day: string, slot: number) => post(app, h, '/dailies/accept', { day, slot, period: 'week' });
const claimW = (h: Hero, day: string, slot: number) => post(app, h, '/dailies/claim', { day, slot, period: 'week' });

async function unlockedHero(level: number): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, level);
  await seedClaims(h, ['c1_stronger']);
  return h;
}

async function addKills(h: Hero, map: string, monster: string, n: number, at: number): Promise<void> {
  for (let i = 0; i < n; i++) {
    await getPool().query(
      `INSERT INTO kill_log (character_id, map_id, monster_id, monster_level, context, xp_granted, request_id, created_at)
       VALUES ($1, $2, $3, 20, 'field', 0, $4, $5)`,
      [h.dbId, map, monster, randomUUID(), new Date(at)],
    );
  }
}

async function addDungeonClear(h: Hero, at: number, dungeonId?: string): Promise<void> {
  const eco = getGameData().economy;
  const id = dungeonId ?? [...eco.dungeons.byId.values()].find((d) => !d.isRaid)!.id;
  await getPool().query(
    `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, started_at, ended_at, rank, cards)
     VALUES ($1, $2, 0, 'cleared', $3, $3, $3, 0, '[]'::jsonb)`,
    [h.dbId, id, new Date(at)],
  );
}

const xpToNext = (level: number) => getGameData().economy.progression.xpToNext[level - 1] as number;
const ledger = async (h: Hero, table: 'xp_ledger' | 'gold_ledger') =>
  Number((await getPool().query<{ n: string }>(`SELECT count(*) AS n FROM ${table} WHERE character_id = $1 AND reason = 'daily_quest'`, [h.dbId])).rows[0]!.n);

describe('일일 의뢰', () => {
  it('해금 퀘스트(1-19) 전에는 잠김: 빈 목록, 수락 403', async () => {
    const h = await newHero(app);
    await seedLevel(h, 20);
    const v = await list(h);
    expect(v).toMatchObject({ unlocked: false, slots: [], game_day: '2026-10-05' });
    const r = await accept(h, '2026-10-05', 0);
    expect(r.status).toBe(403);
    expect(r.body.errors.code).toBe('DAILY_LOCKED');
  });

  it('목록은 결정적: 오늘 3칸 + 전날 3칸, 몇 번 조회해도 같다', async () => {
    const h = await unlockedHero(20);
    const a = await list(h);
    const b = await list(h);
    expect(a.unlocked).toBe(true);
    expect(a.slots).toEqual(b.slots);
    expect(a.slots.map((s) => s.day)).toEqual(['2026-10-04', '2026-10-04', '2026-10-04', '2026-10-05', '2026-10-05', '2026-10-05']);
    expect(a.next_reset_at).toBe('2026-10-05T21:00:00.000Z');
    // Lv20: 쌍벽 관문(24)은 후보가 아니다 -> 능선 둘 + 던전 하나
    const today = a.slots.filter((s) => s.day === '2026-10-05');
    expect(new Set(today.map((s) => s.template_id))).toEqual(new Set(['d_ridge_knights', 'd_ridge_archers', 'd_canyon_dungeon']));
    for (const s of a.slots) {
      expect(s).toMatchObject({ state: 'offered', progress: 0, reward: { xp: Math.round(0.05 * xpToNext(20)), gold: 600 } });
    }
  });

  it('수락은 멱등: 같은 request_id는 재생, 다른 request_id도 행 하나', async () => {
    const h = await unlockedHero(20);
    const rid = randomUUID();
    const r1 = await accept(h, '2026-10-05', 0, rid);
    expect(r1.status).toBe(200);
    const r2 = await accept(h, '2026-10-05', 0, rid);
    expect(r2.status).toBe(200);
    expect(r2.body.data).toEqual(r1.body.data);
    // 레벨이 바뀌어도 수락한 칸의 의뢰는 그대로
    const before = r1.body.data.slots.find((s: Slot) => s.day === '2026-10-05' && s.slot === 0) as Slot;
    await seedLevel(h, 30);
    const r3 = await accept(h, '2026-10-05', 0);
    expect(r3.status).toBe(200);
    const after = r3.body.data.slots.find((s: Slot) => s.day === '2026-10-05' && s.slot === 0) as Slot;
    expect(after.template_id).toBe(before.template_id);
    expect(after.state).toBe('active');
    const n = await getPool().query('SELECT count(*)::int AS n FROM daily_quests WHERE character_id = $1', [h.dbId]);
    expect(n.rows[0].n).toBe(1);
  });

  it('잘못된 칸 422, 내일 날짜와 이틀 전은 DAILY_EXPIRED', async () => {
    const h = await unlockedHero(20);
    expect((await accept(h, '2026-10-05', 3)).body.errors.code).toBe('DAILY_SLOT_INVALID');
    expect((await accept(h, '2026-10-06', 0)).body.errors.code).toBe('DAILY_EXPIRED');
    expect((await accept(h, '2026-10-03', 0)).body.errors.code).toBe('DAILY_EXPIRED');
    expect((await claim(h, '2026-10-05', 0)).body.errors.code).toBe('DAILY_NOT_ACCEPTED');
  });

  it('처치 진행은 수락 이후 의뢰 맵의 그 몬스터만 센다', async () => {
    const h = await unlockedHero(20);
    const v = await list(h);
    const s = v.slots.find((x) => x.day === '2026-10-05' && x.type === 'kill')!;
    expect((await accept(h, s.day, s.slot)).status).toBe(200);
    await addKills(h, s.map!, s.monster!, 5, T0 - 1000); // 수락 전
    await addKills(h, 'forest_crossing', s.monster!, 5, T0 + 1000); // 같은 몬스터, 다른 맵
    await addKills(h, s.map!, s.monster!, 7, T0 + 2000);
    const now = (await list(h)).slots.find((x) => x.day === s.day && x.slot === s.slot)!;
    expect(now).toMatchObject({ state: 'active', progress: 7 });
  });

  it('미완료 청구 422, 완료 청구는 경험치·골드 1회, 재생은 같은 응답, 다시 청구는 409', async () => {
    const h = await unlockedHero(20);
    const s = (await list(h)).slots.find((x) => x.day === '2026-10-05' && x.type === 'kill')!;
    await accept(h, s.day, s.slot);
    await addKills(h, s.map!, s.monster!, s.count - 1, T0 + 1000);
    const notDone = await claim(h, s.day, s.slot);
    expect(notDone.status).toBe(422);
    expect(notDone.body.errors.code).toBe('DAILY_NOT_DONE');

    await addKills(h, s.map!, s.monster!, 1, T0 + 2000);
    const gold0 = await goldOf(h);
    const rid = randomUUID();
    const r = await claim(h, s.day, s.slot, rid);
    expect(r.status).toBe(200);
    const xp = Math.round(0.05 * xpToNext(20));
    expect(r.body.data.granted).toEqual({ xp, gold: 600 });
    expect(r.body.data).toMatchObject({ level: 20, xp, gold: gold0 + 600 });
    expect(r.body.data.delta).toMatchObject({ gold: gold0 + 600, level: 20, xp });
    expect(r.body.data.slots.find((x: Slot) => x.day === s.day && x.slot === s.slot)).toMatchObject({ state: 'claimed', progress: s.count });

    const replay = await claim(h, s.day, s.slot, rid);
    expect(replay.status).toBe(200);
    expect(replay.body.data).toEqual(r.body.data);
    const again = await claim(h, s.day, s.slot);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('DAILY_ALREADY_CLAIMED');

    expect(await goldOf(h)).toBe(gold0 + 600);
    expect(await ledger(h, 'xp_ledger')).toBe(1);
    expect(await ledger(h, 'gold_ledger')).toBe(1);
    const row = await getPool().query('SELECT reward FROM daily_quests WHERE character_id = $1', [h.dbId]);
    expect(row.rows[0].reward).toEqual({ xp, gold: 600, level: 20 });
    await expectLedgerConsistent(h);
  });

  it('던전 의뢰: 수락 이후 요일 던전 클리어를 센다', async () => {
    const h = await unlockedHero(20);
    const s = (await list(h)).slots.find((x) => x.day === '2026-10-05' && x.type === 'dungeon')!;
    await accept(h, s.day, s.slot);
    await addDungeonClear(h, T0 - 1000);
    expect((await claim(h, s.day, s.slot)).body.errors.code).toBe('DAILY_NOT_DONE');
    await addDungeonClear(h, T0 + 1000);
    expect((await claim(h, s.day, s.slot)).status).toBe(200);
  });

  it('전날 칸은 하루 이월, 이틀 지나면 사라진다', async () => {
    const h = await unlockedHero(20);
    const s = (await list(h)).slots.find((x) => x.day === '2026-10-05' && x.type === 'kill')!;
    await accept(h, s.day, s.slot);

    clock = T0 + DAY; // 다음 게임 날짜
    const v1 = await list(h);
    expect(v1.game_day).toBe('2026-10-06');
    const carried = v1.slots.find((x) => x.day === '2026-10-05' && x.slot === s.slot)!;
    expect(carried).toMatchObject({ template_id: s.template_id, state: 'active' });
    expect(v1.slots.filter((x) => x.day === '2026-10-05')).toHaveLength(3);
    await addKills(h, s.map!, s.monster!, s.count, clock);
    // 어제 끝낸 칸은 오늘 목록에서 빠진다
    expect((await claim(h, s.day, s.slot)).status).toBe(200);
    expect((await list(h)).slots.some((x) => x.day === '2026-10-05' && x.slot === s.slot)).toBe(false);

    const h2 = await unlockedHero(20);
    clock = T0;
    await accept(h2, '2026-10-05', 0);
    clock = T0 + 2 * DAY;
    const v2 = await list(h2);
    expect(v2.slots.some((x) => x.day === '2026-10-05')).toBe(false);
    expect((await claim(h2, '2026-10-05', 0)).body.errors.code).toBe('DAILY_EXPIRED');
  });

  it('만렙(Lv40)은 경험치 대신 maxLevelGold', async () => {
    const h = await unlockedHero(40);
    const v = await list(h);
    expect(v.giver).toBe('눈꽃 마을 의뢰 게시판');
    const s = v.slots.find((x) => x.day === '2026-10-05' && x.type === 'kill')!;
    expect(s.reward).toEqual({ xp: 0, gold: 3000 });
    await accept(h, s.day, s.slot);
    await addKills(h, s.map!, s.monster!, s.count, T0 + 1000);
    const gold0 = await goldOf(h);
    const r = await claim(h, s.day, s.slot);
    expect(r.status).toBe(200);
    expect(r.body.data.granted).toEqual({ xp: 0, gold: 3000 });
    expect(await goldOf(h)).toBe(gold0 + 3000);
    expect(await ledger(h, 'xp_ledger')).toBe(0);
    await expectLedgerConsistent(h);
  });
});

// 주간 칸은 캐릭터마다 4개 후보 중 3개가 뽑힌다. 원하는 형식이 나올 때까지 캐릭터를 만든다
async function heroWithWeekly(type: string): Promise<{ h: Hero; s: Slot }> {
  for (let i = 0; i < 20; i++) {
    const h = await unlockedHero(20);
    const s = (await list(h)).weekly.slots.find((x) => x.type === type);
    if (s) return { h, s };
  }
  throw new Error(`주간 ${type} 칸이 나오지 않습니다`);
}

describe('주간 의뢰', () => {
  it('주 3칸, 주 시작(목요일) 날짜, 보상 15%와 구간 골드, 잠김이면 빈 목록', async () => {
    const locked = await newHero(app);
    await seedLevel(locked, 20);
    expect((await list(locked)).weekly).toEqual({ week_start: '2026-10-01', next_reset_at: '2026-10-07T21:00:00.000Z', slots: [] });

    const h = await unlockedHero(20);
    const w = (await list(h)).weekly;
    expect(w.week_start).toBe('2026-10-01');
    expect(w.slots).toHaveLength(3);
    for (const s of w.slots) {
      expect(s).toMatchObject({ period: 'week', day: '2026-10-01', state: 'offered', reward: { xp: Math.round(0.15 * xpToNext(20)), gold: 2500 } });
    }
    expect(new Set(w.slots.map((s) => s.template_id)).size).toBe(3);
  });

  it('레이드 의뢰: 수락 이후 해골왕 클리어만 센다, 청구하면 주간 보상', async () => {
    const { h, s } = await heroWithWeekly('raid');
    expect((await acceptW(h, s.day, s.slot)).status).toBe(200);
    await addDungeonClear(h, T0 - 1000, 'raid_skeleton_king');
    await addDungeonClear(h, T0 + 1000); // 요일 던전은 레이드가 아니다
    expect((await claimW(h, s.day, s.slot)).body.errors.code).toBe('DAILY_NOT_DONE');
    await addDungeonClear(h, T0 + 2000, 'raid_skeleton_king');
    const gold0 = await goldOf(h);
    const r = await claimW(h, s.day, s.slot);
    expect(r.status).toBe(200);
    expect(r.body.data.granted).toEqual({ xp: Math.round(0.15 * xpToNext(20)), gold: 2500 });
    expect(await goldOf(h)).toBe(gold0 + 2500);
    expect(r.body.data.weekly.slots.find((x: Slot) => x.slot === s.slot).state).toBe('claimed');
    expect((await claimW(h, s.day, s.slot)).status).toBe(409);
    await expectLedgerConsistent(h);
  });

  it('일일 완료 의뢰: 수락 이후 청구한 일일 칸 수를 센다', async () => {
    const { h, s } = await heroWithWeekly('daily');
    await acceptW(h, s.day, s.slot);
    const seedDaily = async (n: number, at: number, from: number) => {
      for (let i = 0; i < n; i++) {
        await getPool().query(
          `INSERT INTO daily_quests (character_id, period, game_day, slot, template_id, accepted_at, claimed_at, reward)
           VALUES ($1, 'day', $2::date, $3, 'd_ridge_knights', $4, $4, '{}'::jsonb)`,
          [h.dbId, `2026-09-${String(10 + from + i).padStart(2, '0')}`, 0, new Date(at)],
        );
      }
    };
    await seedDaily(3, T0 - 1000, 0); // 수락 전 청구분은 세지 않는다
    await seedDaily(s.count - 1, T0 + 1000, 3);
    const v = (await list(h)).weekly.slots.find((x) => x.slot === s.slot)!;
    expect(v).toMatchObject({ state: 'active', progress: s.count - 1 });
    expect((await claimW(h, s.day, s.slot)).body.errors.code).toBe('DAILY_NOT_DONE');

    // 실제 일일 청구 하나로 채운다
    const d = (await list(h)).slots.find((x) => x.day === '2026-10-05' && x.type === 'kill')!;
    await accept(h, d.day, d.slot);
    await addKills(h, d.map!, d.monster!, d.count, T0 + 2000);
    expect((await claim(h, d.day, d.slot)).status).toBe(200);
    expect((await list(h)).weekly.slots.find((x) => x.slot === s.slot)).toMatchObject({ state: 'ready', progress: s.count });
    expect((await claimW(h, s.day, s.slot)).status).toBe(200);
  });

  it('주 경계(목요일 06:00 KST)를 넘으면 지난 주 칸은 사라지고 청구는 DAILY_EXPIRED', async () => {
    const h = await unlockedHero(20);
    const s = (await list(h)).weekly.slots[0]!;
    await acceptW(h, s.day, s.slot);
    clock = Date.parse('2026-10-07T20:59:59Z'); // 목요일 05:59:59 KST: 아직 같은 주
    expect((await list(h)).weekly.slots.find((x) => x.slot === s.slot)).toMatchObject({ day: '2026-10-01', state: 'active' });
    clock = Date.parse('2026-10-07T21:00:00Z'); // 목요일 06:00 KST: 새 주
    const w = (await list(h)).weekly;
    expect(w.week_start).toBe('2026-10-08');
    expect(w.slots.every((x) => x.day === '2026-10-08' && x.state === 'offered')).toBe(true);
    expect((await claimW(h, '2026-10-01', s.slot)).body.errors.code).toBe('DAILY_EXPIRED');
    expect((await acceptW(h, '2026-10-01', s.slot)).body.errors.code).toBe('DAILY_EXPIRED');
  });

  it('만렙 주간 보상은 weekly.maxLevelGold', async () => {
    const h = await unlockedHero(40);
    for (const s of (await list(h)).weekly.slots) expect(s.reward).toEqual({ xp: 0, gold: 10000 });
  });
});
