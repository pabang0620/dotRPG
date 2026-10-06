// 9단계 9절: 멤버 카드 대조용 서버 값(worn, career, gear_hash)과 card_mismatch 기록
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { anomalyKinds, fakeRng, seedWorn } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { clearAll, formParty, get, honestHostReport, newHero, post, raw, stats, startAndBegin, type Hero } from './partyHelpers';

const app = buildApp();
const MONDAY = '2026-10-05T03:00:00Z';
let fixed = new Date(MONDAY);
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};
beforeEach(async () => {
  await resetDb();
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const memberCardRows = async (h: Hero): Promise<number> =>
  Number(((await getPool().query("SELECT count(*) AS n FROM anomaly_log WHERE kind = 'member_card' AND character_id = $1", [h.dbId])).rows[0] as { n: string }).n);

async function team(n = 2): Promise<{ L: Hero; M: Hero[]; sid: string; all: Hero[] }> {
  const all: Hero[] = [];
  for (let i = 0; i < n; i++) all.push(await newHero(app));
  await formParty(app, all[0] as Hero, all.slice(1));
  const first = await post(app, all[0] as Hero, '/field-sessions/enter', { map_id: 'forest' });
  const sid = first.body.data.session.id as string;
  for (const h of all.slice(1)) await post(app, h, '/field-sessions/enter', { map_id: 'forest' });
  for (const h of all) await raw(app, h, `/field-sessions/${sid}/heartbeat`, { seen_epoch: 1, synced: true });
  return { L: all[0] as Hero, M: all.slice(1), sid, all };
}

describe('9.1 서버 뷰: worn, career, gear_hash', () => {
  it('필드 세션 뷰의 members[]에 서버가 아는 착용 장비 원본과 서버 기록 전직이 실린다(장비를 바꾸면 gear_hash와 worn이 같이 바뀐다)', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    const view = async () => (await get(app, L, `/field-sessions/${sid}`)).body.data.session.members as { character_id: string; worn: { slot: number; item_key: string }[]; career: number; gear_hash: string }[];
    const before = (await view()).find((x) => x.character_id === m.id) as ReturnType<typeof view> extends Promise<(infer T)[]> ? T : never;
    const db = await getPool().query("SELECT slot, item_key FROM character_items WHERE character_id = $1 AND location = 'worn' ORDER BY slot", [m.dbId]);
    expect(before.worn).toEqual(db.rows);
    expect(before.worn.length).toBeGreaterThan(0);
    expect(before.career).toBe(0);
    await seedWorn(m, 3, 'eq_ring_1_c');
    await getPool().query("INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, 2, 0, 'promote')", [m.dbId]);
    const after = (await view()).find((x) => x.character_id === m.id) as typeof before;
    expect(after.worn).toEqual([...before.worn, { slot: 3, item_key: 'eq_ring_1_c' }].sort((a, b) => a.slot - b.slot));
    expect(after.gear_hash).not.toBe(before.gear_hash);
    expect(after.career).toBe(2);
    expect(after.worn.length).toBeLessThanOrEqual(8);
  });

  it('파티 판 뷰(RunView)의 members[]에도 같은 값이 실린다', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    await formParty(app, host, [member]);
    const { runId } = await startAndBegin(app, host, [member]);
    const run = (await get(app, host, `/party-runs/${runId}`)).body.data.run;
    expect(run.members).toHaveLength(2);
    for (const m of run.members) {
      expect(m).toMatchObject({ career: 0 });
      expect(m.gear_hash).toMatch(/^[0-9a-f]{16}$/);
      expect(m.worn.length).toBeGreaterThan(0);
    }
  });
});

describe('9.3 card_mismatch 기록', () => {
  it('F6 observe: 활성 멤버(호스트 제외)의 불일치는 member_card 한 건, 같은 세션에서 다시 보내도 한 건. 알 수 없는 좌석·호스트 본인은 무시', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    const credit = (over: Record<string, unknown>) => post(app, L, `/field-sessions/${sid}/observe`, { host_epoch: 1, window_ms: 10_000, credits: [over] });
    advance(10);
    expect((await credit({ seat: 1, kills: 0, card_mismatch: true })).status).toBe(200);
    expect(await memberCardRows(m)).toBe(1);
    advance(10);
    expect((await credit({ seat: 1, kills: 0, card_mismatch: true })).status).toBe(200);
    expect(await memberCardRows(m)).toBe(1);
    const detail = await getPool().query("SELECT severity, detail FROM anomaly_log WHERE kind = 'member_card'");
    expect(detail.rows[0]).toMatchObject({ severity: 2, detail: { session_id: sid, character_id: m.id } });
    advance(10);
    expect((await credit({ seat: 0, kills: 0, card_mismatch: true })).status).toBe(200); // 호스트 본인
    expect((await credit({ seat: 3, kills: 0, card_mismatch: true })).status).toBe(200); // 없는 좌석
    expect(await memberCardRows(L)).toBe(0);
    expect((await getPool().query("SELECT count(*) AS n FROM anomaly_log WHERE kind = 'member_card'")).rows[0]?.n).toBe('1');
    expect(await anomalyKinds(m)).toContain('member_card');
  });

  it('방장 보고 members[].card_mismatch도 같다. 판의 멤버가 아닌 캐릭터는 무시하고, 판 결과에는 쓰이지 않는다', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    const stranger = await newHero(app);
    await formParty(app, host, [member]);
    const { runId, runs } = await startAndBegin(app, host, [member]);
    const hostRun = runs.get(host.id) as string;
    const body = await honestHostReport(hostRun, [host, member]);
    (body.members as Record<string, unknown>[])[1] = { ...(body.members as Record<string, unknown>[])[1], card_mismatch: true };
    (body.members as Record<string, unknown>[])[0] = { ...(body.members as Record<string, unknown>[])[0], card_mismatch: true };
    const rep = await post(app, host, `/party-runs/${runId}/host-report`, { ...body, members: [...(body.members as unknown[]), { character_id: stranger.id, hits_taken: 0, max_combo: 0, revives_used: 0, damage_dealt: 0, card_mismatch: true }] });
    expect(rep.status).toBe(200);
    expect(await memberCardRows(member)).toBe(1);
    expect(await memberCardRows(host)).toBe(0);
    expect(await memberCardRows(stranger)).toBe(0);
  });

  it('카드 위조가 정산을 왜곡하지 못한다: 서버 레벨로 계산한 attackCap을 넘는 멤버 피해는 DAMAGE_OVER_CAP으로 방장 보고가 무효', async () => {
    const host = await newHero(app);
    const member = await newHero(app);
    await formParty(app, host, [member]);
    const { runId, runs } = await startAndBegin(app, host, [member]);
    const hostRun = runs.get(host.id) as string;
    const memberRun = runs.get(member.id) as string;
    await clearAll(app, host, hostRun, advance);
    fixed = new Date(new Date(MONDAY).getTime() + 1000);
    await clearAll(app, member, memberRun, advance);
    advance(40);
    const body = await honestHostReport(hostRun, [host, member]);
    // 멤버가 레벨 999 카드를 보냈다고 해도 서버는 레벨 1로 상한을 계산한다
    (body.members as { damage_dealt: number }[])[1]!.damage_dealt = 2_000_000_000;
    expect((await post(app, host, `/party-runs/${runId}/host-report`, body)).status).toBe(200);
    await post(app, member, `/dungeon-runs/${memberRun}/result`, { outcome: 'cleared', stats: stats() });
    await post(app, host, `/dungeon-runs/${hostRun}/result`, { outcome: 'cleared', stats: stats() });
    const r = await getPool().query("SELECT detail->'reasons' AS reasons FROM anomaly_log WHERE kind = 'party_host' AND character_id = $1", [host.dbId]);
    expect(r.rows.flatMap((x) => x.reasons)).toContain('DAMAGE_OVER_CAP');
  });
});
