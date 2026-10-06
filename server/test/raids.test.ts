import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { anomalyKinds, countOf, expectLedgerConsistent, fakeRng, seedClaims, seedItem, seedLevel } from './economyHelpers';
import { clearAll, formParty, get, honestHostReport, newHero, post, roomsOf, stats, startAndBegin, type Hero } from './partyHelpers';

const app = buildApp();
const WED = '2026-10-07T03:00:00Z'; // 수요일 12:00 KST: 해골왕·바위 심장 열림
const SAT = '2026-10-10T03:00:00Z';
const SUN = '2026-10-11T03:00:00Z';
let fixed = new Date(WED);
const at = (iso: string) => {
  fixed = new Date(iso);
};
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeEach(async () => {
  await resetDb();
  at(WED);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const raidHero = async (level: number, claims: string[]): Promise<Hero> => {
  const h = await newHero(app);
  await seedLevel(h, level);
  await seedClaims(h, claims);
  return h;
};
const result = (h: Hero, runUuid: string, body: Record<string, unknown>, rid?: string) =>
  post(app, h, `/dungeon-runs/${runUuid}/result`, body, rid);
const raidKill = (h: Hero, runId: string, map: string, monster: string, room: number) =>
  post(app, h, '/kills', { map_id: map, monster_id: monster, run_id: runId, room_index: room });

describe('GET /raids', () => {
  it('해금·열린 요일·보상 가능 여부를 서버 시계로 알려 준다', async () => {
    const h = await newHero(app);
    const res = await get(app, h, '/raids');
    expect(res.status).toBe(200);
    const raids = res.body.data.raids as { id: string; unlocked: boolean; open_today: boolean; tier: string; reward_available: boolean }[];
    expect(raids.map((r) => r.id).sort()).toEqual(['raid_grah', 'raid_skeleton_king']);
    expect(raids.every((r) => !r.unlocked)).toBe(true);
    expect(raids.find((r) => r.id === 'raid_skeleton_king')).toMatchObject({ open_today: true, tier: 'mid' });
    expect(raids.find((r) => r.id === 'raid_grah')).toMatchObject({ open_today: false, tier: 'final', reward_available: false });
    expect(res.body.data.reset.weekly_start_at).toBeTruthy();

    await seedClaims(h, ['c1_fortress']);
    const after = await get(app, h, '/raids');
    expect(after.body.data.raids.find((r: { id: string }) => r.id === 'raid_skeleton_king')).toMatchObject({
      unlocked: true,
      reward_available: true,
      clears_this_period: 0,
      min_humans_for_reward: 2,
      key: { item: 'key_seal', have: 0, drop_min: 20, drop_max: 50 },
    });
  });

  it('해금 퀘스트를 받지 않았어도 선행 퀘스트를 청구하고 저장 상태가 수락 이상이면 연다', async () => {
    const h = await newHero(app);
    await seedClaims(h, ['c1_stronger']);
    expect((await get(app, h, '/raids')).body.data.raids.find((r: { id: string }) => r.id === 'raid_skeleton_king').unlocked).toBe(false);
    await getPool().query("UPDATE character_state SET quests = $2::jsonb WHERE character_id = $1", [
      h.dbId,
      JSON.stringify([{ id: 'c1_fortress', status: 2, step: 0, counts: [] }]),
    ]);
    expect((await get(app, h, '/raids')).body.data.raids.find((r: { id: string }) => r.id === 'raid_skeleton_king').unlocked).toBe(true);
  });
});

describe('레이드 입장(솔로, AI 동반)', () => {
  const enter = (h: Hero, over: Record<string, unknown> = {}, rid?: string) =>
    post(app, h, '/dungeon-runs', { dungeon_id: 'raid_skeleton_king', difficulty: 0, ...over }, rid);

  it('정상: 입장 횟수를 쓰지 않고, 사람이 1명이면 연습 입장(보상 잠금)이다', async () => {
    const h = await raidHero(20, ['c1_fortress']);
    const res = await enter(h, { ai_count: 3 });
    expect(res.status).toBe(201);
    expect(res.body.data.run).toMatchObject({ party_size: 4, humans: 1, ai_count: 3, reward_locked: true, lock_reason: 'TOO_FEW_HUMANS' });
    expect(res.body.data.entries_left).toBe(3);
    expect((await get(app, h, '/dungeons')).body.data.entries.used).toBe(0);
    const run = await get(app, h, `/dungeon-runs/${res.body.data.run.id}`);
    expect(run.body.data.run).toMatchObject({ state: 'playing', ai_count: 3, reward_locked: true });
  });

  it('입력 오류와 규칙 오류: ai_count, 해금, 레벨, 요일, 난이도', async () => {
    const h = await raidHero(20, []);
    expect((await enter(h, { ai_count: 4 })).status).toBe(400);
    const locked = await enter(h);
    expect(locked.status).toBe(422);
    expect(locked.body.errors.code).toBe('RAID_LOCKED');
    expect(await anomalyKinds(h)).toContain('raid_enter');
    const low = await raidHero(1, ['c1_fortress']);
    expect((await enter(low)).body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: 17 });
    expect((await enter(h, { difficulty: 1 })).body.errors.code).toBe('RAID_LOCKED');
    const ok = await raidHero(20, ['c1_fortress']);
    expect((await enter(ok, { difficulty: 1 })).body.errors.code).toBe('DIFFICULTY_LOCKED');
    // 월요일에는 해골왕이 닫힌다(레이드는 openDays만 본다)
    at('2026-10-05T03:00:00Z');
    expect((await enter(ok)).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    // 토요일: 요일 던전은 weekendOpensAll로 전부 열리지만 최종 레이드(일요일만)는 닫혀 있다
    at(SAT);
    const fin = await raidHero(40, ['c2_grah']);
    expect((await enter(fin, { dungeon_id: 'raid_grah' })).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    expect((await post(app, fin, '/dungeon-runs', { dungeon_id: 'smelter', difficulty: 0 })).status).toBe(201);
  });

  it('재전송: 같은 request_id의 레이드 입장은 같은 응답, 동시 입장은 한 판만', async () => {
    const h = await raidHero(20, ['c1_fortress']);
    const rid = randomUUID();
    const a = await enter(h, { ai_count: 1 }, rid);
    const b = await enter(h, { ai_count: 1 }, rid);
    expect(b.body).toEqual(a.body);
    const h2 = await raidHero(20, ['c1_fortress']);
    const [x, y] = await Promise.all([enter(h2), enter(h2)]);
    expect([x.status, y.status].sort()).toEqual([201, 409]);
    expect([x, y].find((r) => r.status === 409)?.body.errors.code).toBe('RUN_ACTIVE');
  });

  it('최종 레이드 열쇠 부족: 보상 없는 연습판으로 입장(혼자서도 스토리 완료 가능), 열쇠가 있어도 입장(횟수 소모 없음)', async () => {
    at(SUN);
    const h = await raidHero(40, ['c2_grah']);
    const no = await post(app, h, '/dungeon-runs', { dungeon_id: 'raid_grah', difficulty: 0 });
    expect(no.status).toBe(201);
    expect(no.body.data.run).toMatchObject({ reward_locked: true });
    expect(await anomalyKinds(h)).not.toContain('raid_enter');
  });

  it('연습판(사람 1명): 처치 경험치·드롭이 없고, 클리어해도 카드·청구·열쇠가 없다. 레이드 몬스터는 필드에서 받지 않는다', async () => {
    const h = await raidHero(20, ['c1_fortress']);
    const run = await enter(h, { ai_count: 3 });
    const runId = run.body.data.run.id as string;
    const t0 = fixed.getTime();
    const before = await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [h.dbId]);
    advance(20);
    const k = await raidKill(h, runId, 'dgn_raid_1', 'skel_knight', 0);
    expect(k.status).toBe(200);
    expect(k.body.data).toMatchObject({ granted_xp: 0, drops: [], reward_locked: true });
    expect((await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [h.dbId])).rows[0]).toEqual(before.rows[0]);
    const field = await post(app, h, '/kills', { map_id: 'village', monster_id: 'boss_skeleton_king' });
    expect(field.body.errors.code).toBe('RAID_NOT_AVAILABLE');
    // 끝까지 클리어(첫 방의 첫 처치는 이미 보고했다)
    let skip = 1;
    for (const [room, def] of roomsOf('raid_skeleton_king').entries()) {
      for (const [monster, n] of def.kills) {
        for (let i = 0; i < n; i++) {
          if (skip > 0 && room === 0 && monster === 'skel_knight') {
            skip--;
            continue;
          }
          advance(20);
          expect((await raidKill(h, runId, def.map, monster, room)).status).toBe(200);
        }
      }
    }
    at(new Date(t0 + 300_000).toISOString());
    const res = await result(h, runId, { outcome: 'cleared', stats: stats({ elapsed_ms: 300_000, max_combo: 300 }) });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ result: 'cleared', granted_xp: 0, card_count: 0 });
    expect(res.body.data.raid).toEqual({ reward_locked: true, lock_reason: 'TOO_FEW_HUMANS' });
    const claims = await getPool().query('SELECT count(*) AS n FROM raid_claims WHERE character_id = $1', [h.dbId]);
    expect(Number((claims.rows[0] as { n: string }).n)).toBe(0);
    expect(await countOf(h, 'key_seal')).toBe(0);
    await expectLedgerConsistent(h);
  });
});

/** 레이드 파티 판을 만들어 모두 클리어하고 방장 보고까지 보낸다. 정산(result)은 호출 쪽 몫 */
async function raidParty(dungeonId: string, heroes: Hero[], step: number, totalSec: number) {
  const [host, ...members] = heroes as [Hero, ...Hero[]];
  await formParty(app, host, members, { dungeon_id: dungeonId, difficulty: 0 });
  const { runId, runs } = await startAndBegin(app, host, members);
  const t0 = fixed.getTime();
  for (const h of heroes) {
    at(new Date(t0 + 1).toISOString());
    await clearAll(app, h, runs.get(h.id) as string, advance, dungeonId, step);
  }
  at(new Date(t0 + totalSec * 1000).toISOString());
  const elapsed = totalSec * 1000;
  const rep = await post(
    app,
    host,
    `/party-runs/${runId}/host-report`,
    await honestHostReport(runs.get(host.id) as string, heroes, { elapsed_ms: elapsed }, dungeonId),
  );
  if (rep.status !== 200) throw new Error(`host-report ${rep.status} ${JSON.stringify(rep.body)}`);
  return { runId, runs, elapsed };
}

describe('레이드 파티 정산', () => {
  it('중간 레이드: 사람 2명이면 보상(경험치·카드·열쇠 조각)과 일일 청구가 한 번 생기고, 같은 날 두 번째는 연습이다', async () => {
    const host = await raidHero(20, ['c1_fortress']);
    const member = await raidHero(20, ['c1_fortress']);
    const { runId, runs, elapsed } = await raidParty('raid_skeleton_king', [host, member], 20, 300);
    // 방장이 자기 결과를 먼저 보고해야 멤버의 정산이 확정된다
    const hostBody = { outcome: 'cleared', stats: stats({ elapsed_ms: elapsed, max_combo: 300 }) };
    expect((await result(host, runs.get(host.id) as string, hostBody)).body.data.result).toBe('pending');
    const rm = await result(member, runs.get(member.id) as string, hostBody);
    expect(rm.body.data.result).toBe('cleared');
    expect(rm.body.data.granted_xp).toBeGreaterThan(0);
    expect(rm.body.data.card_count).toBe(4);
    expect(rm.body.data.raid).toMatchObject({ reward_locked: false, key_gain: 20 });
    const rh = await post(app, host, `/dungeon-runs/${runs.get(host.id)}/settle`, {});
    expect(rh.body.data.result).toBe('cleared');
    for (const h of [host, member]) {
      expect(await countOf(h, 'key_seal')).toBe(20);
      const c = await getPool().query('SELECT period_kind FROM raid_claims WHERE character_id = $1', [h.dbId]);
      expect(c.rows).toEqual([{ period_kind: 'daily' }]);
      const led = await getPool().query("SELECT delta FROM item_ledger WHERE character_id = $1 AND reason = 'raid_key'", [h.dbId]);
      expect(led.rows).toEqual([{ delta: 20 }]);
      await expectLedgerConsistent(h);
    }
    const raids = await get(app, host, '/raids');
    expect(raids.body.data.raids.find((r: { id: string }) => r.id === 'raid_skeleton_king')).toMatchObject({ reward_available: false, clears_this_period: 1 });
    expect(runId).toBeTruthy();

    // 같은 날 다시: 입장은 되지만 보상 잠금(ALREADY_CLAIMED)이고 처치 보상도 없다
    const again = await post(app, host, '/party/start', { ai_count: 0 });
    expect(again.status).toBe(201);
    const pv = await get(app, member, '/party');
    await post(app, member, `/party-runs/${again.body.data.run.id}/join`, { entry_token: pv.body.data.party.run.me.entry_token });
    const view = await get(app, host, `/party-runs/${again.body.data.run.id}`);
    expect(view.body.data.run.me).toMatchObject({ reward_locked: true, lock_reason: 'ALREADY_CLAIMED' });
    advance(20);
    const k = await raidKill(host, view.body.data.run.me.run_id, 'dgn_raid_1', 'skel_knight', 0);
    expect(k.body.data).toMatchObject({ granted_xp: 0, reward_locked: true });
    // 내일(다음 06:00 이후)은 다시 받을 수 있다
    at('2026-10-08T00:00:00Z');
    expect((await get(app, host, '/raids')).body.data.raids.find((r: { id: string }) => r.id === 'raid_skeleton_king').reward_available).toBe(false); // 목요일은 닫힘
    at('2026-10-10T03:00:00Z');
    expect((await get(app, host, '/raids')).body.data.raids.find((r: { id: string }) => r.id === 'raid_skeleton_king').reward_available).toBe(true);
  });

  it('최종 레이드: 열쇠 조각을 소모하고 주간 청구가 생긴다. 판 도중 열쇠를 잃으면 그 멤버만 KEYS_MISSING으로 잠긴다', async () => {
    at(SUN);
    const a = await raidHero(40, ['c2_grah']);
    const b = await raidHero(40, ['c2_grah']);
    await seedItem(a, 'key_seal', 100);
    await seedItem(b, 'key_seal', 100);
    const { runs, elapsed } = await raidParty('raid_grah', [a, b], 20, 330);
    // b는 판 도중 열쇠를 팔아 버렸다
    await getPool().query("DELETE FROM character_items WHERE character_id = $1 AND item_key = 'key_seal'", [b.dbId]);
    await getPool().query(
      "INSERT INTO item_ledger (character_id, item_key, delta, balance_after, location, reason, ref) VALUES ($1, 'key_seal', -100, 0, 'bag', 'shop_sell', 'test')",
      [b.dbId],
    );
    const body = { outcome: 'cleared', stats: stats({ elapsed_ms: elapsed, max_combo: 300 }) };
    expect((await result(a, runs.get(a.id) as string, body)).body.data.result).toBe('pending');
    const rb = await result(b, runs.get(b.id) as string, body);
    expect(rb.body.data.result).toBe('cleared');
    expect(rb.body.data).toMatchObject({ granted_xp: 0, card_count: 0 });
    expect(rb.body.data.raid).toEqual({ reward_locked: true, lock_reason: 'KEYS_MISSING' });
    const ra = await post(app, a, `/dungeon-runs/${runs.get(a.id)}/settle`, {});
    expect(ra.body.data.granted_xp).toBeGreaterThan(0);
    expect(ra.body.data.raid).toMatchObject({ reward_locked: false, key_cost: 100 });
    expect(await countOf(a, 'key_seal')).toBe(0);
    const claim = await getPool().query('SELECT period_kind FROM raid_claims WHERE character_id = $1', [a.dbId]);
    expect(claim.rows).toEqual([{ period_kind: 'weekly' }]);
    expect((await getPool().query('SELECT count(*) AS n FROM raid_claims WHERE character_id = $1', [b.dbId])).rows[0]).toEqual({ n: '0' });
    await expectLedgerConsistent(a);
    // 보상을 받은 주에는 열쇠 없이도 연습 입장이 된다
    const practice = await post(app, a, '/dungeon-runs', { dungeon_id: 'raid_grah', difficulty: 0 });
    expect(practice.status).toBe(201);
    expect(practice.body.data.run.reward_locked).toBe(true);
  });

  it('레이드 보상 최소 인원: 정산 때 이탈하지 않은 사람이 2명 미만이면 잠긴다', async () => {
    const host = await raidHero(20, ['c1_fortress']);
    const member = await raidHero(20, ['c1_fortress']);
    await formParty(app, host, [member], { dungeon_id: 'raid_skeleton_king', difficulty: 0 });
    const { runId, runs } = await startAndBegin(app, host, [member]);
    // 멤버가 대조를 피하려고 시작 직후 나갔다
    expect((await post(app, member, `/party-runs/${runId}/leave`, {})).body.data.left).toBe(true);
    const t0 = fixed.getTime();
    await clearAll(app, host, runs.get(host.id) as string, advance, 'raid_skeleton_king', 20);
    at(new Date(t0 + 300_000).toISOString());
    const res = await result(host, runs.get(host.id) as string, { outcome: 'cleared', stats: stats({ elapsed_ms: 300_000, max_combo: 300 }) });
    expect(res.body.data.result).toBe('cleared');
    expect(res.body.data).toMatchObject({ granted_xp: 0, card_count: 0 });
    expect(res.body.data.raid).toEqual({ reward_locked: true, lock_reason: 'TOO_FEW_HUMANS' });
    expect((await getPool().query('SELECT count(*) AS n FROM raid_claims WHERE character_id = $1', [host.dbId])).rows[0]).toEqual({ n: '0' });
  });
});
