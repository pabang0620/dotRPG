import { randomUUID } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { monsterXp, rollKillDrops } from '../src/domains/kills/killRules';
import { xpFactor } from '../src/domains/fieldsessions/xpFactor';
import { electHost, electionWinner, type Candidate } from '../src/domains/fieldsessions/hostElection';
import { sweepStale } from '../src/ops/jobs/staleRuns';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { auth, buildApp, DATA_DIR, rehashDataDir, resetDb, shutdown } from './helpers';
import { anomalyKinds, expectLedgerConsistent, fakeRng, seedLevel } from './economyHelpers';
import { formParty, get, newHero, post, raw, type Hero } from './partyHelpers';
import { joinRoom, ticketFor } from './relayHelpers';
import { sleep, startServer, until, type TestServer } from './wsHelpers';

// 필드 화력 상한이 시험에 걸리게: 스킬 배율과 범위 배수를 1로 둔다(솔로 한도 = 10초에 해골 10마리)
let app = buildApp({ POWER_AOE_CAP: '1', POWER_SKILL_FACTOR: '1', RELAY_HOST_GRACE_MS: '300' });
let srv: TestServer;
let fixed = new Date('2026-10-05T03:00:00Z');
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeAll(async () => {
  await resetDb();
  srv = await startServer(app);
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await srv.stop();
  await shutdown();
});
beforeEach(() => {
  fixed = new Date('2026-10-05T03:00:00Z');
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});

const enter = (h: Hero, map = 'forest', rid?: string) => post(app, h, '/field-sessions/enter', { map_id: map }, rid);
const hb = (h: Hero, sid: string, epoch: number, synced = true) => raw(app, h, `/field-sessions/${sid}/heartbeat`, { seen_epoch: epoch, synced });
const kill = (h: Hero, sid: string, ref?: number, over: Record<string, unknown> = {}, rid?: string) =>
  post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: sid, ...(ref !== undefined ? { monster_ref: ref } : {}), ...over }, rid);
const observe = (h: Hero, sid: string, epoch: number, credits: { seat: number; kills: number }[], windowMs = 10_000) =>
  post(app, h, `/field-sessions/${sid}/observe`, { host_epoch: epoch, window_ms: windowMs, credits });

/** 방장(L)과 멤버들이 파티를 이루고 모두 숲에 들어온 상태(방장이 먼저 들어와 호스트) */
async function team(n = 2): Promise<{ L: Hero; M: Hero[]; sid: string; all: Hero[] }> {
  const all: Hero[] = [];
  for (let i = 0; i < n; i++) all.push(await newHero(app));
  await formParty(app, all[0] as Hero, all.slice(1));
  const first = await enter(all[0] as Hero);
  const sid = first.body.data.session.id as string;
  for (const h of all.slice(1)) {
    const r = await enter(h);
    if (r.status !== 200) throw new Error(`enter ${r.status} ${JSON.stringify(r.body)}`);
  }
  // 화력 상한은 연결·환영이 끝난(playing) 멤버만 합친다: 모두 synced 하트비트를 보낸다
  for (const h of all) await hb(h, sid, 1, true);
  return { L: all[0] as Hero, M: all.slice(1), sid, all };
}

const sessionRow = async (sid: string) => (await getPool().query('SELECT * FROM field_sessions WHERE uuid = $1', [sid])).rows[0];
const memberState = async (sid: string, h: Hero) =>
  (await getPool().query('SELECT m.state, m.seat, m.left_reason FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 AND m.character_id = $2', [sid, h.dbId])).rows[0] as { state: string; seat: number; left_reason: string | null };

describe('F1 입장과 세션 조회(F2, F7)', () => {
  it('정상: 방장이 세션을 만들고(201) 멤버가 합류한다(200). 중계 전송, 호스트 키는 호스트에게만', async () => {
    const a = await newHero(app);
    const b = await newHero(app);
    await formParty(app, a, [b]);
    const r1 = await enter(a);
    expect(r1.status).toBe(201);
    const s1 = r1.body.data.session;
    expect(s1).toMatchObject({
      map_id: 'forest',
      state: 'active',
      host: { character_id: a.id, seat: 0, epoch: 1 },
      transport: { current: 'relay', epoch: 1, order: ['relay'] },
      me: { seat: 0, state: 'joined', entry_token: null },
    });
    expect(s1.transport.relay.url).toMatch(/\/relay$/);
    expect(r1.body.data.host_key).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(s1.election_until).toBeTruthy();
    const r2 = await enter(b);
    expect(r2.status).toBe(200);
    expect(r2.body.data.host_key).toBeNull();
    expect(r2.body.data.session.id).toBe(s1.id);
    expect(r2.body.data.session.members.map((m: { seat: number }) => m.seat)).toEqual([0, 1]);
    expect(r2.body.data.session.members[0]).toMatchObject({ character_id: a.id, is_leader: true, level: 1 });
    expect(r2.body.data.session.members[0].gear_hash).toMatch(/^[0-9a-f]{16}$/);
    // F7 내 세션, F2 폴링(같은 버전이면 changed:false)
    const mine = await get(app, b, '/field-session');
    expect(mine.body.data.session.id).toBe(s1.id);
    const g = await get(app, b, `/field-sessions/${s1.id}`);
    expect(g.body.data).toMatchObject({ changed: true });
    const same = await get(app, b, `/field-sessions/${s1.id}?after_version=${g.body.data.session.version}`);
    expect(same.body.data).toEqual({ changed: false });
    expect((await get(app, await newHero(app), `/field-sessions/${s1.id}`)).body.errors.code).toBe('FIELD_SESSION_NOT_FOUND');
  });

  it('입력 오류와 거절: 받지 않는 값 400, 공유할 수 없는 맵 422, 혼자·파티 없음은 세션 없음(200)', async () => {
    const a = await newHero(app);
    expect(enter(a, 'forest').then((r) => r.body.data)).resolves.toMatchObject({ session: null, reason: 'NO_PARTY' });
    const b = await newHero(app);
    expect((await post(app, a, '/field-sessions/enter', { map_id: 'forest', seat: 2 })).status).toBe(400);
    expect((await post(app, a, '/field-sessions/enter', { map_id: '' })).status).toBe(400);
    await formParty(app, a, []);
    expect((await enter(a)).body.data).toMatchObject({ session: null, reason: 'ALONE' });
    await formParty(app, b, [await newHero(app)]);
    for (const map of ['village', 'dgn_forest_1', 'nowhere']) {
      const r = await enter(b, map);
      expect(r.status).toBe(422);
      expect(r.body.errors.code).toBe('MAP_NOT_SHARED');
    }
  });

  it('재전송: 같은 request_id는 같은 응답이고 행이 늘지 않는다. 동시 입장 둘은 세션 하나에 다른 좌석', async () => {
    const [a, b, c] = [await newHero(app), await newHero(app), await newHero(app)];
    await formParty(app, a, [b, c]);
    const rid = randomUUID();
    const [x, y] = await Promise.all([enter(a, 'forest', rid), enter(a, 'forest', rid)]);
    expect(x.body).toEqual(y.body);
    const [rb, rc] = await Promise.all([enter(b), enter(c)]);
    expect(rb.status).toBe(200);
    expect(rc.status).toBe(200);
    expect(rb.body.data.session.id).toBe(x.body.data.session.id);
    const sid = x.body.data.session.id as string;
    const rows = await getPool().query('SELECT m.seat FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 ORDER BY m.seat', [sid]);
    expect(rows.rows.map((r) => r.seat)).toEqual([0, 1, 2]);
    expect(Number((await getPool().query("SELECT count(*) AS n FROM field_sessions WHERE party_id = (SELECT party_id FROM field_sessions WHERE uuid = $1)", [sid])).rows[0].n)).toBe(1);
    const again = await post(app, a, '/field-sessions/enter', { map_id: 'forest' }, rid);
    expect(again.status).toBe(x.status);
    const other = await post(app, a, '/field-sessions/enter', { map_id: 'canyon' }, rid);
    expect(other.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });

  it('활성 파티 판에 있으면 409 IN_PARTY_RUN, 던전 출발은 세션을 닫는다(dungeon_start)', async () => {
    const { L, M, sid } = await team(2);
    const st = await post(app, L, '/party/start', { ai_count: 0 });
    expect(st.status).toBe(201);
    expect((await memberState(sid, L)).left_reason).toBe('dungeon_start');
    expect((await memberState(sid, M[0] as Hero)).left_reason).toBe('dungeon_start');
    expect((await sessionRow(sid)).state).toBe('ended');
    expect((await enter(L)).body.errors.code).toBe('IN_PARTY_RUN');
  });
});

describe('호스트 선출과 인계', () => {
  const c = (id: number, over: Partial<Candidate> = {}): Candidate => ({ characterId: id, seat: id, state: 'playing', isLeader: false, partyJoinedAt: id, fresh: true, ...over });

  it('electHost: 방장 우선, 없으면 파티 가입이 가장 빠른 사람, 신선하지 않거나 끊긴 사람은 제외, 후보가 없으면 null', () => {
    expect(electHost([c(2), c(1, { isLeader: true, partyJoinedAt: 9 }), c(3)])).toBe(1);
    expect(electHost([c(3, { partyJoinedAt: 1 }), c(2, { partyJoinedAt: 5 })])).toBe(3);
    expect(electHost([c(1, { fresh: false }), c(2, { state: 'disconnected' }), c(3, { state: 'joined' })])).toBe(3);
    expect(electHost([c(1, { state: 'joined' }), c(2)], null)).toBe(2); // playing 이 joined 보다 먼저
    expect(electHost([c(1), c(2)], 1)).toBe(2);
    expect(electHost([c(1, { state: 'left' })])).toBeNull();
    expect(electionWinner([c(2, { state: 'joined' }), c(1, { state: 'joined', isLeader: true })], 2)).toBe(1);
    expect(electionWinner([c(1, { state: 'joined', isLeader: true })], 1)).toBeNull();
  });

  it('시작 선출 창(5초) 안에 방장이 들어오면 호스트를 넘기고, 창 뒤에 들어오면 넘기지 않는다(고정 호스트)', async () => {
    const [L, a, b] = [await newHero(app), await newHero(app), await newHero(app)];
    await formParty(app, L, [a, b]);
    const first = await enter(a);
    const sid = first.body.data.session.id as string;
    expect(first.body.data.session.host.character_id).toBe(a.id);
    advance(2);
    const lead = await enter(L);
    expect(lead.body.data.session.host).toMatchObject({ character_id: L.id, epoch: 2 });
    // 창이 지난 뒤 다른 멤버가 들어와도 바뀌지 않는다
    advance(10);
    const late = await enter(b);
    expect(late.body.data.session.host).toMatchObject({ character_id: L.id, epoch: 2 });
    expect(sid).toBe(late.body.data.session.id);
    // 새 세션: 방장이 창 뒤에 들어오면 인계하지 않는다
    await post(app, a, `/field-sessions/${sid}/leave`, {});
    await post(app, b, `/field-sessions/${sid}/leave`, {});
    await post(app, L, `/field-sessions/${sid}/leave`, {});
    const second = await enter(a);
    expect(second.status).toBe(201);
    advance(30);
    const l2 = await enter(L);
    expect(l2.body.data.session.host).toMatchObject({ character_id: a.id, epoch: 1 });
  });

  it('F4 떠나기: 호스트가 떠나면 즉시 인계(세대+1), 멱등, 마지막 사람이 떠나면 세션 종료(session_key 삭제)', async () => {
    const { L, M, sid } = await team(2);
    const rid = randomUUID();
    const out = await post(app, L, `/field-sessions/${sid}/leave`, {}, rid);
    expect(out.status).toBe(200);
    expect(out.body.data).toMatchObject({ left: true, host: { character_id: (M[0] as Hero).id, epoch: 2 } });
    expect((await post(app, L, `/field-sessions/${sid}/leave`, {}, rid)).body).toEqual(out.body);
    expect((await post(app, L, `/field-sessions/${sid}/leave`, {})).body.data.left).toBe(true); // 이미 떠났으면 성공
    expect((await memberState(sid, L)).left_reason).toBe('left');
    expect((await get(app, L, `/field-sessions/${sid}`)).body.errors.code).toBe('FIELD_SESSION_NOT_FOUND');
    await post(app, M[0] as Hero, `/field-sessions/${sid}/leave`, {});
    const row = await sessionRow(sid);
    expect(row).toMatchObject({ state: 'ended', end_reason: 'empty', session_key: null, host_character_id: null });
    // 끝난 뒤 다시 들어오면 새 세션(새 uuid)
    const next = await enter(L);
    expect(next.status).toBe(201);
    expect(next.body.data.session.id).not.toBe(sid);
    const seatReuse = await get(app, L, '/field-session');
    expect(seatReuse.body.data.session.me.seat).toBe(0);
  });

  it('하트비트: synced면 playing, 12초 정체한 호스트는 disconnected로 서버가 인계, 60초 넘게 끊기면 left(rejoin_timeout)', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    const h1 = await hb(L, sid, 1, true);
    expect(h1.body.data).toMatchObject({ state: 'active', me: { state: 'playing' }, host_changed: false, host: { character_id: L.id, epoch: 1 } });
    expect(h1.body.data.members[0]).toMatchObject({ level: 1 });
    await hb(m, sid, 1, true);
    advance(13);
    const h2 = await hb(m, sid, 1, true);
    expect(h2.body.data).toMatchObject({ host_changed: true, host: { character_id: m.id, epoch: 2 } });
    expect((await memberState(sid, L)).state).toBe('disconnected');
    // 옛 호스트가 60초 안에 돌아오면 playing 이지만 호스트는 그대로 새 호스트
    const back = await hb(L, sid, 1, true);
    expect(back.body.data).toMatchObject({ me: { state: 'playing' }, host_changed: true, host: { character_id: m.id } });
    // 다시 끊겨 60초가 지나면 이탈
    advance(13);
    await hb(m, sid, 2, true);
    expect((await memberState(sid, L)).state).toBe('disconnected');
    advance(61);
    await hb(m, sid, 2, true);
    expect(await memberState(sid, L)).toMatchObject({ state: 'left', left_reason: 'rejoin_timeout' });
    expect((await hb(L, sid, 2)).body.errors.code).toBe('FIELD_SESSION_NOT_FOUND');
    expect((await raw(app, m, `/field-sessions/${sid}/heartbeat`, { seen_epoch: 2 })).status).toBe(400); // synced 필요
  });

  it('F5 claim: 호스트가 살아 있으면 HOST_ALIVE, 죽었으면 가장 우선인 후보만(NOT_NEXT_HOST), 옛 세대는 HOST_CHANGED', async () => {
    const { L, M, sid } = await team(3);
    const [m1, m2] = M as [Hero, Hero];
    await hb(L, sid, 1, true);
    const claim = (h: Hero, epoch: number, rid?: string) => post(app, h, `/field-sessions/${sid}/host/claim`, { observed_epoch: epoch }, rid);
    expect((await claim(m1, 1)).body.errors.code).toBe('HOST_ALIVE');
    advance(10);
    await hb(m1, sid, 1, false); // m1 은 신선, joined
    advance(3);
    const no = await claim(m2, 1);
    expect(no.status).toBe(409);
    expect(no.body.errors).toMatchObject({ code: 'NOT_NEXT_HOST', next_seat: 1 });
    const rid = randomUUID();
    const ok = await claim(m1, 1, rid);
    expect(ok.status).toBe(200);
    expect(ok.body.data).toMatchObject({ host: { character_id: m1.id, epoch: 2 } });
    expect(ok.body.data.host_key).toBeTruthy();
    expect((await claim(m1, 1, rid)).body).toEqual(ok.body); // 재전송
    const stale = await claim(m2, 1);
    expect(stale.body.errors.code).toBe('HOST_CHANGED');
    expect(stale.body.errors.host).toMatchObject({ character_id: m1.id });
    expect((await memberState(sid, L)).state).toBe('disconnected');
  });

  it('F6 observe: 호스트만(403 NOT_HOST), 현재 세대만(409), 공급 한도를 넘는 항목은 무시하고 field_host 기록', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    expect((await observe(m, sid, 1, [{ seat: 0, kills: 1 }])).body.errors.code).toBe('NOT_HOST');
    expect((await observe(L, sid, 2, [{ seat: 0, kills: 1 }])).body.errors.code).toBe('HOST_EPOCH_STALE');
    expect((await post(app, L, `/field-sessions/${sid}/observe`, { host_epoch: 1, window_ms: 10_000, credits: [] })).status).toBe(400);
    expect((await post(app, L, `/field-sessions/${sid}/observe`, { host_epoch: 1, window_ms: 10_000, credits: [{ seat: 0, kills: 1, xp: 5 }] })).status).toBe(400);
    // 정직한 관찰: 방 안 처치 먼저 받아들인 뒤 크레딧(받아들인 수 + 8 까지만)
    const ok = await observe(L, sid, 1, [{ seat: 0, kills: 4 }, { seat: 1, kills: 3 }]);
    expect(ok.body.data).toEqual({ accepted: true });
    let cr = await getPool().query('SELECT seat, kills_credited FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 ORDER BY seat', [sid]);
    expect(cr.rows.map((r) => r.kills_credited)).toEqual([4, 3]);
    await observe(L, sid, 1, [{ seat: 1, kills: 12 }], 30_000);
    cr = await getPool().query('SELECT kills_credited FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 AND m.seat = 1', [sid]);
    expect(cr.rows[0].kills_credited).toBe(8); // 받아들인 처치 0 + 크레딧 여유 8
    const bad = await observe(L, sid, 1, [{ seat: 1, kills: 99 }], 1000);
    expect(bad.status).toBe(200);
    expect(await anomalyKinds(L)).toContain('field_host');
    expect((await getPool().query('SELECT last_observe_at FROM field_sessions WHERE uuid = $1', [sid])).rows[0].last_observe_at).toBeTruthy();
  });
});

describe('처치 보고(E1): 멤버마다 각자 판정', () => {
  it('정상: 두 멤버가 같은 몬스터를 각자 한 번씩 보고하면 각자 경험치·드롭 1회, 한 멤버의 monster_ref 중복은 409', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    advance(2);
    const a = await kill(L, sid, 7);
    const b = await kill(m, sid, 7);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(a.body.data).toMatchObject({ granted_xp: 7, leveled_up: false, field: { shared: true, xp_factor: 1 } });
    expect(a.body.data.drops).toHaveLength(1);
    const dup = await kill(L, sid, 7, {}, randomUUID());
    expect(dup.status).toBe(409);
    expect(dup.body.errors.code).toBe('KILL_DUPLICATE');
    const rows = await getPool().query('SELECT character_id, monster_ref, xp_factor, field_session_id FROM kill_log WHERE monster_ref = 7 ORDER BY character_id');
    expect(rows.rows).toHaveLength(2);
    expect(rows.rows.every((r) => Number(r.xp_factor) === 1)).toBe(true);
    const acc = await getPool().query('SELECT kills_accepted FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 ORDER BY seat', [sid]);
    expect(acc.rows.map((r) => r.kills_accepted)).toEqual([1, 1]);
    await expectLedgerConsistent(L);
    await expectLedgerConsistent(m);
  });

  it('입력 오류: session_id와 run_id 동시, monster_ref 단독, 받지 않는 값은 400. 세션 밖·맵 불일치·연출 스폰은 거절', async () => {
    const { L, sid } = await team(2);
    expect((await kill(L, sid, 1, { run_id: randomUUID(), room_index: 0 })).status).toBe(400);
    expect((await post(app, L, '/kills', { map_id: 'forest', monster_id: 'skeleton', monster_ref: 3 })).status).toBe(400);
    expect((await kill(L, sid, 1, { xp: 999 })).status).toBe(400);
    const stranger = await newHero(app);
    expect((await kill(stranger, sid, 1)).body.errors.code).toBe('FIELD_SESSION_INVALID');
    expect((await kill(L, randomUUID(), 1)).body.errors.code).toBe('FIELD_SESSION_INVALID');
    advance(1);
    const wrongMap = await kill(L, sid, 1, { map_id: 'village', monster_id: 'skel_warrior' });
    expect(wrongMap.body.errors.code).toBe('FIELD_SESSION_INVALID');
    // 숲 세션에서 연출 스폰(skel_warrior)은 맥락 불일치
    const scripted = await kill(L, sid, 2, { monster_id: 'skel_warrior' });
    expect(scripted.body.errors.code).toBe('KILL_REJECTED');
  });

  it('재전송: 같은 request_id는 같은 응답이고 처치·경험치가 한 번만 반영된다', async () => {
    const { M, sid } = await team(2);
    const m = M[0] as Hero;
    const rid = randomUUID();
    advance(2);
    const a = await kill(m, sid, 11, {}, rid);
    const b = await kill(m, sid, 11, {}, rid);
    expect(a.status).toBe(200);
    expect(b.body).toEqual(a.body);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(Number((await getPool().query('SELECT count(*) AS n FROM kill_log WHERE character_id = $1', [m.dbId])).rows[0].n)).toBe(1);
    expect(Number((await getPool().query('SELECT xp FROM characters WHERE id = $1', [m.dbId])).rows[0].xp)).toBe(7);
  });

  it('동시 요청: 두 멤버의 동시 보고가 모두 받아들여지고, 같은 멤버의 같은 monster_ref 동시 보고는 하나만 통과한다', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    advance(2);
    const res = await Promise.all([kill(L, sid, 21), kill(m, sid, 21), kill(L, sid, 22), kill(m, sid, 22)]);
    expect(res.map((r) => r.status)).toEqual([200, 200, 200, 200]);
    const dups = await Promise.all([kill(L, sid, 30), kill(L, sid, 30)]);
    expect(dups.map((r) => r.status).sort()).toEqual([200, 409]);
    const acc = await getPool().query('SELECT kills_accepted FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 ORDER BY seat', [sid]);
    expect(acc.rows.map((r) => r.kills_accepted)).toEqual([3, 2]);
  });

  it('공급 한도는 멤버마다 센다(해골 숲 39지점 x 1.1 = 리스폰 25초에 43마리), 한도를 넘으면 KILL_REJECTED(멤버는 severity 1)', async () => {
    const { M, sid } = await team(2);
    const m = M[0] as Hero;
    for (let i = 0; i < 43; i++) {
      advance(0.5);
      const r = await kill(m, sid, 100 + i);
      expect(r.status).toBe(200);
    }
    advance(0.5);
    const over = await kill(m, sid, 200);
    expect(over.status).toBe(422);
    expect(over.body.errors.code).toBe('KILL_REJECTED');
    const an = await getPool().query("SELECT kind, severity FROM anomaly_log WHERE character_id = $1 AND kind = 'kill_supply'", [m.dbId]);
    expect(an.rows).toEqual([{ kind: 'kill_supply', severity: 1 }]);
    advance(30);
    expect((await kill(m, sid, 201)).status).toBe(200);
  });

  it('화력 상한: 파티 세션은 활성 멤버 화력 합 x 1.15 로 판정한다(솔로 레벨 1 한도 10마리를 넘어 받고, 합이 작으면 거절)', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    // 스킬 배율·범위 배수를 1로 낮춰 두었다: 솔로 레벨 1은 10초에 해골 10마리가 화력 한도(10 x 30 <= 10/0.36 x 10 + 30)
    for (let i = 0; i < 12; i++) {
      advance(0.5);
      expect((await kill(m, sid, 300 + i)).status).toBe(200);
    }
    // 세션 화력 합을 줄이면(멤버 attack_cap 1 + 1): 개인 상한 1 x (1 + 알파 1.0) = 2 이므로 한도는 (2 / 0.36 x 10 + 30) / 30 = 2마리
    await getPool().query('UPDATE field_session_members SET attack_cap = 1 WHERE session_id = (SELECT id FROM field_sessions WHERE uuid = $1)', [sid]);
    advance(11);
    // 몬스터 체력·장비가 데이터에서 바뀌므로 정확한 마릿수 대신 "작은 합이면 12마리 안에 거절된다"로 본다
    let over = await kill(L, sid, 320);
    for (let i = 1; over.status === 200 && i < 12; i++) {
      advance(0.5);
      over = await kill(L, sid, 320 + i);
    }
    expect(over.status).toBe(422);
    expect(over.body.errors.code).toBe('KILL_REJECTED');
    expect(await anomalyKinds(L)).toContain('kill_power');
  });

  it('처치 대조 게이트: 호스트 관찰이 신선한 동안 기여로 인정받지 못한 처치가 24건이면 막고(field_uncredited), 크레딧이 들어오면 풀린다', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    await observe(L, sid, 1, [{ seat: 0, kills: 0 }]);
    for (let i = 0; i < 17; i++) {
      advance(0.5);
      expect((await kill(m, sid, 400 + i)).status).toBe(200);
    }
    advance(20);
    await observe(L, sid, 1, [{ seat: 0, kills: 0 }]);
    for (let i = 0; i < 7; i++) {
      advance(0.5);
      expect((await kill(m, sid, 500 + i)).status).toBe(200);
    }
    advance(0.5);
    const blocked = await kill(m, sid, 600);
    expect(blocked.status).toBe(422);
    expect(blocked.body.errors.code).toBe('KILL_REJECTED');
    expect(await anomalyKinds(m)).toContain('field_uncredited');
    // 호스트가 이 멤버의 기여 처치를 알리면 부채가 줄어 다시 받는다
    await observe(L, sid, 1, [{ seat: 1, kills: 10 }]);
    advance(0.5);
    expect((await kill(m, sid, 601)).status).toBe(200);
    // 호스트 관찰이 30초 넘게 없으면 게이트는 꺼진다(정직한 멤버를 호스트 장애로 막지 않는다)
    advance(40);
    for (let i = 0; i < 12; i++) {
      advance(0.5);
      expect((await kill(m, sid, 700 + i)).status).toBe(200);
    }
    expect((await getPool().query("SELECT count(*) AS n FROM anomaly_log WHERE kind = 'field_host' AND detail->>'why' = 'observe_missing' AND detail->>'session_id' = $1", [sid])).rows[0].n).toBe('1');
  });
});

describe('레벨 격차 감쇠(6.7)', () => {
  const policy = { carrySlack: 5, carryStep: 0.12, carryMin: 0.2 };

  it('xpFactor: 슬랙 안은 1, 격차마다 0.12씩 줄고 하한 0.2. 현재 데이터(몬스터 레벨 1)에서는 모두 1', () => {
    expect(xpFactor(1, 1, policy)).toBe(1);
    expect(xpFactor(20, 15, policy)).toBe(1);
    expect(xpFactor(20, 10, policy)).toBe(0.4);
    expect(xpFactor(20, 12, policy)).toBe(0.64);
    expect(xpFactor(20, 1, policy)).toBe(0.2);
    expect(xpFactor(1, 50, policy)).toBe(1);
  });

  it('드롭 확률 배율: 재료 확률에 xp_factor가 곱해진다', () => {
    const eco = getGameData().economy;
    const mat = eco.shop.materials.find((m) => m.dropChance > 0 && m.dropChance < 1) as (typeof eco.shop.materials)[number];
    const def = eco.monsters.get('skeleton') as NonNullable<ReturnType<typeof eco.monsters.get>>;
    const rng = fakeRng({ unit: mat.dropChance * 0.5, int: (min) => min });
    const keys = (mul: number) => rollKillDrops(eco, def, 'warrior', 0, rng, mul).filter((d) => d.itemKey === mat.id).length;
    expect(keys(1)).toBeGreaterThan(0);
    expect(keys(0.2)).toBe(0);
  });

  it('통합: 몬스터 레벨 20 필드에서 레벨 1 멤버는 하드 격차(경험치 1), 레벨 12 멤버는 x0.55(9단계 기본값), 솔로는 감쇠 없음', async () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-'));
    for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
    const maps = JSON.parse(fs.readFileSync(path.join(dir, 'maps.json'), 'utf8')) as { maps: { id: string; fieldSpawns: { level?: number }[] }[] };
    const spawn = (maps.maps.find((m) => m.id === 'forest') as { fieldSpawns: { level?: number; xp?: number }[] }).fieldSpawns[0]!;
    spawn.level = 20;
    delete spawn.xp; // 사냥터 고정 경험치 대신 레벨 공식으로 본다
    fs.writeFileSync(path.join(dir, 'maps.json'), JSON.stringify(maps));
    rehashDataDir(dir);
    const high = buildApp({ GAME_DATA_DIR: dir });
    const eco = getGameData().economy;
    const base = monsterXp(eco, eco.monsters.get('skeleton') as never, 20);
    const [a, b] = [await newHero(high), await newHero(high)];
    await formParty(high, a, [b]);
    await seedLevel(b, 12);
    const s = (await post(high, a, '/field-sessions/enter', { map_id: 'forest' })).body.data.session.id as string;
    await post(high, b, '/field-sessions/enter', { map_id: 'forest' });
    const ka = await post(high, a, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: s, monster_ref: 1 });
    const kb = await post(high, b, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: s, monster_ref: 1 });
    expect(ka.status).toBe(200);
    // d = 20 - 1 = 19 >= FIELD_CARRY_HARD_GAP(15): 경험치는 정확히 1(배율은 하한 0.02)
    expect(ka.body.data.field.xp_factor).toBe(0.02);
    expect(ka.body.data.granted_xp).toBe(1);
    // d = 20 - 12 = 8: 1 - 0.15 x (8 - 5) = 0.55
    expect(kb.body.data.field.xp_factor).toBe(0.55);
    expect(kb.body.data.granted_xp).toBe(Math.round(base * 0.55));
    expect(Number((await getPool().query('SELECT xp_factor FROM kill_log WHERE character_id = $1', [a.dbId])).rows[0].xp_factor)).toBe(0.02);
    const solo = await newHero(high);
    const ks = await post(high, solo, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect(ks.body.data.granted_xp).toBe(base);
    expect(ks.body.data.field).toBeUndefined();
    app = buildApp({ POWER_AOE_CAP: '1', POWER_SKILL_FACTOR: '1', RELAY_HOST_GRACE_MS: '300' });
  });
});

describe('파티 변화가 세션에 반영된다', () => {
  it('파티를 나가면 같은 트랜잭션에서 세션 멤버십이 닫히고(호스트면 인계), 마지막이 나가면 세션이 끝난다. 강퇴도 같다', async () => {
    const { L, M, sid } = await team(3);
    const [m1, m2] = M as [Hero, Hero];
    const k = await post(app, L, '/party/kick', { target: m2.id });
    expect(k.status).toBe(200);
    expect(await memberState(sid, m2)).toMatchObject({ state: 'left', left_reason: 'kicked' });
    expect((await get(app, m2, `/field-sessions/${sid}`)).body.errors.code).toBe('FIELD_SESSION_NOT_FOUND');
    expect((await post(app, L, '/party/leave', {})).status).toBe(200);
    expect(await memberState(sid, L)).toMatchObject({ state: 'left', left_reason: 'party_left' });
    expect((await sessionRow(sid)).host_character_id).toBe(String(m1.dbId));
    expect((await sessionRow(sid)).host_epoch).toBe(2);
    expect((await post(app, m1, '/party/leave', {})).status).toBe(200);
    expect(await sessionRow(sid)).toMatchObject({ state: 'ended', end_reason: 'empty', session_key: null });
  });

  it('stale-runs 작업: 마지막 활동이 FIELD_STALE_SECONDS(300초)를 넘은 세션을 ended(stale)로 정리한다', async () => {
    const { sid } = await team(2);
    advance(200);
    expect((await sweepStale()).endedFieldSessions).toBe(0);
    advance(150);
    expect((await sweepStale()).endedFieldSessions).toBeGreaterThanOrEqual(1);
    expect(await sessionRow(sid)).toMatchObject({ state: 'ended', end_reason: 'stale' });
  });
});

describe('필드 중계 방(T1 kind=field)', () => {
  it('중계로 붙고, 호스트 연결이 끊기면 서버가 인계하고 field_sessions.host 가 바뀐다', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    expect((await ticketFor(app, await newHero(app), 'field', sid)).body.errors?.code).toBe('ROOM_NOT_FOUND');
    const a = await joinRoom(app, srv.port, L, 'field', sid);
    const b = await joinRoom(app, srv.port, m, 'field', sid);
    expect(a.ready).toMatchObject({ seat: 0, host_seat: 0, host_epoch: 1, room: { kind: 'field' } });
    expect(b.ready).toMatchObject({ seat: 1, peers: [0] });
    await until(async () => (await memberState(sid, m)).state === 'playing');
    a.c.terminate();
    expect(await b.c.waitT('host.changed')).toMatchObject({ seat: 1, epoch: 2 });
    expect((await sessionRow(sid)).host_character_id).toBe(String(m.dbId));
    expect((await memberState(sid, L)).state).toBe('disconnected');
    // 옛 호스트가 돌아오면 playing 으로 복귀하지만 호스트는 그대로
    const back = await joinRoom(app, srv.port, L, 'field', sid, { resume: true });
    expect(back.ready).toMatchObject({ host_seat: 1, host_epoch: 2 });
    await until(async () => (await memberState(sid, L)).state === 'playing');
    // 세션이 끝나면 방도 닫힌다
    await post(app, L, `/field-sessions/${sid}/leave`, {});
    expect(await back.c.waitClose()).toBe(4011);
    await post(app, m, `/field-sessions/${sid}/leave`, {});
    expect(await b.c.waitClose()).toBeGreaterThan(0);
    expect((await ticketFor(app, m, 'field', sid)).body.errors?.code).toBe('ROOM_CLOSED');
    await sleep(100);
  });
});
