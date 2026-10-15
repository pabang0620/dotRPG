import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, fakeRng, get, goldOf, newHero, post, seedItem, type Hero } from './economyHelpers';

const app = buildApp();
const MONDAY = '2026-10-05T03:00:00Z'; // 월요일 12:00 KST
let fixed = new Date(MONDAY);
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeAll(resetDb);
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});
beforeEach(() => {
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});

const enter = (h: Hero, over: Record<string, unknown> = {}, rid?: string) =>
  post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0, ...over }, rid);

// gold_vein 일반: 방 구성(dungeons.json)
const ROOMS: { map: string; kills: [string, number][] }[] = [
  { map: 'dgn_canyon_1', kills: [['skel_gold', 3], ['skel_warrior', 2]] },
  { map: 'dgn_canyon_2', kills: [['skel_gold', 3], ['skel_miner', 2]] },
  { map: 'dgn_canyon_3', kills: [['skel_gold', 4], ['skel_warrior', 2]] },
  { map: 'dgn_canyon_boss', kills: [['boss_gold_foreman', 1], ['skel_gold', 2]] },
];

const killIn = (h: Hero, runId: string, room: number, monster: string, extra: Record<string, unknown> = {}) =>
  post(app, h, '/kills', { map_id: (ROOMS[room] as { map: string }).map, monster_id: monster, run_id: runId, room_index: room, ...extra });

async function clearRooms(h: Hero, runId: string, upTo = 3): Promise<void> {
  for (let room = 0; room <= upTo; room++) {
    for (const [monster, n] of (ROOMS[room] as { kills: [string, number][] }).kills) {
      for (let i = 0; i < n; i++) {
        advance(2);
        const res = await killIn(h, runId, room, monster);
        if (res.status !== 200) throw new Error(`kill failed ${res.status} ${JSON.stringify(res.body)}`);
      }
    }
  }
}

const result = (h: Hero, runId: string, body: Record<string, unknown>, rid?: string) =>
  post(app, h, `/dungeon-runs/${runId}/result`, body, rid);
const stats = (over: Record<string, unknown> = {}) => ({
  elapsed_ms: 80_000,
  hits_taken: 2,
  max_combo: 20,
  revives_used: 0,
  ...over,
});

async function playFullRun(h: Hero): Promise<string> {
  const run = await enter(h);
  const id = run.body.data.run.id as string;
  await clearRooms(h, id);
  advance(40);
  return id;
}

describe('GET /characters/:id/dungeons', () => {
  it('오늘 열린 던전, 입장 횟수, 난이도 해금을 서버 시계로 알려 준다', async () => {
    const h = await newHero(app);
    const res = await get(app, h, '/dungeons');
    expect(res.status).toBe(200);
    const d = res.body.data;
    expect(d.entries).toEqual({ limit: 3, used: 0, left: 3 });
    expect(d.reset.daily_start_at).toBe('2026-10-04T21:00:00.000Z');
    expect(d.active_run).toBeNull();
    const gold = d.dungeons.find((x: { id: string }) => x.id === 'gold_vein');
    expect(gold.open_today).toBe(true);
    expect(gold.difficulties[0]).toMatchObject({ difficulty: 0, unlocked: true, cleared: false });
    expect(gold.difficulties[1]).toMatchObject({ unlocked: true, cleared: false }); // 2026-10-14: 모든 난이도 개방
    expect(d.dungeons.find((x: { id: string }) => x.id === 'smelter').open_today).toBe(false);
    expect(d.dungeons.some((x: { id: string }) => x.id.startsWith('raid_'))).toBe(false);
  });
});

describe('POST /characters/:id/dungeon-runs', () => {
  it('정상: 201, 입장 횟수를 쓴다', async () => {
    const h = await newHero(app);
    const res = await enter(h);
    expect(res.status).toBe(201);
    expect(res.body.data.run).toMatchObject({ dungeon_id: 'gold_vein', difficulty: 0, party_size: 1 });
    expect(res.body.data.entries_left).toBe(2);
    const list = await get(app, h, '/dungeons');
    expect(list.body.data.entries.used).toBe(1);
    expect(list.body.data.active_run.id).toBe(res.body.data.run.id);
  });

  it('입력 오류와 규칙 오류: 모르는 던전, 레이드, 닫힌 요일, 잠긴 난이도', async () => {
    const h = await newHero(app);
    expect((await enter(h, { difficulty: 4 })).status).toBe(400);
    expect((await enter(h, { party_size: 4 })).status).toBe(400);
    expect((await enter(h, { dungeon_id: 'nope' })).body.errors.code).toBe('DUNGEON_UNKNOWN');
    // 4단계: 레이드 입장이 열렸다. 월요일에는 최종 레이드(일요일만)가 닫혀 있다
    expect((await enter(h, { dungeon_id: 'raid_grah' })).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    expect((await enter(h, { dungeon_id: 'smelter' })).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    // 2026-10-14: 요일·일반 던전은 앞 난이도 클리어 없이 열린다(레벨 조건만 본다)
    expect((await enter(h, { difficulty: 1 })).body.errors?.code).not.toBe('DIFFICULTY_LOCKED');
  });

  it('주말에는 요일 던전이 전부 열리고, 06:00 전에는 전날 요일이다', async () => {
    const h = await newHero(app);
    fixed = new Date('2026-10-10T03:00:00Z'); // 토요일 12:00 KST
    expect((await enter(h, { dungeon_id: 'smelter' })).status).toBe(201);
    const h2 = await newHero(app);
    fixed = new Date('2026-10-05T20:00:00Z'); // 화요일 05:00 KST: 아직 월요일
    expect((await enter(h2)).status).toBe(201);
    const h3 = await newHero(app);
    fixed = new Date('2026-10-05T21:00:00Z'); // 화요일 06:00 KST 정각: 화요일
    expect((await enter(h3)).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
  });

  it('하루 3번까지, 06:00 KST에 초기화(서버 시계)', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 3; i++) {
      const run = await enter(h);
      expect(run.status).toBe(201);
      await result(h, run.body.data.run.id, { outcome: 'failed', stats: stats() });
    }
    const no = await enter(h);
    expect(no.status).toBe(422);
    expect(no.body.errors.code).toBe('NO_ENTRIES_LEFT');
    // 다음 월요일 06:00 KST 이후에는 다시 3번
    fixed = new Date('2026-10-12T00:00:00Z');
    expect((await enter(h)).status).toBe(201);
  });

  it('2026-10-14 일반 던전: 매일 열리고 하루 입장 횟수를 쓰지 않는다(요일 던전 횟수가 0이어도 들어간다)', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 3; i++) {
      const run = await enter(h);
      await result(h, run.body.data.run.id, { outcome: 'failed', stats: stats() });
    }
    expect((await enter(h)).body.errors.code).toBe('NO_ENTRIES_LEFT');
    for (const id of ['normal_canyon', 'normal_forest', 'normal_winter', 'normal_canyon']) {
      const run = await enter(h, { dungeon_id: id });
      expect(run.status).toBe(201);
      await result(h, run.body.data.run.id, { outcome: 'failed', stats: stats() });
    }
    // 소탕은 일반 던전에 쓸 수 없다(입장 횟수가 없으므로)
    const sweep = await post(app, h, '/sweep/run', { dungeon_id: 'normal_canyon', difficulty: 0 });
    expect(sweep.status).not.toBe(200);
  });

  it('2026-10-14 요일 던전 초기화권: 다 쓴 입장 횟수를 되돌리고, 쓴 횟수가 없으면 소모 없이 거절', async () => {
    const h = await newHero(app);
    await seedItem(h, 'ticket_dungeon_reset', 2, 'bag', 'account');
    const use = () => post(app, h, '/items/use', { item_id: 'ticket_dungeon_reset' });
    const none = await use();
    expect(none.status).toBe(422);
    expect(none.body.errors.code).toBe('NOTHING_TO_RESET');
    expect(await countOf(h, 'ticket_dungeon_reset')).toBe(2);
    for (let i = 0; i < 3; i++) {
      const run = await enter(h);
      await result(h, run.body.data.run.id, { outcome: 'failed', stats: stats() });
    }
    expect((await enter(h)).body.errors.code).toBe('NO_ENTRIES_LEFT');
    fixed = new Date(fixed.getTime() + 1000);
    const ok = await use();
    expect(ok.status).toBe(200);
    expect(ok.body.data.entries_left).toBe(3);
    expect(await countOf(h, 'ticket_dungeon_reset')).toBe(1);
    fixed = new Date(fixed.getTime() + 1000);
    for (let i = 0; i < 3; i++) {
      const run = await enter(h);
      expect(run.status).toBe(201);
      await result(h, run.body.data.run.id, { outcome: 'failed', stats: stats() });
      fixed = new Date(fixed.getTime() + 1000);
    }
    expect((await enter(h)).body.errors.code).toBe('NO_ENTRIES_LEFT');
    await expectLedgerConsistent(h);
  });

  it('진행 중인 판이 있으면 409 RUN_ACTIVE, 오래 방치되면 닫고 입장(횟수는 이미 소진)', async () => {
    const h = await newHero(app);
    const first = await enter(h);
    const again = await enter(h);
    expect(again.status).toBe(409);
    expect(again.body.errors).toMatchObject({ code: 'RUN_ACTIVE', run_id: first.body.data.run.id });
    advance(3601);
    const next = await enter(h);
    expect(next.status).toBe(201);
    const old = await getPool().query('SELECT state FROM dungeon_runs WHERE uuid = $1', [first.body.data.run.id]);
    expect(old.rows[0].state).toBe('abandoned');
    expect((await get(app, h, '/dungeons')).body.data.entries.used).toBe(2);
  });

  it('재전송: 같은 request_id는 횟수를 한 번만 쓴다', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await enter(h, {}, rid);
    const b = await enter(h, {}, rid);
    expect(b.status).toBe(201);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect((await get(app, h, '/dungeons')).body.data.entries.used).toBe(1);
  });

  it('동시 요청: 동시에 입장해도 한 판만 진행되고 횟수는 한 번만 쓴다', async () => {
    const h = await newHero(app);
    const [a, b] = await Promise.all([enter(h), enter(h)]);
    expect([a.status, b.status].sort()).toEqual([201, 409]);
    expect((await get(app, h, '/dungeons')).body.data.entries.used).toBe(1);
  });
});

describe('던전 맥락의 처치 보고', () => {
  it('정상: 몬스터 레벨은 서버가 계산하고 방 진행이 기록된다', async () => {
    const h = await newHero(app);
    const run = await enter(h, { difficulty: 0 });
    const id = run.body.data.run.id as string;
    const res = await killIn(h, id, 0, 'skel_gold', { hits: 60 });
    expect(res.status).toBe(200);
    expect(res.body.data.granted_xp).toBe(30);
    // 황금 해골: 타격 골드는 서버 상한(ceil(HP 40 / 공격력 10) = 4)으로 자른다
    const log = await getPool().query('SELECT hits, context, monster_level FROM kill_log WHERE character_id = $1', [h.dbId]);
    expect(log.rows).toEqual([{ hits: 4, context: 'dungeon', monster_level: 1 }]);
    const golds = res.body.data.drops.filter((d: { item_key: string }) => d.item_key === 'gold');
    expect(golds.length).toBeGreaterThanOrEqual(4 + 2 + 1); // 타격 4 + 추가 골드 더미 2 이상 + 기본 1
  });

  it('거절: 다른 방의 맵, 방에 없는 몬스터, 방 순서, 그룹 마릿수 초과, 남의 판', async () => {
    const h = await newHero(app);
    const id = (await enter(h)).body.data.run.id as string;
    const wrongMap = await post(app, h, '/kills', { map_id: 'dgn_canyon_2', monster_id: 'skel_gold', run_id: id, room_index: 0 });
    expect(wrongMap.body.errors.code).toBe('KILL_REJECTED');
    expect((await killIn(h, id, 0, 'skel_miner')).body.errors.code).toBe('KILL_REJECTED'); // 방에 없는 몬스터
    expect((await killIn(h, id, 1, 'skel_gold')).body.errors.code).toBe('KILL_REJECTED'); // 앞 방이 정리되지 않았다
    expect((await killIn(h, id, 3, 'boss_gold_foreman')).body.errors.code).toBe('KILL_REJECTED');
    for (let i = 0; i < 2; i++) {
      advance(2);
      expect((await killIn(h, id, 0, 'skel_warrior')).status).toBe(200);
    }
    advance(2);
    const over = await killIn(h, id, 0, 'skel_warrior'); // 그룹 count 2 초과
    expect(over.body.errors.code).toBe('KILL_REJECTED');
    const kinds = await getPool().query('SELECT kind FROM anomaly_log WHERE character_id = $1 ORDER BY id', [h.dbId]);
    expect(kinds.rows.map((r) => r.kind)).toEqual(['kill_target', 'kill_target', 'kill_target', 'kill_target', 'kill_supply']);

    const other = await newHero(app);
    const foreign = await post(app, other, '/kills', { map_id: 'dgn_canyon_1', monster_id: 'skel_gold', run_id: id, room_index: 0 });
    expect(foreign.status).toBe(409);
    expect(foreign.body.errors.code).toBe('RUN_NOT_PLAYING');
  });

  it('끝난 판에는 처치를 보고할 수 없다(409 RUN_NOT_PLAYING)', async () => {
    const h = await newHero(app);
    const id = (await enter(h)).body.data.run.id as string;
    await result(h, id, { outcome: 'failed', stats: stats() });
    expect((await killIn(h, id, 0, 'skel_gold')).body.errors.code).toBe('RUN_NOT_PLAYING');
  });

  it('1초 창은 던전 6마리까지', async () => {
    const h = await newHero(app);
    const id = (await enter(h)).body.data.run.id as string;
    // 같은 시각에 5마리(방 0)까지는 되고 방 1은 열리지 않으므로 방 0의 5마리 + 한 마리 더는 마릿수에서 걸린다
    for (const m of ['skel_gold', 'skel_gold', 'skel_gold', 'skel_warrior', 'skel_warrior']) {
      expect((await killIn(h, id, 0, m)).status).toBe(200);
    }
    const next = await killIn(h, id, 1, 'skel_gold');
    expect(next.status).toBe(200); // 방 0이 정리됐으므로 다음 방 처치 허용(6번째)
    const seventh = await killIn(h, id, 1, 'skel_gold');
    expect(seventh.status).toBe(429);
    expect(seventh.body.errors.code).toBe('KILL_RATE_LIMITED');
  });
});

describe('POST /characters/:id/dungeon-runs/:run_id/result', () => {
  it('클리어: 서버가 랭크·경험치·카드를 정한다(점수 94 -> SS, 필드 대비 보정 1102 x 1.4 = 1543 + 처치 경험치)', async () => {
    const h = await newHero(app);
    const id = await playFullRun(h);
    const before = await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [h.dbId]);
    const res = await result(h, id, { outcome: 'cleared', stats: stats() });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({
      result: 'cleared',
      rank: 'SS',
      granted_xp: 1543,
      card_count: 4,
      score: { time: 40, hits: 24, kills: 10, combo: 20, revive_penalty: 0, total: 94 },
    });
    expect(JSON.stringify(res.body)).not.toMatch(/cards"/); // 선택 전에는 카드 내용을 주지 않는다
    const after = await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [h.dbId]);
    expect(after.rows[0]).not.toEqual(before.rows[0]);
    const xl = await getPool().query("SELECT delta FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_clear'", [h.dbId]);
    expect(xl.rows).toEqual([{ delta: 1543 }]);
    // 해금: 난이도 1이 열린다
    const list = await get(app, h, '/dungeons');
    const gold = list.body.data.dungeons.find((d: { id: string }) => d.id === 'gold_vein');
    expect(gold.difficulties[0]).toMatchObject({ cleared: true, best_rank: 1 });
    expect(gold.difficulties[1].unlocked).toBe(true);
  });

  it('시간 점수는 서버 시계 기준: 오래 걸린 판을 짧게 보고해도 시간 점수가 오르지 않는다', async () => {
    const h = await newHero(app);
    const id = await playFullRun(h);
    advance(600); // 실제로는 10분 넘게 걸렸다
    const res = await result(h, id, { outcome: 'cleared', stats: stats() }); // 80초라고 보고
    expect(res.body.data.result).toBe('cleared');
    expect(res.body.data.score.time).toBeLessThan(40);
    expect(res.body.data.rank).not.toBe('SS');
  });

  it('입력 오류: 랭크나 경험치를 보내면 400, 값 범위 초과도 400', async () => {
    const h = await newHero(app);
    const id = (await enter(h)).body.data.run.id as string;
    expect((await result(h, id, { outcome: 'cleared', rank: 'SSS', stats: stats() })).status).toBe(400);
    expect((await result(h, id, { outcome: 'cleared', stats: { ...stats(), granted_xp: 9 } })).status).toBe(400);
    expect((await result(h, id, { outcome: 'cleared', stats: stats({ hits_taken: -1 }) })).status).toBe(400);
    expect((await result(h, id, { outcome: 'won', stats: stats() })).status).toBe(400);
    expect((await result(h, 'not-a-uuid', { outcome: 'failed', stats: stats() })).status).toBe(400);
  });

  it('실패 보고: 보상 없이 닫고, 이미 받은 처치 경험치는 유지', async () => {
    const h = await newHero(app);
    const id = (await enter(h)).body.data.run.id as string;
    await killIn(h, id, 0, 'skel_gold');
    const res = await result(h, id, { outcome: 'failed', stats: stats() });
    expect(res.body.data).toEqual({ result: 'failed' });
    const c = await getPool().query('SELECT xp FROM characters WHERE id = $1', [h.dbId]);
    expect(c.rows[0].xp).toBe(30);
    const again = await result(h, id, { outcome: 'failed', stats: stats() });
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('RUN_NOT_PLAYING');
  });

  it('2026-10-14: 운영자 보류는 없다. 구조 위반·불가능한 화력만 보상 없이 닫고(rejected), 빠른 클리어·값 이상은 기록만 하고 정산한다', async () => {
    const closed = async (run: string) =>
      (await getPool().query('SELECT state, hold_reason, xp_granted FROM dungeon_runs WHERE uuid = $1', [run])).rows[0];
    // 1) 방을 돌지 않고 클리어 보고: 보상 없이 닫힌다(held 아님)
    const a = await newHero(app);
    const idA = (await enter(a)).body.data.run.id as string;
    advance(100);
    const resA = await result(a, idA, { outcome: 'cleared', stats: stats() });
    expect(resA.body.data.result).toBe('rejected');
    expect((await closed(idA)).state).toBe('failed');
    expect((await closed(idA)).hold_reason).toContain('BOSS_ROOM_NOT_REACHED');
    expect((await getPool().query("SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_clear'", [a.dbId])).rows[0].n).toBe(0);
    expect((await getPool().query("SELECT kind FROM anomaly_log WHERE character_id = $1 AND kind = 'dungeon_result'", [a.dbId])).rows).toHaveLength(1);

    // 2) 정상 진행 후 아주 빠른 보고(쩔·고레벨): 정산된다, 기록만 남는다
    const b = await newHero(app);
    const idB = await playFullRun(b);
    const resB = await result(b, idB, { outcome: 'cleared', stats: stats({ elapsed_ms: 8_000 }) });
    expect(resB.body.data.result).toBe('cleared');
    expect((await closed(idB)).state).toBe('cleared');
    const soft = await getPool().query("SELECT detail FROM anomaly_log WHERE character_id = $1 AND kind = 'dungeon_result'", [b.dbId]);
    expect(JSON.stringify(soft.rows)).toContain('TOO_FAST');

    // 3) 서버 경과보다 길게 보고: 정산된다(시간 점수는 서버 기준)
    const c = await newHero(app);
    const idC = await playFullRun(c);
    expect((await result(c, idC, { outcome: 'cleared', stats: stats({ elapsed_ms: 900_000 }) })).body.data.result).toBe('cleared');

    // 4) 부활 초과: 정산하되 난이도 한도로 자른다
    const d = await newHero(app);
    const idD = await playFullRun(d);
    expect((await result(d, idD, { outcome: 'cleared', stats: stats({ revives_used: 6 }) })).body.data.result).toBe('cleared');

    // 5) 파티 화력 상한으로 불가능한 속도: 보상 없이 닫고 보류 목록에는 넣지 않는다
    const e = await newHero(app);
    const idE = await playFullRun(e);
    await getPool().query('UPDATE dungeon_runs SET power_cap = 0.001 WHERE uuid = $1', [idE]);
    const resE = await result(e, idE, { outcome: 'cleared', stats: stats() });
    expect(resE.body.data.result).toBe('rejected');
    expect((await closed(idE)).hold_reason).toContain('POWER');
    expect((await getPool().query("SELECT count(*)::int AS n FROM dungeon_runs WHERE state = 'held'")).rows[0].n).toBe(0);

    // 6) 쩔(화력이 큰 파티 상한)으로 아주 빠른 클리어: 통과
    const f = await newHero(app);
    const idF = await playFullRun(f);
    await getPool().query('UPDATE dungeon_runs SET power_cap = 1000000 WHERE uuid = $1', [idF]);
    expect((await result(f, idF, { outcome: 'cleared', stats: stats({ elapsed_ms: 5_000 }) })).body.data.result).toBe('cleared');
  });

  it('재전송: 같은 request_id는 같은 응답이고 경험치는 한 번만', async () => {
    const h = await newHero(app);
    const id = await playFullRun(h);
    const rid = randomUUID();
    const a = await result(h, id, { outcome: 'cleared', stats: stats() }, rid);
    const b = await result(h, id, { outcome: 'cleared', stats: stats() }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    const n = await getPool().query("SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_clear'", [h.dbId]);
    expect(n.rows[0].n).toBe(1);
  });

  it('동시 요청: 같은 판의 결과를 동시에 보고해도 한 번만 처리된다', async () => {
    const h = await newHero(app);
    const id = await playFullRun(h);
    const [a, b] = await Promise.all([
      result(h, id, { outcome: 'cleared', stats: stats() }),
      result(h, id, { outcome: 'cleared', stats: stats() }),
    ]);
    expect([a.status, b.status].sort()).toEqual([200, 409]);
    const n = await getPool().query("SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_clear'", [h.dbId]);
    expect(n.rows[0].n).toBe(1);
  });
});

describe('POST /characters/:id/dungeon-runs/:run_id/cards/pick', () => {
  async function cleared(): Promise<{ h: Hero; id: string }> {
    const h = await newHero(app);
    const id = await playFullRun(h);
    await result(h, id, { outcome: 'cleared', stats: stats() });
    return { h, id };
  }
  const pick = (h: Hero, id: string, index: number, rid?: string) =>
    post(app, h, `/dungeon-runs/${id}/cards/pick`, { index }, rid);

  it('정상: 고른 카드만 지급하고 4장을 모두 공개한다', async () => {
    // 카드 굴림: 가중치 첫 항목(골드 300..600)이 4번 나오도록 int는 최솟값
    const { h, id } = await cleared();
    const res = await pick(h, id, 2);
    expect(res.status).toBe(200);
    expect(res.body.data.cards).toHaveLength(4);
    const card = res.body.data.card as { item_key: string; count: number };
    expect(card).toEqual(res.body.data.cards[2]);
    if (card.item_key === 'gold') expect(await goldOf(h)).toBe(100 + card.count);
    else expect(await countOf(h, card.item_key)).toBeGreaterThanOrEqual(card.count);
    await expectLedgerConsistent(h);
  });

  it('2026-10-14 다중 보상: 확정 재료·확정 골드·운 카드 2장을 모두 받는다. 같은 카드는 한 번만', async () => {
    const { h, id } = await cleared();
    const first = await pick(h, id, 0);
    expect(first.status).toBe(200);
    expect((first.body.data.card as { item_key: string }).item_key).toBe('mat_bone'); // 황금 광맥 확정 재료
    const gold = await pick(h, id, 1);
    expect((gold.body.data.card as { item_key: string; count: number }).item_key).toBe('gold');
    expect(await goldOf(h)).toBeGreaterThanOrEqual(100 + (gold.body.data.card as { count: number }).count);
    expect((await pick(h, id, 2)).status).toBe(200);
    const last = await pick(h, id, 3);
    expect(last.status).toBe(200);
    expect(last.body.data.remaining).toBe(0);
    const again = await pick(h, id, 1);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('CARD_ALREADY_FLIPPED');
    await expectLedgerConsistent(h);
  });

  it('입력 오류: 범위 밖 번호, 카드가 없는 판, 남의 판', async () => {
    const { h, id } = await cleared();
    expect((await pick(h, id, 4)).status).toBe(400);
    expect((await post(app, h, `/dungeon-runs/${id}/cards/pick`, { index: 0, item_key: 'x' })).status).toBe(400);
    const failedRun = (await enter(h, { dungeon_id: 'gold_vein' })).body.data.run.id as string;
    expect((await pick(h, failedRun, 0)).body.errors.code).toBe('NO_CARDS'); // 진행 중이라 카드가 없다
    const other = await newHero(app);
    const foreign = await pick(other, id, 0);
    expect(foreign.status).toBe(404);
  });

  it('선택 시간(24시간)이 지나면 410 CARDS_EXPIRED', async () => {
    const { h, id } = await cleared();
    advance(25 * 3600);
    const res = await pick(h, id, 0);
    expect(res.status).toBe(410);
    expect(res.body.errors.code).toBe('CARDS_EXPIRED');
  });

  it('재전송과 동시 요청: 카드를 두 장 받지 못한다', async () => {
    const { h, id } = await cleared();
    const rid = randomUUID();
    const a = await pick(h, id, 0, rid);
    const b = await pick(h, id, 0, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);

    // 같은 카드를 동시에 두 번 뒤집어도 한 번만 받는다
    const c = await cleared();
    const [x, y] = await Promise.all([pick(c.h, c.id, 0), pick(c.h, c.id, 0)]);
    expect([x.status, y.status].sort()).toEqual([200, 409]);
    await expectLedgerConsistent(c.h);
  });
});
