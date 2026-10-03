import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { anomalyKinds, expectLedgerConsistent, fakeRng } from './economyHelpers';
import { clearAll, formParty, get, honestHostReport, newHero, post, raw, stats, startAndBegin, type Hero } from './partyHelpers';

const app = buildApp();
const MONDAY = '2026-10-05T03:00:00Z';
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

async function duo() {
  const host = await newHero(app);
  const member = await newHero(app);
  await formParty(app, host, [member]);
  const { runId, runs } = await startAndBegin(app, host, [member]);
  return { host, member, runId, hostRun: runs.get(host.id) as string, memberRun: runs.get(member.id) as string };
}

const result = (h: Hero, runUuid: string, body: Record<string, unknown>, rid?: string) =>
  post(app, h, `/dungeon-runs/${runUuid}/result`, body, rid);
const settle = (h: Hero, runUuid: string, rid?: string) => post(app, h, `/dungeon-runs/${runUuid}/settle`, {}, rid);

describe('파티 판 시작: start, join, begin', () => {
  it('정상: 멤버 입장 확인으로 자동 시작, 멤버별 dungeon_runs와 입장 횟수', async () => {
    const { host, member, runId, hostRun, memberRun } = await duo();
    expect(hostRun).not.toBe(memberRun);
    const view = await get(app, host, `/party-runs/${runId}`);
    expect(view.body.data.run).toMatchObject({ state: 'playing', humans: 2, ai_count: 0 });
    expect(view.body.data.run.me.entry_token).toBeTruthy();
    const rows = await getPool().query(
      'SELECT party_size, humans, counts_entry, slot FROM dungeon_runs WHERE uuid = ANY($1::uuid[]) ORDER BY slot',
      [[hostRun, memberRun]],
    );
    expect(rows.rows).toEqual([
      { party_size: 2, humans: 2, counts_entry: true, slot: 0 },
      { party_size: 2, humans: 2, counts_entry: true, slot: 1 },
    ]);
    expect((await get(app, member, '/dungeons')).body.data.entries.used).toBe(1);
    // 판 참여 중에는 솔로 입장이 막힌다
    const solo = await post(app, member, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 });
    expect(solo.body.errors.code).toBe('IN_PARTY_RUN');
  });

  it('입력 오류와 규칙 오류: 토큰 불일치, 준비 안 함, 방장이 아님, 정원 초과', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    const partyId = await formParty(app, host, []);
    const ap = await post(app, member, `/parties/${partyId}/apply`, {});
    await post(app, host, `/party/applications/${ap.body.data.application.id}/respond`, { accept: true });
    expect((await post(app, host, '/party/start', { ai_count: 4 })).status).toBe(400);
    expect((await post(app, host, '/party/start', { ai_count: 3 })).body.errors.code).toBe('PARTY_TOO_BIG');
    expect((await post(app, host, '/party/start', { ai_count: 0 })).body.errors.code).toBe('NOT_ALL_READY');
    expect((await post(app, member, '/party/start', { ai_count: 0 })).body.errors.code).toBe('NOT_LEADER');
    await raw(app, member, '/party/ready', { ready: true });
    const st = await post(app, host, '/party/start', { ai_count: 1 });
    expect(st.status).toBe(201);
    const runId = st.body.data.run.id as string;
    const bad = await post(app, member, `/party-runs/${runId}/join`, { entry_token: 'A'.repeat(22) });
    expect(bad.status).toBe(403);
    expect(bad.body.errors.code).toBe('ENTRY_TOKEN_INVALID');
    expect((await post(app, member, `/party-runs/${runId}/join`, { entry_token: 'short' })).status).toBe(400);
    // 방장이 아닌 사람은 begin 불가
    expect((await post(app, member, `/party-runs/${runId}/begin`, {})).body.errors.code).toBe('NOT_HOST');
  });

  it('재전송: 같은 request_id의 start와 join은 같은 응답이고 판은 하나', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    await formParty(app, host, [member]);
    const rid = randomUUID();
    const a = await post(app, host, '/party/start', { ai_count: 0 }, rid);
    const b = await post(app, host, '/party/start', { ai_count: 0 }, rid);
    expect(b.status).toBe(a.status);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body.data.run.id).toBe(a.body.data.run.id);
    const runs = await getPool().query("SELECT count(*) AS n FROM party_runs WHERE state = 'gathering'");
    expect(Number((runs.rows[0] as { n: string }).n)).toBeGreaterThanOrEqual(1);
    const pv = await get(app, member, '/party');
    const token = pv.body.data.party.run.me.entry_token as string;
    const jr = randomUUID();
    const j1 = await post(app, member, `/party-runs/${a.body.data.run.id}/join`, { entry_token: token }, jr);
    const j2 = await post(app, member, `/party-runs/${a.body.data.run.id}/join`, { entry_token: token }, jr);
    expect(j2.body).toEqual(j1.body);
    const count = await getPool().query('SELECT count(*) AS n FROM dungeon_runs WHERE character_id = $1', [member.dbId]);
    expect(Number((count.rows[0] as { n: string }).n)).toBe(1);
  });

  it('동시 요청: 두 멤버가 동시에 join해도 판은 한 번만 시작하고 입장 횟수는 1회씩', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    const m2 = await newHero(app);
    await formParty(app, host, [m1, m2]);
    const st = await post(app, host, '/party/start', { ai_count: 0 });
    const runId = st.body.data.run.id as string;
    const tok = async (h: Hero) => (await get(app, h, '/party')).body.data.party.run.me.entry_token as string;
    const [t1, t2] = [await tok(m1), await tok(m2)];
    const [a, b] = await Promise.all([
      post(app, m1, `/party-runs/${runId}/join`, { entry_token: t1 }),
      post(app, m2, `/party-runs/${runId}/join`, { entry_token: t2 }),
    ]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect([a.body.data.begun, b.body.data.begun].filter(Boolean).length).toBe(1);
    const rows = await getPool().query('SELECT character_id FROM dungeon_runs WHERE party_run_id = (SELECT id FROM party_runs WHERE uuid = $1)', [runId]);
    expect(rows.rows.length).toBe(3);
  });

  it('입장 횟수 부족: 멤버가 소진했으면 출발이 MEMBER_NOT_ELIGIBLE', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    await formParty(app, host, [member]);
    await getPool().query(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, reset_day, started_at, state, ended_at)
       SELECT $1, 'gold_vein', 0, 1, $2, $3, 'failed', $3 FROM generate_series(1, 3)`,
      [member.dbId, '2026-10-04T21:00:00Z', MONDAY],
    );
    const st = await post(app, host, '/party/start', { ai_count: 0 });
    expect(st.status).toBe(422);
    expect(st.body.errors.code).toBe('MEMBER_NOT_ELIGIBLE');
    expect(st.body.errors.members[0].code).toBe('NO_ENTRIES_LEFT');
  });
});

describe('파티 판 처치와 정산', () => {
  it('정상: 두 멤버가 각자 처치를 보고하고 결과가 일치하면 각자 보상을 받는다', async () => {
    const { host, member, hostRun, memberRun } = await duo();
    await clearAll(app, host, hostRun, advance);
    // 같은 시계에서 멤버도 같은 처치를 보고한다(시계는 방장이 쓴 만큼 이미 진행됨)
    fixed = new Date(MONDAY);
    advance(1);
    await clearAll(app, member, memberRun, advance);
    advance(40);
    const rep = await post(app, host, `/party-runs/${(await get(app, host, '/party')).body.data.party.run.id}/host-report`, await honestHostReport(hostRun, [host, member]));
    expect(rep.status).toBe(200);
    expect(rep.body.data).toEqual({ accepted: true, epoch: 1 });

    // 방장이 먼저 보고하면 증인(멤버)의 보고를 기다린다
    expect((await result(host, hostRun, { outcome: 'cleared', stats: stats() })).body.data.result).toBe('pending');
    const m = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(m.status).toBe(200);
    expect(m.body.data.result).toBe('cleared');
    expect(m.body.data.granted_xp).toBeGreaterThan(0);
    expect(m.body.data.card_count).toBe(4);
    const h = await settle(host, hostRun);
    expect(h.body.data.result).toBe('cleared');
    // 이미 정산된 행의 settle은 저장된 결과를 돌려준다
    const again = await settle(member, memberRun);
    expect(again.body.data.result).toBe('cleared');
    const pick = await post(app, member, `/dungeon-runs/${memberRun}/cards/pick`, { index: 0 });
    expect(pick.status).toBe(200);
    await expectLedgerConsistent(host);
    await expectLedgerConsistent(member);
    // 판이 끝나 파티가 다시 forming
    const pv = await get(app, host, '/party');
    expect(pv.body.data.party.state).toBe('forming');
    expect(pv.body.data.party.run).toBeNull();
  });

  it('멤버가 먼저 보고하면 방장 보고가 올 때까지 pending, 이후 settle로 확정(재전송은 같은 결과)', async () => {
    const { host, member, runId, hostRun, memberRun } = await duo();
    await clearAll(app, host, hostRun, advance);
    fixed = new Date(MONDAY);
    advance(1);
    await clearAll(app, member, memberRun, advance);
    advance(40);
    const first = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(first.body.data).toEqual({ result: 'pending', settle_after_ms: 2000 });
    const before = await getPool().query('SELECT xp FROM characters WHERE id = $1', [member.dbId]);
    await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(hostRun, [host, member]));
    // 방장이 자기 결과를 보고하기 전에는 H를 믿지 않고 기다린다
    expect((await settle(member, memberRun)).body.data.result).toBe('pending');
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    const rid = randomUUID();
    const s1 = await settle(member, memberRun, rid);
    const s2 = await settle(member, memberRun, rid);
    expect(s1.body.data.result).toBe('cleared');
    expect(s2.body).toEqual(s1.body);
    expect(s2.headers['idempotent-replay']).toBe('true');
    const xpLedger = await getPool().query("SELECT count(*) AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_clear'", [member.dbId]);
    expect(Number((xpLedger.rows[0] as { n: string }).n)).toBe(1);
    void before;
  });

  it('동시 요청: 같은 판의 settle 두 번이 동시에 와도 클리어 경험치는 한 번', async () => {
    const { host, member, runId, hostRun, memberRun } = await duo();
    await clearAll(app, host, hostRun, advance);
    fixed = new Date(MONDAY);
    advance(1);
    await clearAll(app, member, memberRun, advance);
    advance(40);
    await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(hostRun, [host, member]));
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    const [a, b] = await Promise.all([settle(member, memberRun), settle(member, memberRun)]);
    expect([a.body.data.result, b.body.data.result]).toEqual(['cleared', 'cleared']);
    const n = await getPool().query("SELECT count(*) AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'dungeon_clear'", [member.dbId]);
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
    await expectLedgerConsistent(member);
  });

  it('입력 오류와 상태 오류: 잘못된 outcome, 보고 전 settle, 이미 보고한 판', async () => {
    const { member, memberRun } = await duo();
    expect((await result(member, memberRun, { outcome: 'win', stats: stats() })).status).toBe(400);
    expect((await settle(member, memberRun)).body.errors.code).toBe('RUN_NOT_REPORTED');
    // 처치 없이 클리어 보고: 자기 검증에서 보류
    const held = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(held.body.data.result).toBe('held');
    expect((await result(member, memberRun, { outcome: 'failed', stats: stats() })).body.errors.code).toBe('RUN_NOT_PLAYING');
  });

  it('방장 보고와 멤버 보고가 어긋나면 보류(held): 경과 시간 불일치', async () => {
    const { host, member, runId, hostRun, memberRun } = await duo();
    await clearAll(app, host, hostRun, advance);
    fixed = new Date(MONDAY);
    advance(1);
    await clearAll(app, member, memberRun, advance);
    advance(40);
    // 방장은 60초로 보고(유효 범위), 멤버는 80초: 5초 허용을 넘는다
    await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(hostRun, [host, member], { elapsed_ms: 60_000 }));
    const m = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(m.body.data.result).toBe('pending');
    const h = await result(host, hostRun, { outcome: 'cleared', stats: stats({ elapsed_ms: 60_000 }) });
    // 방장 쪽 증인(멤버)의 보고가 방장 관찰과 어긋나므로 방장도 멤버도 보류
    expect(h.body.data.result).toBe('held');
    expect((await settle(member, memberRun)).body.data).toEqual({ result: 'held' });
    expect(await anomalyKinds(member)).toContain('party_result');
    const xp = await getPool().query("SELECT count(*) AS n FROM xp_ledger WHERE reason = 'dungeon_clear' AND character_id = ANY($1::bigint[])", [[host.dbId, member.dbId]]);
    expect(Number((xp.rows[0] as { n: string }).n)).toBe(0);
  });

  it('멤버끼리 일치하고 방장만 어긋나면 멤버는 정산되고 방장은 이상치로 기록된다', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    const m2 = await newHero(app);
    await formParty(app, host, [m1, m2]);
    const { runId, runs } = await startAndBegin(app, host, [m1, m2]);
    const [rh, r1, r2] = [runs.get(host.id), runs.get(m1.id), runs.get(m2.id)] as string[];
    await clearAll(app, host, rh as string, advance);
    for (const [h, r] of [[m1, r1], [m2, r2]] as [Hero, string][]) {
      fixed = new Date(MONDAY);
      advance(1);
      await clearAll(app, h, r, advance);
    }
    advance(40);
    await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(rh as string, [host, m1, m2], { elapsed_ms: 60_000 }));
    expect((await result(host, rh as string, { outcome: 'cleared', stats: stats({ elapsed_ms: 60_000 }) })).body.data.result).toBe('pending');
    // m2가 먼저 보고하면 m1(다른 멤버)의 보고가 없어 방장 보고와 불일치 -> 대기(pending)
    const a = await result(m2, r2 as string, { outcome: 'cleared', stats: stats() });
    expect(a.body.data.result).toBe('pending');
    // m1이 보고하면 m1은 m2와 일치 -> 정산(방장 이상치)
    const b = await result(m1, r1 as string, { outcome: 'cleared', stats: stats() });
    expect(b.body.data.result).toBe('cleared');
    const s = await settle(m2, r2 as string);
    expect(s.body.data.result).toBe('cleared');
    expect(await anomalyKinds(host)).toContain('party_host');
  });

  it('실패 보고는 즉시 닫히고 처치 경험치는 유지된다', async () => {
    const { host, member, hostRun, memberRun } = await duo();
    await clearAll(app, host, hostRun, advance);
    const xpBefore = await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [host.dbId]);
    const r = await result(host, hostRun, { outcome: 'failed', stats: stats() });
    expect(r.body.data).toEqual({ result: 'failed' });
    const xpAfter = await getPool().query('SELECT level, xp FROM characters WHERE id = $1', [host.dbId]);
    expect(xpAfter.rows[0]).toEqual(xpBefore.rows[0]);
    expect((await result(member, memberRun, { outcome: 'failed', stats: stats() })).body.data.result).toBe('failed');
  });

  it('방장이 아닌 멤버의 킬 거절은 차단 카운트(severity 2 이상)에 쌓이지 않는다', async () => {
    const { member, memberRun } = await duo();
    const res = await post(app, member, '/kills', { map_id: 'dgn_canyon_1', monster_id: 'skel_gold', run_id: memberRun, room_index: 2 });
    expect(res.status).toBe(422);
    const sev = await getPool().query("SELECT severity FROM anomaly_log WHERE character_id = $1 AND kind LIKE 'kill%'", [member.dbId]);
    expect(sev.rows.every((r: { severity: number }) => r.severity === 1)).toBe(true);
  });
});

describe('호스트 인계와 방장 보고, 이탈', () => {
  it('하트비트: 12초 무신호는 끊김, 60초 안에 돌아오면 복귀, 넘기면 이탈과 abandoned', async () => {
    const { host, member, runId, memberRun } = await duo();
    advance(5);
    const ok = await raw(app, host, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    expect(ok.status).toBe(200);
    expect(ok.body.data.host_changed).toBe(false);
    advance(20);
    const r = await raw(app, host, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    expect(r.body.data.members.find((x: { slot: number }) => x.slot === 1).state).toBe('disconnected');
    // 복귀
    const back = await raw(app, member, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    expect(back.body.data.me.state).toBe('playing');
    // 다시 끊겨 60초 넘김
    advance(20);
    await raw(app, host, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    advance(70);
    const gone = await raw(app, host, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    expect(gone.body.data.members.find((x: { slot: number }) => x.slot === 1).state).toBe('left');
    const row = await getPool().query('SELECT state FROM dungeon_runs WHERE uuid = $1', [memberRun]);
    expect((row.rows[0] as { state: string }).state).toBe('abandoned');
  });

  it('호스트 인계: 살아 있는 방장은 거절, 죽은 뒤에는 가장 작은 자리만 승인, 세대 증가와 옛 보고 거절', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    const m2 = await newHero(app);
    await formParty(app, host, [m1, m2]);
    const { runId, runs, hostKey } = await startAndBegin(app, host, [m1, m2]);
    expect(hostKey).toBeTruthy();
    const hb = (h: Hero) => raw(app, h, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    await Promise.all([hb(host), hb(m1), hb(m2)]);
    const claim = (h: Hero, epoch = 1, rid?: string) => post(app, h, `/party-runs/${runId}/host/claim`, { observed_epoch: epoch }, rid);
    // 방장이 살아 있다
    expect((await claim(m1)).body.errors.code).toBe('HOST_ALIVE');
    // 방장 하트비트가 끊김(12초 초과), 멤버들은 계속 신호
    advance(20);
    await Promise.all([hb(m1), hb(m2)]);
    // 큰 자리(m2)는 거절
    const no = await claim(m2);
    expect(no.status).toBe(409);
    expect(no.body.errors.code).toBe('NOT_NEXT_HOST');
    expect(no.body.errors.next_slot).toBe(1);
    const rid = randomUUID();
    const yes = await claim(m1, 1, rid);
    expect(yes.status).toBe(200);
    expect(yes.body.data.host.epoch).toBe(2);
    expect(yes.body.data.host.character_id).toBe(m1.id);
    expect(yes.body.data.host_key).toBeTruthy();
    // 재전송은 같은 응답
    expect((await claim(m1, 1, rid)).body).toEqual(yes.body);
    // 이미 인계됨: 옛 세대로 요청하면 HOST_CHANGED + 현재 호스트
    const late = await claim(m2, 1);
    expect(late.body.errors.code).toBe('HOST_CHANGED');
    expect(late.body.errors.host.epoch).toBe(2);
    // 하트비트가 새 세대를 알린다
    expect((await hb(m2)).body.data.host_changed).toBe(true);
    // 옛 방장의 방장 보고는 거절, 새 방장의 보고는 받는다
    const old = await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(runs.get(host.id) as string, [host, m1, m2]));
    expect(old.status).toBe(409);
    expect(old.body.errors.code).toBe('HOST_EPOCH_STALE');
    const fresh = await post(app, m1, `/party-runs/${runId}/host-report`, await honestHostReport(runs.get(m1.id) as string, [host, m1, m2], { host_epoch: 2 }));
    expect(fresh.status).toBe(200);
    expect(fresh.body.data.epoch).toBe(2);
    // 같은 세대에 다른 request_id로 또 보내면 거절
    const dup = await post(app, m1, `/party-runs/${runId}/host-report`, await honestHostReport(runs.get(m1.id) as string, [host, m1, m2], { host_epoch: 2 }));
    expect(dup.body.errors.code).toBe('REPORT_EXISTS');
  });

  it('동시 요청: 두 멤버가 동시에 인계를 요청해도 하나만 승인된다', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    const m2 = await newHero(app);
    await formParty(app, host, [m1, m2]);
    const { runId } = await startAndBegin(app, host, [m1, m2]);
    const hb = (h: Hero) => raw(app, h, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    advance(20);
    await Promise.all([hb(m1), hb(m2)]);
    const claim = (h: Hero) => post(app, h, `/party-runs/${runId}/host/claim`, { observed_epoch: 1 });
    const [a, b] = await Promise.all([claim(m1), claim(m2)]);
    expect([a.status, b.status].sort()).toEqual([200, 409]);
    const run = await getPool().query('SELECT host_epoch FROM party_runs WHERE uuid = $1', [runId]);
    expect((run.rows[0] as { host_epoch: number }).host_epoch).toBe(2);
  });

  it('방장 보고 입력 오류: 경험치 같은 지급 값은 받지 않는다(400)', async () => {
    const { host, runId, hostRun, member } = await duo();
    const body = await honestHostReport(hostRun, [host, member]);
    expect((await post(app, host, `/party-runs/${runId}/host-report`, { ...body, granted_xp: 999 })).status).toBe(400);
    expect((await post(app, member, `/party-runs/${runId}/host-report`, body)).body.errors.code).toBe('NOT_HOST');
  });

  it('이탈: 판 도중 나가면 abandoned이고 이후 처치 보고는 RUN_NOT_PLAYING, 방장이 나가면 바로 인계 가능', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    await formParty(app, host, [m1]);
    const { runId, runs } = await startAndBegin(app, host, [m1]);
    const lv = await post(app, host, `/party-runs/${runId}/leave`, {});
    expect(lv.body.data).toEqual({ left: true });
    const k = await post(app, host, '/kills', { map_id: 'dgn_canyon_1', monster_id: 'skel_gold', run_id: runs.get(host.id), room_index: 0 });
    expect(k.body.errors.code).toBe('RUN_NOT_PLAYING');
    const row = await getPool().query('SELECT state FROM dungeon_runs WHERE uuid = $1', [runs.get(host.id)]);
    expect((row.rows[0] as { state: string }).state).toBe('abandoned');
    // 이미 나갔으면 다시 불러도 성공
    expect((await post(app, host, `/party-runs/${runId}/leave`, {})).body.data.left).toBe(true);
    // 12초를 기다리지 않고 인계
    const hb = await raw(app, m1, `/party-runs/${runId}/heartbeat`, { seen_epoch: 1 });
    expect(hb.status).toBe(200);
    const claim = await post(app, m1, `/party-runs/${runId}/host/claim`, { observed_epoch: 1 });
    expect(claim.status).toBe(200);
  });

  it('입장 마감: 들어오지 않은 멤버는 불참 처리되고 AI가 그 자리를 채우며 입장 횟수는 쓰지 않는다', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    await formParty(app, host, [member]);
    const st = await post(app, host, '/party/start', { ai_count: 0 });
    const runId = st.body.data.run.id as string;
    advance(95);
    const view = await get(app, host, `/party-runs/${runId}`);
    expect(view.body.data.run.state).toBe('playing');
    expect(view.body.data.run).toMatchObject({ humans: 1, ai_count: 1 });
    expect(view.body.data.run.members.find((m: { character_id: string }) => m.character_id === member.id).state).toBe('no_show');
    expect((await get(app, member, '/dungeons')).body.data.entries.used).toBe(0);
    expect((await get(app, host, '/dungeons')).body.data.entries.used).toBe(1);
  });
});
