// 9단계 15절·12.8: 보관 정리, 작업 등록, 정합성 점검 정보 항목, 스냅샷 지표와 경보
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { hourStart, HOUR_MS } from '../src/domains/antiabuse/incomeMeter';
import { evaluate } from '../src/ops/alertRules';
import { listJobs, registerJob, runJob, resetJobStop } from '../src/ops/jobRunner';
import { registerAllJobs, MANUAL_JOBS } from '../src/ops/jobs';
import { integrityJob } from '../src/ops/jobs/integrity';
import { collectSnapshot } from '../src/ops/snapshot';
import { newHero, post, type Hero } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';

const app = buildApp({ PURGE_BATCH: '2' });
registerAllJobs();
void registerJob;

beforeEach(async () => {
  await resetDb();
  resetJobStop();
});
afterAll(shutdown);

const q = async <T = Record<string, unknown>>(sql: string, p: unknown[] = []): Promise<T[]> => (await getPool().query(sql, p)).rows as T[];
const count = async (table: string, where = 'true'): Promise<number> => Number((await q<{ n: string }>(`SELECT count(*) AS n FROM ${table} WHERE ${where}`))[0]?.n);
const ago = (days: number): Date => new Date(Date.now() - days * 86_400_000);
const accountOf = async (h: Hero): Promise<number> => Number((await q<{ account_id: string }>('SELECT account_id FROM characters WHERE id = $1', [h.dbId]))[0]?.account_id);

describe('작업 등록', () => {
  it('presence-sweep(1분), economy-hold-sweep, income-reconcile(매일)이 등록되고 수동 실행 허용 목록에 있다', () => {
    const byName = new Map(listJobs().map((j) => [j.name, j]));
    expect(byName.get('presence-sweep')?.schedule).toEqual({ kind: 'every', minutes: 1 });
    expect(byName.get('economy-hold-sweep')?.schedule).toEqual({ kind: 'every', minutes: 10 });
    expect(byName.get('income-reconcile')?.schedule).toMatchObject({ kind: 'daily_kst' });
    for (const n of ['presence-sweep', 'economy-hold-sweep', 'income-reconcile']) expect(MANUAL_JOBS).toContain(n);
  });

  it('세 작업이 모두 ok로 끝나고 결과가 job_runs에 기록된다. presence-sweep은 신선도를 잃은 행을 timeout으로 닫는다', async () => {
    const h = await newHero(app);
    await q(
      `INSERT INTO online_sessions (account_id, character_id, family_id, map_id, map_since, map_changed_at, last_seen_at)
       VALUES ($1, $2, $3, 'village', now() - interval '10 minutes', now() - interval '10 minutes', now() - interval '10 minutes')`,
      [await accountOf(h), h.dbId, randomUUID()],
    );
    const sweep = await runJob('presence-sweep', 'manual');
    expect(sweep).toMatchObject({ status: 'ok', detail: { closed: 1 } });
    expect((await q('SELECT end_reason FROM online_sessions'))[0]).toEqual({ end_reason: 'timeout' });
    expect((await runJob('economy-hold-sweep', 'manual')).status).toBe('ok');
    const rec = await runJob('income-reconcile', 'manual');
    expect(rec).toMatchObject({ status: 'ok', detail: { mismatched: 0 } });
    expect(await count('job_runs', "job IN ('presence-sweep', 'economy-hold-sweep', 'income-reconcile') AND status = 'ok'")).toBe(3);
  });
});

describe('보관 정리(purge-daily)', () => {
  it('login_events 90일, 기기·IP 집계 180일, 끝난 프레즌스 7일, 활동 시간·소득 집계 35일을 지우고 안쪽 경계는 남긴다', async () => {
    const h = await newHero(app);
    const acct = await accountOf(h);
    const dev = (c: string): string => c.repeat(64);
    for (const d of [91, 89]) await q("INSERT INTO login_events (account_id, kind, created_at) VALUES ($1, 'login', $2)", [acct, ago(d)]);
    for (const [c, d] of [['a', 181], ['b', 179]] as const) {
      await q('INSERT INTO account_devices (account_id, device_hash, last_seen_at) VALUES ($1, $2, $3)', [acct, dev(c), ago(d)]);
      await q('INSERT INTO account_ips (account_id, ip, last_seen_at) VALUES ($1, $2, $3)', [acct, c === 'a' ? '10.0.0.1' : '10.0.0.2', ago(d)]);
    }
    for (const d of [36, 34]) {
      const hour = hourStart(ago(d));
      await q('INSERT INTO play_time_hourly (character_id, hour_start, active_seconds) VALUES ($1, $2, 60)', [h.dbId, hour]);
      await q('INSERT INTO income_hourly (character_id, hour_start, level_max, xp) VALUES ($1, $2, 1, 5)', [h.dbId, hour]);
    }
    await q(
      `INSERT INTO online_sessions (account_id, character_id, family_id, map_id, map_since, map_changed_at, last_seen_at, ended_at, end_reason)
       VALUES ($1, $2, $3, 'village', $4, $4, $4, $4, 'leave')`,
      [acct, h.dbId, randomUUID(), ago(8)],
    );
    const r = await runJob('purge-daily', 'manual');
    expect(r.status).toBe('ok');
    // 가입 때 남은 오늘 기록(register, 127.0.0.1)은 안쪽 경계라 남는다
    expect(await count('login_events')).toBe(2);
    expect(await count('account_devices')).toBe(1);
    expect(await count('account_ips')).toBe(2);
    expect(await count('account_ips', "ip = '10.0.0.1'")).toBe(0);
    expect(await count('play_time_hourly')).toBe(1);
    expect(await count('income_hourly')).toBe(1);
    expect(await count('online_sessions')).toBe(0);
    // 제재·감사 기록은 지우지 않는다
    await q("INSERT INTO economy_holds (account_id, character_id, kind, state, evidence, created_at) VALUES ($1, $2, 'velocity', 'shadow', '{}'::jsonb, $3)", [acct, h.dbId, ago(400)]);
    await runJob('purge-daily', 'manual');
    expect(await count('economy_holds')).toBe(1);
  });
});

describe('정합성 점검의 9단계 정보 항목(경보의 mismatches에는 넣지 않는다)', () => {
  it('I-income, I-names, I-career, I-holds', async () => {
    const h = await newHero(app);
    await post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    await q('UPDATE income_hourly SET xp = xp + 5 WHERE character_id = $1', [h.dbId]);
    await q("UPDATE characters SET name = '운영자' WHERE id = $1", [h.dbId]);
    await q("UPDATE character_state SET career = '{\"schema\":1,\"career\":1,\"nodes\":[],\"training\":[],\"refunded\":0,\"questStage\":0,\"awakened\":false}'::jsonb WHERE character_id = $1", [h.dbId]);
    await q("INSERT INTO economy_holds (account_id, character_id, kind, state, evidence, created_at) VALUES ($1, $2, 'velocity', 'active', '{}'::jsonb, now() - interval '30 hours')", [await accountOf(h), h.dbId]);
    const res = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    const info = res.detail.info as Record<string, { count: number }>;
    expect(info['I-income']?.count).toBe(1);
    expect(info['I-names']?.count).toBe(1);
    expect(info['I-career']?.count).toBe(1);
    expect(info['I-holds']?.count).toBe(1);
    expect(res.detail.mismatches).toBe(0);
  });
});

describe('스냅샷 지표와 경보', () => {
  it('presence·economy 지표가 스냅샷에 실리고, 24시간 신규 active 정지가 5건 이상이면 경고', async () => {
    const heroes: Hero[] = [];
    for (let i = 0; i < 5; i++) heroes.push(await newHero(app));
    for (const h of heroes) {
      await q("INSERT INTO economy_holds (account_id, character_id, kind, state, evidence) VALUES ($1, $2, 'velocity', 'active', '{}'::jsonb)", [await accountOf(h), h.dbId]);
    }
    await q("INSERT INTO economy_holds (account_id, character_id, kind, state, evidence) VALUES ($1, $2, 'velocity', 'shadow', '{}'::jsonb)", [await accountOf(heroes[0] as Hero), (heroes[0] as Hero).dbId]);
    const snap = await collectSnapshot();
    expect(snap.economy).toMatchObject({ holds_active: 5, holds_active_24h: 5, holds_shadow_24h: 1 });
    expect(snap.presence).toMatchObject({ online: 0 });
    const keys = evaluate({ snap, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 }).map((a) => `${a.level}:${a.key}`);
    expect(keys).toContain('warning:hold_burst');
    const calm = { ...snap, economy: { ...(snap.economy as NonNullable<typeof snap.economy>), holds_active_24h: 4 } };
    expect(evaluate({ snap: calm, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0 }).map((a) => a.key)).not.toContain('hold_burst');
    void HOUR_MS;
  });
});
