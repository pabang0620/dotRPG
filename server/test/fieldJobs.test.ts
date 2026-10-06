import { getPool } from '../src/db/pool';
import { runJob, resetJobStop } from '../src/ops/jobRunner';
import { registerAllJobs } from '../src/ops/jobs';
import { integrityJob } from '../src/ops/jobs/integrity';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, pinClockToMondayFlowing, resetDb, shutdown } from './helpers';
import { fakeRng } from './economyHelpers';
import { formParty, newHero, post, type Hero } from './partyHelpers';

const app = buildApp({ PURGE_BATCH: '5' });
registerAllJobs();
beforeAll(async () => {
  pinClockToMondayFlowing();
  await resetDb();
  resetJobStop();
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  await shutdown();
});

const q = async <T = Record<string, unknown>>(sql: string, p: unknown[] = []): Promise<T[]> => (await getPool().query(sql, p)).rows as T[];

async function endedSession(withKill: boolean): Promise<string> {
  const heroes: Hero[] = [await newHero(app), await newHero(app)];
  await formParty(app, heroes[0] as Hero, [heroes[1] as Hero]);
  const sid = (await post(app, heroes[0] as Hero, '/field-sessions/enter', { map_id: 'forest' })).body.data.session.id as string;
  await post(app, heroes[1] as Hero, '/field-sessions/enter', { map_id: 'forest' });
  if (withKill) {
    setRng(fakeRng({ unit: 1 }));
    expect((await post(app, heroes[1] as Hero, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: sid, monster_ref: 1 })).status).toBe(200);
  }
  for (const h of heroes) await post(app, h, `/field-sessions/${sid}/leave`, {});
  return sid;
}

describe('필드 세션 정리 작업', () => {
  it('purge-daily: 종료 후 30일이 지난 세션과 멤버, 90일 지난 relay_room_stats 를 지운다. kill_log 가 가리키는 세션은 남긴다', async () => {
    const plain = await endedSession(false);
    const referenced = await endedSession(true);
    const recent = await endedSession(false);
    await q("UPDATE field_sessions SET ended_at = now() - interval '31 days' WHERE uuid = ANY($1::uuid[])", [[plain, referenced]]);
    await q("INSERT INTO relay_room_stats (room_kind, room_ref, started_at, ended_at, peak_peers) VALUES ('field', gen_random_uuid(), now() - interval '100 days', now() - interval '100 days', 2), ('field', gen_random_uuid(), now(), now(), 2)");
    const r = await runJob('purge-daily', 'manual');
    expect(r.status).toBe('ok');
    const left = (await q<{ uuid: string }>('SELECT uuid FROM field_sessions ORDER BY id')).map((x) => x.uuid);
    expect(left).toContain(referenced);
    expect(left).toContain(recent);
    expect(left).not.toContain(plain);
    expect(Number((await q<{ n: string }>('SELECT count(*) AS n FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1', [plain]))[0]?.n)).toBe(0);
    expect(Number((await q<{ n: string }>('SELECT count(*) AS n FROM relay_room_stats'))[0]?.n)).toBe(1);
  });

  it('integrity-nightly I4: 2시간 넘게 활동 없는 활성 필드 세션과 활성 멤버가 없는 활성 세션을 잡는다', async () => {
    const heroes: Hero[] = [await newHero(app), await newHero(app)];
    await formParty(app, heroes[0] as Hero, [heroes[1] as Hero]);
    const sid = (await post(app, heroes[0] as Hero, '/field-sessions/enter', { map_id: 'forest' })).body.data.session.id as string;
    await q("UPDATE field_sessions SET last_active_at = now() - interval '3 hours' WHERE uuid = $1", [sid]);
    const r = await integrityJob({ opts: { full: false }, shouldStop: () => false } as never);
    const i4 = (r.detail as { checks: { I4: { samples: { kind: string }[] } } }).checks.I4;
    expect(i4.samples.map((s) => s.kind)).toContain('field_session_stuck');
    await q("UPDATE field_session_members SET state = 'left', left_reason = 'left', left_at = now() WHERE session_id = (SELECT id FROM field_sessions WHERE uuid = $1)", [sid]);
    const r2 = await integrityJob({ opts: { full: false }, shouldStop: () => false } as never);
    expect((r2.detail as { checks: { I4: { samples: { kind: string }[] } } }).checks.I4.samples.map((s) => s.kind)).toContain('field_session_empty');
  });
});
