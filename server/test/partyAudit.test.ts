import { getPool } from '../src/db/pool';
import { runMatchTick } from '../src/domains/match/matchService';
import { getQueueStore } from '../src/domains/match/queueStore';
import * as partyRepo from '../src/domains/party/partyRepository';
import { runSettleTick } from '../src/domains/partyruns/partySettle';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown } from './helpers';
import { anomalyKinds, fakeRng, seedClaims, seedLevel } from './economyHelpers';
import { clearAll, createParty, formParty, get, honestHostReport, newHero, post, raidEntryLevel, roomsOf, stats, startAndBegin, type Hero } from './partyHelpers';

const app = buildApp();
const MONDAY = '2026-10-05T03:00:00Z';
let fixed = new Date(MONDAY);
const at = (ms: number) => {
  fixed = new Date(ms);
};
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeEach(async () => {
  await resetDb();
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
  getQueueStore().clear();
});
afterAll(async () => {
  jest.restoreAllMocks();
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const result = (h: Hero, runUuid: string, body: Record<string, unknown>) => post(app, h, `/dungeon-runs/${runUuid}/result`, body);
const settle = (h: Hero, runUuid: string) => post(app, h, `/dungeon-runs/${runUuid}/settle`, {});

/** 방장 + 멤버가 같은 판에서 모든 방을 처치한 상태(결과 보고 전) */
async function playedDuo(aiCount = 0) {
  const host = await newHero(app);
  const member = await newHero(app);
  await formParty(app, host, [member]);
  const { runId, runs } = await startAndBegin(app, host, [member], aiCount);
  const hostRun = runs.get(host.id) as string;
  const memberRun = runs.get(member.id) as string;
  await clearAll(app, host, hostRun, advance);
  at(new Date(MONDAY).getTime() + 1000);
  await clearAll(app, member, memberRun, advance);
  advance(40);
  return { host, member, runId, hostRun, memberRun };
}

const reasonsOf = async (h: Hero, kind: string): Promise<string[]> => {
  const r = await getPool().query<{ reasons: string[] }>(
    "SELECT detail->'reasons' AS reasons FROM anomaly_log WHERE character_id = $1 AND kind = $2",
    [h.dbId, kind],
  );
  return r.rows.flatMap((x) => x.reasons);
};
const stateOf = async (runUuid: string): Promise<string> =>
  ((await getPool().query('SELECT state FROM dungeon_runs WHERE uuid = $1', [runUuid])).rows[0] as { state: string }).state;

describe('감사 1: 방장 보고(H)는 방장 자신의 결과 보고와 같아야 한다', () => {
  it('방장은 failed, H는 cleared: 멤버는 보상을 받지 못하고(pending 후 held) 방장은 기록된다', async () => {
    const { host, member, runId, hostRun, memberRun } = await playedDuo();
    expect((await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(hostRun, [host, member]))).status).toBe(200);
    expect((await result(host, hostRun, { outcome: 'failed', stats: stats() })).body.data.result).toBe('failed');
    const m = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(m.body.data.result).toBe('pending');
    advance(91);
    const s = await settle(member, memberRun);
    expect(s.body.data).toEqual({ result: 'held' });
    expect(await reasonsOf(host, 'party_host')).toContain('HOST_SELF_MISMATCH');
    const xp = await getPool().query("SELECT count(*) AS n FROM xp_ledger WHERE reason = 'dungeon_clear' AND character_id = $1", [member.dbId]);
    expect(Number((xp.rows[0] as { n: string }).n)).toBe(0);
  });
});

describe('감사 2: 레이드 최소 인원은 클리어를 유지한 사람만 센다', () => {
  it('2명 레이드에서 한 명이 바로 실패하면 남은 사람도 보상을 받지 못한다', async () => {
    fixed = new Date('2026-10-07T03:00:00Z');
    const mk = async () => {
      const h = await newHero(app);
      await seedLevel(h, raidEntryLevel());
      await seedClaims(h, ['c1_fortress']);
      return h;
    };
    const host = await mk();
    const member = await mk();
    await formParty(app, host, [member], { dungeon_id: 'raid_skeleton_king', difficulty: 0 });
    const { runId, runs } = await startAndBegin(app, host, [member]);
    expect((await result(member, runs.get(member.id) as string, { outcome: 'failed', stats: stats() })).body.data.result).toBe('failed');
    const t0 = fixed.getTime();
    await clearAll(app, host, runs.get(host.id) as string, advance, 'raid_skeleton_king', 20);
    at(t0 + 300_000);
    await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(runs.get(host.id) as string, [host, member], { elapsed_ms: 300_000 }, 'raid_skeleton_king'));
    const res = await result(host, runs.get(host.id) as string, { outcome: 'cleared', stats: stats({ elapsed_ms: 300_000, max_combo: 300 }) });
    // 실패한 사람은 증인으로 어긋나 보류되거나, 어느 쪽이든 최소 인원 잠금에 걸린다. 보상은 없다
    const out = res.body.data;
    expect(out.result === 'held' || (out.result === 'cleared' && out.raid.lock_reason === 'TOO_FEW_HUMANS')).toBe(true);
    expect(out.granted_xp ?? 0).toBe(0);
    const claims = await getPool().query('SELECT count(*) AS n FROM raid_claims WHERE character_id = $1', [host.dbId]);
    expect(Number((claims.rows[0] as { n: string }).n)).toBe(0);
  });
});

describe('감사 3: 방장 보고 멤버 목록과 극단값', () => {
  it('같은 character_id가 두 번 들어간 방장 보고는 400', async () => {
    const { host, member, runId, hostRun } = await playedDuo();
    const body = await honestHostReport(hostRun, [host, member]);
    const dup = (body.members as { character_id: string }[])[0] as { character_id: string };
    const res = await post(app, host, `/party-runs/${runId}/host-report`, { ...body, members: [...(body.members as unknown[]), dup] });
    expect(res.status).toBe(400);
  });

  it('방장이 멤버의 피격만 999로 보고하면 멤버 자기 값(2)으로 정산되고 방장이 기록된다', async () => {
    const { host, member, runId, hostRun, memberRun } = await playedDuo();
    const body = await honestHostReport(hostRun, [host, member]);
    const members = (body.members as { character_id: string; hits_taken: number }[]).map((m) => (m.character_id === member.id ? { ...m, hits_taken: 999 } : m));
    await post(app, host, `/party-runs/${runId}/host-report`, { ...body, members });
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    const m = await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    expect(m.body.data.result).toBe('cleared');
    const row = await getPool().query("SELECT stats->>'hits_taken' AS hits FROM dungeon_runs WHERE uuid = $1", [memberRun]);
    expect((row.rows[0] as { hits: string }).hits).toBe('2');
    expect(await reasonsOf(host, 'party_host')).toContain('HOST_STATS_OUTLIER');
  });
});

describe('감사 4: 정산을 부르지 않아 멈춘 reported 행은 서버 틱이 닫는다', () => {
  it('대기 마감 전에는 건드리지 않고, 마감 뒤에는 정산할 수 없으면 held로 닫는다', async () => {
    const { member, memberRun } = await playedDuo();
    expect((await result(member, memberRun, { outcome: 'cleared', stats: stats() })).body.data.result).toBe('pending');
    await runSettleTick();
    expect(await stateOf(memberRun)).toBe('reported');
    advance(91);
    await runSettleTick();
    expect(await stateOf(memberRun)).toBe('held');
    expect(await anomalyKinds(member)).toContain('party_result');
    // 이미 닫힌 행은 다시 건드리지 않는다
    await runSettleTick();
    expect(await stateOf(memberRun)).toBe('held');
  });

  it('마감 뒤 방장 보고와 멤버 보고가 맞으면 틱이 정산(보상 한 번)한다', async () => {
    const { host, member, runId, hostRun, memberRun } = await playedDuo();
    await post(app, host, `/party-runs/${runId}/host-report`, await honestHostReport(hostRun, [host, member]));
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    // 멤버는 보고만 하고 settle을 부르지 않는다: 보고 응답이 이미 정산했을 수 있으므로 상태만 확인
    await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    advance(91);
    await runSettleTick();
    await runSettleTick();
    expect(await stateOf(memberRun)).toBe('cleared');
    expect(await stateOf(hostRun)).toBe('cleared');
    const xp = await getPool().query("SELECT count(*) AS n FROM xp_ledger WHERE reason = 'dungeon_clear' AND character_id = ANY($1::bigint[])", [[host.dbId, member.dbId]]);
    expect(Number((xp.rows[0] as { n: string }).n)).toBe(2);
  });
});

describe('감사 5: 방장 보고의 AI 항목', () => {
  it('ai_count보다 많은 AI 항목과 용병 한도를 넘는 AI 딜은 H를 무효로 만든다', async () => {
    const { host, member, runId, hostRun, memberRun } = await playedDuo(1);
    const body = await honestHostReport(hostRun, [host, member]);
    await post(app, host, `/party-runs/${runId}/host-report`, {
      ...body,
      ai: [{ slot: 2, damage_dealt: 2_000_000_000 }],
    });
    await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    expect(await reasonsOf(host, 'party_host')).toContain('AI_DAMAGE_OVER_CAP');
  });

  it('ai_count가 0인데 AI 항목이 있으면 AI_OVER_COUNT', async () => {
    const { host, member, runId, hostRun, memberRun } = await playedDuo(0);
    const body = await honestHostReport(hostRun, [host, member]);
    await post(app, host, `/party-runs/${runId}/host-report`, { ...body, ai: [{ slot: 2, damage_dealt: 1 }] });
    await result(member, memberRun, { outcome: 'cleared', stats: stats() });
    await result(host, hostRun, { outcome: 'cleared', stats: stats() });
    expect(await reasonsOf(host, 'party_host')).toContain('AI_OVER_COUNT');
  });
});

describe('감사 6: 매칭 파티 구성 중 UNIQUE 위반은 그 멤버만 건너뛴다', () => {
  it('다른 파티에 막 들어간 대기자는 빠지고 나머지로 파티가 만들어지며 티켓은 사라진다', async () => {
    const a = await newHero(app);
    const b = await newHero(app);
    const c = await newHero(app);
    const other = await createParty(app, b);
    expect(other.status).toBe(201);
    for (const h of [a, c]) await post(app, h, '/match/queue', { dungeon_id: 'gold_vein', difficulty: 0 });
    // b는 이미 파티에 있어 대기에 못 들어가므로 티켓을 직접 넣어 경쟁 상황을 만든다
    getQueueStore().put({
      characterId: b.dbId, accountId: 0, dungeonId: 'gold_vein', difficulty: 0, level: 1, power: 570,
      queuedAt: fixed, lastPollAt: fixed, forceDepart: false,
    });
    // 사전 확인은 통과(경쟁으로 아직 안 보임)하고 INSERT에서만 UNIQUE에 걸리게 한다
    const real = partyRepo.findPartyIdOf;
    jest.spyOn(partyRepo, 'findPartyIdOf').mockImplementation(async (db, id) => (id === b.dbId ? null : real(db, id)));
    await runMatchTick(); // 4명 미만이라 아직 안 맞춘다
    advance(5);
    getQueueStore().listByKey('gold_vein', 0).forEach((t) => (t.lastPollAt = fixed));
    const seedTicket = getQueueStore().listByKey('gold_vein', 0)[0];
    if (seedTicket) seedTicket.forceDepart = true;
    await runMatchTick();
    jest.restoreAllMocks();
    expect(getQueueStore().listByKey('gold_vein', 0)).toHaveLength(0);
    const pa = (await get(app, a, '/party')).body.data.party;
    expect(pa).not.toBeNull();
    expect(pa.source).toBe('match');
    const ids = pa.members.map((m: { character_id: string }) => m.character_id);
    expect(ids).toContain(a.id);
    expect(ids).not.toContain(b.id);
    // b는 원래 파티에 그대로
    expect((await get(app, b, '/party')).body.data.party.id).toBe(other.body.data.party.id);
  });
});

void roomsOf;
