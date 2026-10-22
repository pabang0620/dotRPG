import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { setClockOverride } from '../src/utils/clock';
import { CREDIT_CAP_MS, killCredit, mainQuestLevelGate, tierOf, timeCharge, xpPerKill } from '../src/domains/minidungeon/miniDungeonRules';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { get, goldOf, newHero, post, seedClaims, seedLevel, type Hero } from './economyHelpers';

const app = buildApp({ PRESENCE_KILL_MODE: 'off' });
const START = '2026-10-14T03:00:00Z';
let now = new Date(START);
const advance = (seconds: number) => { now = new Date(now.getTime() + seconds * 1000); };
beforeAll(resetDb);
beforeEach(() => { now = new Date(START); setClockOverride(() => now); buildApp({ PRESENCE_KILL_MODE: 'off' }); });
afterAll(async () => { setClockOverride(null); await shutdown(); });

const enter = (h: Hero, tier = 10, requestId?: string) => post(app, h, '/mini-dungeon/enter', { tier }, requestId);
const kill = (h: Hero, id: string, monsterRef = randomUUID(), requestId?: string) => post(app, h, `/mini-dungeon/runs/${id}/kills`, { monster_ref: monsterRef }, requestId);
const leave = (h: Hero, id: string, reason = 'left', requestId?: string) => post(app, h, `/mini-dungeon/runs/${id}/leave`, { reason }, requestId);
async function hero(level = 10): Promise<Hero> { const h = await newHero(app); await seedLevel(h, level); return h; }
async function tradePair() {
  const a = await hero(); const b = await hero();
  const named = await getPool().query<{ id: string; name: string }>('SELECT id, name FROM characters WHERE id = ANY($1::bigint[])', [[a.dbId, b.dbId]]);
  for (const h of [a, b])
    await getPool().query(`INSERT INTO online_sessions (account_id, character_id, family_id, map_id, map_since, map_changed_at, last_seen_at)
      VALUES ((SELECT account_id FROM characters WHERE id = $1), $1, $2, 'village', $3, $3, $3)`, [h.dbId, randomUUID(), now]);
  return { a, b, bName: named.rows.find((c) => Number(c.id) === b.dbId)!.name };
}
async function entered(level = 10) {
  const h = await hero(level);
  const res = await enter(h, Math.floor(level / 10) * 10);
  expect(res.status).toBe(201);
  return { h, run: res.body.data.active_run };
}

describe('mini dungeon rules', () => {
  test.each([[1, null], [9, null], [10, 10], [19, 10], [20, 20], [29, 20], [30, 30], [39, 30], [40, null], [41, null]])('level %s selects %s', (level, tier) => {
    expect(tierOf(level as number, 40)).toBe(tier);
  });
  test('XP follows the actual progression table and time cannot be counted twice', () => {
    const bars = getGameData().economy.progression.xpToNext;
    // 2026-10-18 따라잡기: 평소 레벨 막대 / 8000, 메인 퀘스트가 레벨로 막혔을 때 / 800 (곡선 Lv16+ x2.2)
    expect([10, 20, 30].map((l) => xpPerKill(bars, l))).toEqual([1, 13, 41]);
    expect([10, 20, 30].map((l) => xpPerKill(bars, l, true))).toEqual([4, 128, 406]);
    const end = new Date(now.getTime() + 10_000);
    advance(11);
    const first = timeCharge(new Date(START), end, now);
    expect(first.ms).toBe(10_000);
    expect(first.expired).toBe(true);
    expect(timeCharge(first.until, end, now).ms).toBe(0);
    expect(timeCharge(first.until, end, new Date(START)).ms).toBe(0);
    expect(killCredit(0, new Date(START), now)).toBe(11_000);
    advance(1000);
    expect(killCredit(0, new Date(START), now)).toBe(CREDIT_CAP_MS);
  });
  test('catch-up applies only when every open main quest needs a higher level', () => {
    const q = (minLevel: number, levelNeed = 0, requires: string[] = [], requiresFlags: string[] = []) =>
      ({ kind: 'main', minLevel, requires, requiresFlags, objectives: { levelNeed } });
    const quests: [string, ReturnType<typeof q>][] = [['a', q(0)], ['b', q(12, 0, ['a'])], ['c', q(0, 15, ['a'])], ['d', q(0, 0, ['a'], ['flag'])]];
    const none = new Set<string>();
    expect(mainQuestLevelGate(quests, none, none, 1)).toBeNull(); // a is open
    expect(mainQuestLevelGate(quests, new Set(['a']), none, 11)).toBe(12); // d waits on a flag, not level -> ignored
    expect(mainQuestLevelGate(quests, new Set(['a']), new Set(['flag']), 11)).toBeNull(); // d is open
    expect(mainQuestLevelGate(quests.slice(0, 3), new Set(['a']), none, 11)).toBe(12);
    expect(mainQuestLevelGate(quests.slice(0, 3), new Set(['a']), none, 12)).toBeNull();
    expect(mainQuestLevelGate([['s', { ...q(30), kind: 'sub' }]], none, none, 1)).toBeNull();
  });
});

describe('daily XP-only mini dungeon HTTP', () => {
  test('status lists 10-level bands and the shared 30 minute reset', async () => {
    const h = await hero();
    const r = await get(app, h, '/mini-dungeon');
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ daily_limit_seconds: 1800, used_seconds: 0, remaining_seconds: 1800, active_run: null });
    expect(r.body.data.reset).toEqual({ daily_start_at: '2026-10-13T21:00:00.000Z', next_daily_at: '2026-10-14T21:00:00.000Z' });
    expect(r.body.data.tiers.map((t: { tier: number }) => t.tier)).toEqual([10, 20, 30]);
  });
  test.each([9, 40])('level %s cannot enter', async (level) => {
    const h = await hero(level);
    const r = await enter(h, level === 9 ? 10 : 30);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('MINI_LEVEL_LOCKED');
  });
  test('wrong band and arbitrary client reward data are rejected', async () => {
    const h = await hero(20);
    expect((await enter(h, 10)).body.errors.code).toBe('MINI_TIER_MISMATCH');
    expect((await post(app, h, '/mini-dungeon/enter', { tier: 20, xp: 999999 })).status).toBe(400);
  });
  test('entry replay is exact and preserves one run; a new entry cannot reset time', async () => {
    const h = await hero(); const requestId = randomUUID();
    const first = await enter(h, 10, requestId);
    advance(12.345);
    const replay = await enter(h, 10, requestId);
    expect(replay.headers['idempotent-replay']).toBe('true');
    expect(replay.body).toEqual(first.body);
    const resumed = await enter(h);
    expect(resumed.body.data.remaining_seconds).toBe(1787.655);
    expect(resumed.body.data.active_run.id).not.toBe(first.body.data.active_run.id);
    const db = await getPool().query("SELECT count(*)::int AS n FROM mini_dungeon_runs WHERE character_id = $1 AND state = 'playing'", [h.dbId]);
    expect(db.rows[0].n).toBe(1);
  });
  test('heartbeat and repeated leave settle server time once without XP', async () => {
    const { h, run } = await entered();
    advance(2.125);
    const requestId = randomUUID();
    const p = await post(app, h, `/mini-dungeon/runs/${run.id}/pulse`, {}, requestId);
    expect(p.body.data.used_seconds).toBe(2.125);
    expect(p.body.data.delta).toEqual({});
    advance(1);
    expect((await post(app, h, `/mini-dungeon/runs/${run.id}/pulse`, {}, requestId)).body).toEqual(p.body);
    expect((await leave(h, run.id)).body.data.used_seconds).toBe(3.125);
    advance(100);
    expect((await leave(h, run.id)).body.data.used_seconds).toBe(3.125);
    expect((await getPool().query('SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1', [h.dbId])).rows[0].n).toBe(0);
  });
  test('simultaneous duplicate death reports credit XP once', async () => {
    const { h, run } = await entered();
    advance(5);
    const ref = randomUUID();
    const results = await Promise.all([kill(h, run.id, ref), kill(h, run.id, ref)]);
    expect(results.map((r) => r.status).sort()).toEqual([200, 409]);
    const xp = await getPool().query('SELECT count(*)::int AS n, sum(delta)::int AS xp FROM xp_ledger WHERE character_id = $1', [h.dbId]);
    expect(xp.rows[0]).toEqual({ n: 1, xp: 1 });
  });
  test('an active party rejects solo entry', async () => {
    const h = await hero();
    const party = await post(app, h, '/parties', { dungeon_id: 'mana_graveyard', difficulty: 0, max_members: 4, min_power: 0, message: '', listed: false });
    expect(party.status).toBe(201);
    expect((await enter(h)).body.errors.code).toBe('MINI_SOLO_ONLY');
  });
  test('time remains paused outside and is shared across tiers, preserving milliseconds', async () => {
    const { h, run } = await entered();
    advance(120.25);
    const stopped = await leave(h, run.id);
    expect(stopped.body.data.remaining_seconds).toBe(1679.75);
    advance(500);
    await seedLevel(h, 20);
    const resumed = await enter(h, 20);
    expect(resumed.body.data.remaining_seconds).toBe(1679.75);
    advance(0.25);
    await leave(h, resumed.body.data.active_run.id);
    expect((await get(app, h, '/mini-dungeon')).body.data.used_seconds).toBe(120.5);
  });
  test.each(['death', 'disconnected'])('%s closes the active run without consuming later time', async (reason) => {
    const { h, run } = await entered();
    advance(10);
    expect((await leave(h, run.id, reason)).body.data.remaining_seconds).toBe(1790);
    advance(100);
    expect((await get(app, h, '/mini-dungeon')).body.data.remaining_seconds).toBe(1790);
    expect((await kill(h, run.id)).body.data.accepted).toBe(false);
  });
  test('no reported leave consumes server time after disconnect, without offline XP', async () => {
    const { h, run } = await entered();
    advance(1900);
    const res = await get(app, h, '/mini-dungeon');
    expect(res.body.data).toMatchObject({ remaining_seconds: 0, used_seconds: 1800, active_run: null });
    expect(res.body.data.completed_run).toMatchObject({ id: run.id, state: 'expired', kills: 0, xp_granted: 0 });
    expect((await enter(h)).body.errors.code).toBe('MINI_TIME_EXHAUSTED');
    const ended = await kill(h, run.id);
    expect(ended.body.data).toMatchObject({ accepted: false, granted_xp: 0 });
    const xp = await getPool().query('SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1', [h.dbId]);
    expect(xp.rows[0].n).toBe(0);
  });
  test('06:00 KST ends the old run; unused time is not carried into the new day', async () => {
    now = new Date('2026-10-14T20:59:50Z');
    const { h, run } = await entered();
    expect(run.ends_at).toBe('2026-10-14T21:00:00.000Z');
    advance(15);
    const r = await get(app, h, '/mini-dungeon');
    expect(r.body.data).toMatchObject({ remaining_seconds: 1800, used_seconds: 0, active_run: null });
    const day = await getPool().query('SELECT used_ms FROM mini_dungeon_days WHERE character_id = $1 ORDER BY reset_day', [h.dbId]);
    expect(day.rows.map((d) => d.used_ms)).toEqual([10_000, 0]);
    expect((await enter(h)).status).toBe(201);
    expect((await kill(h, run.id)).body.data.accepted).toBe(false);
  });
  test('one combat kill grants XP and the fixed kill gold, with no quest stats, item/drop or dungeon progress', async () => {
    const { h, run } = await entered(20);
    const oldGold = await goldOf(h);
    const oldItems = await getPool().query('SELECT item_key, count, location FROM character_items WHERE character_id = $1 ORDER BY id', [h.dbId]);
    advance(5);
    const r = await kill(h, run.id);
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ accepted: true, granted_xp: 13, granted_gold: 36, catch_up_applied: false, delta: { level: 20, xp: 13, gold: oldGold + 36 } });
    expect(r.body.data.active_run).toMatchObject({ kills: 1, xp_granted: 13 });
    expect(r.body.data.delta.stacks).toBeUndefined();
    expect(await goldOf(h)).toBe(oldGold + 36);
    const ledger = await getPool().query("SELECT reason, delta FROM gold_ledger WHERE character_id = $1 AND ref LIKE 'mini:%'", [h.dbId]);
    expect(ledger.rows).toEqual([{ reason: 'drop_claim', delta: '36' }]);
    expect((await getPool().query('SELECT item_key, count, location FROM character_items WHERE character_id = $1 ORDER BY id', [h.dbId])).rows).toEqual(oldItems.rows);
    for (const table of ['drops', 'kill_stats', 'kill_log', 'dungeon_runs', 'quest_claims']) {
      expect((await getPool().query(`SELECT count(*)::int AS n FROM ${table} WHERE character_id = $1`, [h.dbId])).rows[0].n).toBe(0);
    }
    expect((await getPool().query('SELECT reason, delta FROM xp_ledger WHERE character_id = $1', [h.dbId])).rows).toEqual([{ reason: 'mini_dungeon', delta: 13 }]);
    // 처치 골드는 사냥터 골드 더미처럼 시간당 수입 계량에 들어가지만(drop_claim), 보류 기준(시간당 상한 x3)에는 한참 못 미친다
    expect((await getPool().query('SELECT count(*)::int AS n FROM economy_holds WHERE character_id = $1', [h.dbId])).rows[0].n).toBe(0);
  });
  test('kill UUID and request replay prevent duplicate credit, mismatched replay is rejected', async () => {
    const { h, run } = await entered(); const ref = randomUUID(); const rid = randomUUID();
    advance(5);
    const first = await kill(h, run.id, ref, rid);
    advance(2);
    const replay = await kill(h, run.id, ref, rid);
    expect(replay.body).toEqual(first.body);
    expect(replay.headers['idempotent-replay']).toBe('true');
    expect((await kill(h, run.id, ref)).body.errors.code).toBe('KILL_DUPLICATE');
    expect((await kill(h, run.id, randomUUID(), rid)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    expect((await getPool().query('SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1', [h.dbId])).rows[0].n).toBe(1);
  });
  test('large legitimate mini XP stays in its own ledger and does not trigger field income holds', async () => {
    // Compress thirty minutes of server game time into a test, bypassing only real-time HTTP flood limits.
    buildApp({ PRESENCE_KILL_MODE: 'off', RATE_ECONOMY_CHAR_MAX: '100000' });
    const { h, run } = await entered(30);
    for (let i = 0; i < 360; i++) {
      advance(4.99);
      const r = await kill(h, run.id);
      expect(r.status).toBe(200);
      expect(r.body.data.granted_xp).toBe(41);
    }
    const sum = await getPool().query('SELECT reason, count(*)::int AS n, sum(delta)::int AS xp FROM xp_ledger WHERE character_id = $1 GROUP BY reason', [h.dbId]);
    expect(sum.rows).toEqual([{ reason: 'mini_dungeon', n: 360, xp: 360 * 41 }]);
    // 한 시간에 다 잡아도 처치 골드 360 x 36 = 12,960: 시간당 상한(8만 안팎) x3인 보류 기준에 닿지 않는다
    expect((await getPool().query("SELECT coalesce(sum(delta), 0)::int AS g FROM gold_ledger WHERE character_id = $1 AND ref LIKE 'mini:%'", [h.dbId])).rows[0].g).toBe(360 * 36);
    expect((await getPool().query('SELECT count(*)::int AS n FROM economy_holds WHERE character_id = $1', [h.dbId])).rows[0].n).toBe(0);
    advance(4);
    expect((await get(app, h, '/mini-dungeon')).body.data.remaining_seconds).toBe(0);
  });
  test('kill credits start empty, refill on server time, cap bursts and can retry the same request', async () => {
    const { h, run } = await entered(); const rid = randomUUID(); const ref = randomUUID();
    expect((await kill(h, run.id, ref, rid)).body.errors.code).toBe('MINI_KILL_RATE_LIMITED');
    advance(2);
    expect((await kill(h, run.id, ref, rid)).status).toBe(200);
    advance(100);
    for (let i = 0; i < 6; i++) expect((await kill(h, run.id)).status).toBe(200);
    expect((await kill(h, run.id)).status).toBe(429);
  });
  test('leveling to a new band keeps entry XP and leveling to cap ends the run', async () => {
    const { h, run } = await entered(19);
    await getPool().query('UPDATE characters SET xp = $2 WHERE id = $1', [h.dbId, getGameData().economy.progression.xpToNext[18]! - 1]);
    advance(5);
    const r = await kill(h, run.id);
    expect(r.body.data.delta.level).toBe(20);
    expect(r.body.data.active_run).toMatchObject({ tier: 10, entry_level: 19, xp_per_kill: run.xp_per_kill });
    await leave(h, run.id);
    await seedLevel(h, 39);
    const last = await enter(h, 30);
    await getPool().query('UPDATE characters SET xp = $2 WHERE id = $1', [h.dbId, getGameData().economy.progression.xpToNext[38]! - 1]);
    advance(5);
    const cap = await kill(h, last.body.data.active_run.id);
    expect(cap.body.data.delta).toMatchObject({ level: 40, xp: 0 });
    expect(cap.body.data.delta.gold).toBeGreaterThan(0); // the kill gold is paid even on the capping kill
    expect(cap.body.data.granted_xp).toBe(1);
    expect(cap.body.data.active_run).toBeNull();
    expect(cap.body.data.completed_run).toMatchObject({ id: last.body.data.active_run.id, state: 'level_cap', kills: 1, xp_granted: cap.body.data.granted_xp });
    expect((await enter(h, 30)).body.errors.code).toBe('MINI_LEVEL_LOCKED');
  });
  test('catch-up: while the next main quest waits on level, kills give 10x and stop once the level is reached', async () => {
    const h = await hero(19);
    // c1_stronger(레벨 20 조건)만 남은 상태
    await seedClaims(h, ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise', 'c1_rebuild', 'c1_trail', 'c1s_patrol', 'c1s_ruins',
      'c1s_firstdungeon', 'c1s_depths', 'c1_crossing', 'c2s_trader', 'c2s_trader_guard', 'c2s_trader_ore', 'c1_quarry', 'c1_scout', 'career_path']);
    const entry = await enter(h, 10);
    expect(entry.body.data.catch_up).toEqual({ level_gate: 20 });
    const run = entry.body.data.active_run;
    const bars = getGameData().economy.progression.xpToNext;
    expect(run).toMatchObject({ xp_per_kill: xpPerKill(bars, 19), catch_up_xp_per_kill: xpPerKill(bars, 19, true) });
    advance(5);
    const stuck = await kill(h, run.id);
    expect(stuck.body.data).toMatchObject({ accepted: true, catch_up_applied: true, granted_xp: xpPerKill(bars, 19, true) });
    await getPool().query('UPDATE characters SET xp = $2 WHERE id = $1', [h.dbId, bars[18]! - 1]);
    advance(2);
    const up = await kill(h, run.id);
    expect(up.body.data.delta.level).toBe(20);
    expect(up.body.data.catch_up).toEqual({ level_gate: null });
    advance(2);
    const after = await kill(h, run.id);
    expect(after.body.data).toMatchObject({ accepted: true, catch_up_applied: false, granted_xp: xpPerKill(bars, 19) });
  });
  test('regular reward reports and dungeon entry cannot bypass the XP-only mode', async () => {
    const { h } = await entered();
    expect((await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' })).body.errors.code).toBe('MINI_DUNGEON_ACTIVE');
    expect((await post(app, h, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 })).body.errors.code).toBe('MINI_DUNGEON_ACTIVE');
    expect((await post(app, h, '/parties', { dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, message: '', listed: false })).body.errors.code).toBe('MINI_DUNGEON_ACTIVE');
  });
  test('an existing dungeon cannot enter mini and another character cannot use the run', async () => {
    const h = await hero();
    const normal = await post(app, h, '/dungeon-runs', { dungeon_id: 'mana_graveyard', difficulty: 0 });
    expect(normal.status).toBe(201);
    expect((await enter(h)).body.errors.code).toBe('RUN_ACTIVE');
    const a = await entered(); const b = await hero();
    expect((await kill(b, a.run.id)).status).toBe(404);
    expect((await leave(b, a.run.id)).status).toBe(404);
  });
  test.each(['pending', 'active'])('an open %s trade blocks both participants from entering mini', async (state) => {
    const { a, b, bName } = await tradePair();
    const opened = await post(app, a, '/trades', { target_name: bName });
    expect(opened.status).toBe(201);
    const id = opened.body.data.trade.id as string;
    if (state === 'active') expect((await post(app, b, `/trades/${id}/accept`, {})).status).toBe(200);
    expect((await enter(a)).body.errors.code).toBe('TRADE_ALREADY_OPEN');
    expect((await enter(b)).body.errors.code).toBe('TRADE_ALREADY_OPEN');
    expect((await post(app, a, `/trades/${id}/cancel`, {})).status).toBe(200);
    expect((await enter(a)).status).toBe(201);
  });
  test.each(['requester', 'target'])('mini blocks trade creation when the %s is training', async (role) => {
    const { a, b, bName } = await tradePair();
    const trainer = role === 'requester' ? a : b;
    const started = await enter(trainer);
    expect(started.status).toBe(201);
    const blocked = await post(app, a, '/trades', { target_name: bName });
    expect(blocked.status).toBe(409);
    expect(blocked.body.errors.code).toBe('MINI_DUNGEON_ACTIVE');
    expect((await leave(trainer, started.body.data.active_run.id)).status).toBe(200);
    expect((await post(app, a, '/trades', { target_name: bName })).status).toBe(201);
  });
  test.each(['requester', 'target'])('racing mini entry against trade creation keeps the %s in only one mode', async (role) => {
    for (let attempt = 0; attempt < 3; attempt++) {
      const { a, b, bName } = await tradePair();
      const trainer = role === 'requester' ? a : b;
      const results = await Promise.all([enter(trainer), post(app, a, '/trades', { target_name: bName })]);
      expect(results.map((r) => r.status).sort()).toEqual([201, 409]);
      const states = await getPool().query<{ mini: number; trade: number }>(`SELECT
        (SELECT count(*)::int FROM mini_dungeon_runs WHERE character_id = $1 AND state = 'playing') AS mini,
        (SELECT count(*)::int FROM trades WHERE (requester_character_id = $1 OR target_character_id = $1) AND state IN ('pending','active')) AS trade`, [trainer.dbId]);
      expect(states.rows[0]!.mini + states.rows[0]!.trade).toBe(1);
      expect(states.rows[0]!.mini * states.rows[0]!.trade).toBe(0);
    }
  });
  test.each(['requester', 'target'])('an older pending invitation cannot be accepted while its %s is training', async (role) => {
    const { a, b } = await tradePair();
    expect((await enter(role === 'requester' ? a : b)).status).toBe(201);
    // Simulate a pending invitation retained by an older server, before the mutual-entry guard existed.
    const pending = await getPool().query<{ uuid: string }>(`INSERT INTO trades
      (requester_character_id, target_character_id, requester_account_id, target_account_id, expires_at)
      SELECT a.id, b.id, a.account_id, b.account_id, $3 FROM characters a, characters b
      WHERE a.id = $1 AND b.id = $2 RETURNING uuid`, [a.dbId, b.dbId, new Date(now.getTime() + 60_000)]);
    const id = pending.rows[0]!.uuid;
    const rejected = await post(app, b, `/trades/${id}/accept`, {});
    expect(rejected.status).toBe(409);
    expect(rejected.body.errors.code).toBe('MINI_DUNGEON_ACTIVE');
    // Cleanup remains available while training.
    expect((await post(app, b, `/trades/${id}/decline`, {})).status).toBe(200);
  });
  test('an expired trade invitation does not block mini entry', async () => {
    const { a, bName } = await tradePair();
    expect((await post(app, a, '/trades', { target_name: bName })).status).toBe(201);
    advance(61);
    expect((await enter(a)).status).toBe(201);
  });
  test('enforced presence must be fresh and on this mini map', async () => {
    buildApp({ PRESENCE_KILL_MODE: 'enforce' });
    const { h, run } = await entered();
    advance(3);
    expect((await kill(h, run.id)).body.errors.code).toBe('PRESENCE_REQUIRED');
    const beat = (map_id: string) => request(app).post(`/characters/${h.id}/presence`).set(auth(h.s)).send({ map_id, auto_play: true, input_recent: false });
    expect((await beat('forest')).status).toBe(200);
    expect((await kill(h, run.id)).status).toBe(422);
    expect((await beat('mini_dungeon_10')).status).toBe(200);
    expect((await kill(h, run.id)).status).toBe(200);
  });
});

// 챕터 3 5단계(Docs/ch3/LIVE_CONTENT.md 1절): 티어 40 잿빛 수련의 협곡(Lv40~49), 하루 4분(7단계에서 10분 → 4분), 막대/8,000·따라잡기/800
describe('chapter 3 mini dungeon tier 40', () => {
  const openApp = () => buildApp({ PRESENCE_KILL_MODE: 'off', CHAPTER3_OPEN: 'true' });
  afterEach(() => { buildApp({ PRESENCE_KILL_MODE: 'off' }); });

  test('rules: tier 40 selects only below the open cap, XP per kill follows the Lv40+ bars', () => {
    expect([[39, 30], [40, 40], [49, 40], [50, null]].map(([l]) => tierOf(l as number, 50))).toEqual([30, 40, 40, null]);
    expect(tierOf(40, 40)).toBeNull();
    const bars = getGameData().economy.progression.xpToNext;
    expect(xpPerKill(bars, 40)).toBe(Math.ceil(bars[39]! / 8000));
    expect(xpPerKill(bars, 40, true)).toBe(Math.ceil(bars[39]! / 800));
  });

  test('closed: tier 40 is locked and not listed, Lv40 stays without a mini dungeon', async () => {
    const h = await hero(40);
    const r = await enter(h, 40);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('CHAPTER_LOCKED');
    expect((await enter(h, 30)).body.errors.code).toBe('MINI_LEVEL_LOCKED');
    expect((await get(app, h, '/mini-dungeon')).body.data.tiers.map((t: { tier: number }) => t.tier)).toEqual([10, 20, 30]);
  });

  test('open: Lv40 enters tier 40 with void hounds and a 4 minute day', async () => {
    openApp();
    const h = await hero(40);
    const s = await get(app, h, '/mini-dungeon');
    expect(s.body.data).toMatchObject({ daily_limit_seconds: 240, remaining_seconds: 240 });
    expect(s.body.data.tiers.map((t: { tier: number }) => t.tier)).toEqual([10, 20, 30, 40]);
    const r = await enter(h, 40);
    expect(r.status).toBe(201);
    expect(r.body.data.active_run).toMatchObject({ tier: 40, map_id: 'mini_dungeon_40', monster_id: 'void_hound', entry_level: 40 });
    expect(Date.parse(r.body.data.active_run.ends_at) - Date.parse(r.body.data.active_run.started_at)).toBe(240_000);
    advance(5);
    const k = await kill(h, r.body.data.active_run.id);
    expect(k.status).toBe(200);
    expect(k.body.data).toMatchObject({ accepted: true, granted_gold: 36 });
    expect(k.body.data.granted_xp).toBe(Math.ceil(getGameData().economy.progression.xpToNext[39]! / 8000));
    advance(600);
    const again = await enter(h, 40);
    expect(again.status).toBe(422);
    expect(again.body.errors.code).toBe('MINI_TIME_EXHAUSTED');
  });

  test('open: time used in a lower tier the same day counts against the 4 minutes', async () => {
    openApp();
    const h = await hero(39);
    const first = await enter(h, 30);
    advance(160);
    await leave(h, first.body.data.active_run.id);
    await seedLevel(h, 40);
    const r = await enter(h, 40);
    expect(r.status).toBe(201);
    expect(r.body.data.remaining_seconds).toBe(80);
  });

  test('open: Lv49 enters tier 40, the Lv50 cap cannot', async () => {
    openApp();
    expect((await enter(await hero(49), 40)).status).toBe(201);
    expect((await enter(await hero(50), 40)).body.errors.code).toBe('MINI_LEVEL_LOCKED');
  });
});
