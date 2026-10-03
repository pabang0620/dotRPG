import { randomUUID } from 'node:crypto';
import { Client } from 'pg';
import { getPool } from '../src/db/pool';
import { closeInterruptedRuns, nextRunAt, registerJob, runJob, resetJobStop } from '../src/ops/jobRunner';
import { registerAllJobs } from '../src/ops/jobs';
import { integrityJob } from '../src/ops/jobs/integrity';
import { sweepStale } from '../src/ops/jobs/staleRuns';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { newHero, type Hero } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { adminApp, adminGet, adminPost, makeAdmin, rid } from './opsHelpers';

const pub = buildApp({ PURGE_BATCH: '2' });
registerAllJobs();
registerJob({
  name: 'always-fails',
  schedule: { kind: 'every', minutes: 5 },
  run: async () => {
    throw new Error('boom');
  },
});

beforeEach(async () => {
  await resetDb();
  resetJobStop();
  getRateLimitStore().clear();
});
afterAll(shutdown);

const q = async <T = Record<string, unknown>>(sql: string, p: unknown[] = []): Promise<T[]> => (await getPool().query(sql, p)).rows as T[];
const count = async (table: string, where = 'true'): Promise<number> => Number((await q<{ n: string }>(`SELECT count(*) AS n FROM ${table} WHERE ${where}`))[0]?.n);
const ago = (days: number): Date => new Date(Date.now() - days * 86_400_000);

async function addKill(h: Hero, at: Date, dropExpires: Date | null): Promise<number> {
  const k = await q<{ id: string }>(
    `INSERT INTO kill_log (character_id, map_id, monster_id, monster_level, context, hits, xp_granted, request_id, created_at)
     VALUES ($1, 'village', 'slime', 1, 'field', 1, 1, $2, $3) RETURNING id`,
    [h.dbId, randomUUID(), at],
  );
  const id = Number((k[0] as { id: string }).id);
  if (dropExpires) {
    await q('INSERT INTO drops (character_id, kill_id, item_key, count, expires_at) VALUES ($1, $2, $3, 1, $4)', [h.dbId, id, 'mat_bone', dropExpires]);
  }
  return id;
}

describe('purge-hourly (보관 기간 정리, 배치, FK 순서)', () => {
  it('보관 기간이 지난 행만 지운다: 경계 안쪽은 남고, 결과가 job_runs에 기록된다', async () => {
    const h = await newHero(pub);
    // request_log: 7일 보관. 5건은 8일 전, 1건은 6일 전(PURGE_BATCH=2라 여러 배치로 지운다)
    for (let i = 0; i < 5; i++) {
      await q(
        `INSERT INTO request_log (account_id, request_id, endpoint, request_hash, status_code, response, created_at)
         SELECT account_id, $1, 'x', 'h', 200, '{}'::jsonb, $2 FROM characters WHERE id = $3`,
        [randomUUID(), ago(8), h.dbId],
      );
    }
    await q(
      `INSERT INTO request_log (account_id, request_id, endpoint, request_hash, status_code, response, created_at)
       SELECT account_id, $1, 'x', 'h', 200, '{}'::jsonb, $2 FROM characters WHERE id = $3`,
      [randomUUID(), ago(6), h.dbId],
    );
    // chat: 7일 보관
    for (const d of [8, 1]) {
      await q(
        `INSERT INTO chat_messages (channel, shard, sender_account_id, sender_character_id, sender_name, text, client_msg_id, created_at)
         SELECT 'general', 1, account_id, id, name, '안녕', $1, $2 FROM characters WHERE id = $3`,
        [randomUUID(), ago(d), h.dbId],
      );
    }
    // kill_log 7일, drops는 만료 후 1일
    const oldKill = await addKill(h, ago(8), ago(8));
    const youngKillOldDrop = await addKill(h, ago(2), ago(3));
    const youngKillFreshDrop = await addKill(h, ago(1), new Date(Date.now() + 60_000));
    const before = await count('request_log');
    const r = await runJob('purge-hourly', 'manual');
    expect(r.status).toBe('ok');
    expect(await count('request_log')).toBe(before - 5);
    expect(await count('chat_messages')).toBe(1);
    const kills = (await q<{ id: string }>('SELECT id FROM kill_log ORDER BY id')).map((x) => Number(x.id));
    expect(kills).toEqual([youngKillOldDrop, youngKillFreshDrop]);
    const drops = (await q<{ kill_id: string }>('SELECT kill_id FROM drops')).map((x) => Number(x.kill_id));
    expect(drops).toEqual([youngKillFreshDrop]);
    void oldKill;
    // 기록: 표별 행 수와 배치 수(5건을 2개씩 = 3배치)
    const run = (await q<{ status: string; rows_affected: number; detail: { tables: Record<string, { rows: number; batches: number }> } }>(
      "SELECT status, rows_affected, detail FROM job_runs WHERE job = 'purge-hourly'",
    ))[0];
    expect(run?.status).toBe('ok');
    expect(run?.detail.tables.request_log).toMatchObject({ rows: 5, batches: 3 });
    expect(run?.rows_affected).toBeGreaterThanOrEqual(8);
  });
});

describe('purge-daily', () => {
  it('이상 기록은 심각도별로(1: 30일, 3: 180일), 만료 토큰·수령된 일반 우편·작업 기록·관리자 세션을 지우고 system 우편은 남긴다', async () => {
    const h = await newHero(pub);
    const admin = await makeAdmin('viewer');
    const acc = (await q<{ account_id: string }>('SELECT account_id FROM characters WHERE id = $1', [h.dbId]))[0]?.account_id;
    const an = (sev: number, d: number) =>
      q("INSERT INTO anomaly_log (account_id, character_id, kind, severity, created_at) VALUES ($1, $2, 'kill_rate', $3, $4)", [acc, h.dbId, sev, ago(d)]);
    await an(1, 40);
    await an(1, 10);
    await an(3, 40);
    await an(3, 200);
    await q(
      `INSERT INTO refresh_tokens (account_id, family_id, token_hash, expires_at, created_at) VALUES ($1, $2, 'old-token', $3, $4), ($1, $2, 'fresh-token', now() + interval '10 days', now())`,
      [acc, randomUUID(), ago(40), ago(60)],
    );
    const mail = (kind: string, sys: string | null, claimedDaysAgo: number) =>
      q(
        `INSERT INTO mails (character_id, kind, system_code, gold, created_at, expires_at, claimed_at)
         VALUES ($1, $2, $3, 10, $4, $5, $4)`,
        [h.dbId, kind, sys, ago(claimedDaysAgo + 1), new Date(ago(claimedDaysAgo).getTime() + 86_400_000 * 29)],
      );
    await mail('sold', null, 200);
    await mail('system', 'event', 200);
    await mail('sold', null, 10);
    await q("INSERT INTO job_runs (job, status, started_at, finished_at) VALUES ('purge-hourly', 'ok', $1, $1)", [ago(100)]);
    await q("INSERT INTO admin_sessions (admin_id, token_hash, expires_at, created_at) VALUES ($1, 'x1', $2, $3)", [admin.id, ago(40), ago(41)]);

    const r = await runJob('purge-daily', 'manual');
    expect(r.status).toBe('ok');
    expect((await q<{ severity: number; d: number }>('SELECT severity, round(extract(epoch FROM now() - created_at) / 86400)::int AS d FROM anomaly_log ORDER BY d')).map((x) => `${x.severity}:${x.d}`)).toEqual(['1:10', '3:40']);
    expect(await count('refresh_tokens', "token_hash IN ('old-token', 'fresh-token')")).toBe(1);
    expect(await count('refresh_tokens', "token_hash = 'old-token'")).toBe(0);
    expect((await q<{ kind: string }>('SELECT kind FROM mails ORDER BY id')).map((x) => x.kind)).toEqual(['system', 'sold']);
    expect(await count('job_runs', "started_at < now() - interval '90 days'")).toBe(0);
    expect(await count('admin_sessions', "token_hash = 'x1'")).toBe(0);
    // 원장·dungeon_runs는 지우지 않는다(6.3 "지우지 않는 것")
    expect(await count('gold_ledger')).toBeGreaterThan(0);
  });
});

describe('stale-runs (방치 상태 정리)', () => {
  it('오래 방치된 진행 중 판은 abandoned, 최근 판은 그대로. 방치된 파티는 해산한다', async () => {
    const [a, b] = [await newHero(pub), await newHero(pub)];
    const run = (h: Hero, startedAgoMin: number) =>
      q<{ uuid: string }>(
        `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, state, reset_day, started_at, humans, ai_count, counts_entry)
         VALUES ($1, 'gold_vein', 0, 1, 'playing', date_trunc('day', now()), now() - ($2::int * interval '1 minute'), 1, 0, true) RETURNING uuid`,
        [h.dbId, startedAgoMin],
      );
    const stale = (await run(a, 120))[0]?.uuid;
    const fresh = (await run(b, 5))[0]?.uuid;
    const party = await q<{ id: string }>(
      `INSERT INTO parties (leader_character_id, dungeon_id, difficulty, max_members, source, listed, last_active_at)
       VALUES ($1, 'gold_vein', 0, 2, 'board', false, now() - interval '3 hours') RETURNING id`,
      [b.dbId],
    );
    const res = await sweepStale();
    expect(res).toMatchObject({ abandonedRuns: 1, closedParties: 1 });
    expect((await q<{ state: string }>('SELECT state FROM dungeon_runs WHERE uuid = $1', [stale]))[0]?.state).toBe('abandoned');
    expect((await q<{ state: string }>('SELECT state FROM dungeon_runs WHERE uuid = $1', [fresh]))[0]?.state).toBe('playing');
    expect((await q<{ state: string; close_reason: string }>('SELECT state, close_reason FROM parties WHERE id = $1', [party[0]?.id]))[0]).toEqual({ state: 'closed', close_reason: 'idle_timeout' });
    // 두 번째 실행은 할 일이 없다(멱등)
    expect(await sweepStale()).toMatchObject({ abandonedRuns: 0, closedParties: 0 });
  });
});

describe('integrity-nightly (정합성 점검)', () => {
  const run = (full = true) => integrityJob({ opts: { full }, shouldStop: () => false });

  it('정상 활동 뒤에는 불일치 0', async () => {
    await newHero(pub);
    const r = await run();
    expect(r.detail.mismatches).toBe(0);
  });

  it('원장 없이 바꾼 골드·아이템과 정지 불일치를 잡고, 표본에 캐릭터 uuid를 싣는다(고치지는 않는다)', async () => {
    const h = await newHero(pub);
    await q('UPDATE characters SET gold = gold + 7 WHERE id = $1', [h.dbId]);
    await q("UPDATE character_items SET count = count + 1 WHERE id = (SELECT id FROM character_items WHERE character_id = $1 AND location = 'bag' LIMIT 1)", [h.dbId]);
    await q("UPDATE accounts SET banned_until = now() + interval '1 day' WHERE id = (SELECT account_id FROM characters WHERE id = $1)", [h.dbId]);
    const r = await run();
    const checks = r.detail.checks as Record<string, { count: number; samples: Record<string, unknown>[] }>;
    expect(checks.I1?.count).toBeGreaterThanOrEqual(1);
    expect(checks.I1?.samples[0]).toMatchObject({ character: h.id });
    expect(checks.I2?.count).toBeGreaterThanOrEqual(1);
    expect(checks.I5?.count).toBe(1);
    expect(r.detail.mismatches).toBeGreaterThanOrEqual(3);
    // 읽기 전용: 자동으로 고치지 않는다(7골드가 그대로)
    const before = Number((await q<{ gold: string }>('SELECT gold FROM characters WHERE id = $1', [h.dbId]))[0]?.gold);
    await run();
    expect(Number((await q<{ gold: string }>('SELECT gold FROM characters WHERE id = $1', [h.dbId]))[0]?.gold)).toBe(before);
  });

  it('막힌 상태(I4): 마감이 한참 지난 활성 경매를 센다', async () => {
    const h = await newHero(pub);
    await q(
      `INSERT INTO auction_listings (seller_character_id, seller_account_id, item_key, item_base, count, category, buyout_price, deposit, fee_pct, duration_hours, ends_at, created_at)
       SELECT id, account_id, 'mat_bone', 'mat_bone', 1, 'material', 100, 1, 3, 12, now() - interval '1 hour', now() - interval '13 hours' FROM characters WHERE id = $1`,
      [h.dbId],
    );
    const r = await run(false);
    const checks = r.detail.checks as Record<string, { count: number; samples: { kind: string }[] }>;
    expect(checks.I4?.samples.map((s) => s.kind)).toContain('auction_overdue');
  });
});

describe('JobRunner', () => {
  it('실패는 failed와 오류 문장으로 기록하고, 다른 곳이 같은 작업을 쥐고 있으면 기록 없이 건너뛴다', async () => {
    const f = await runJob('always-fails', 'manual');
    expect(f).toMatchObject({ status: 'failed', error: 'boom' });
    expect((await q<{ status: string; error: string }>("SELECT status, error FROM job_runs WHERE job = 'always-fails'"))[0]).toEqual({ status: 'failed', error: 'boom' });

    const other = new Client({ connectionString: process.env.TEST_DATABASE_URL });
    await other.connect();
    await other.query("SELECT pg_advisory_lock(hashtext('job:purge-hourly'))");
    const skipped = await runJob('purge-hourly', 'schedule');
    expect(skipped).toEqual({ status: 'skipped', reason: 'locked' });
    expect(await count('job_runs', "job = 'purge-hourly'")).toBe(0);
    await other.query("SELECT pg_advisory_unlock(hashtext('job:purge-hourly'))");
    await other.end();
    expect((await runJob('purge-hourly', 'schedule')).status).toBe('ok');
  });

  it('기동 때 running으로 남은 행은 failed(interrupted)로 닫는다', async () => {
    await q("INSERT INTO job_runs (job, status) VALUES ('purge-daily', 'running')");
    expect(await closeInterruptedRuns()).toBe(1);
    expect((await q<{ status: string; error: string }>("SELECT status, error FROM job_runs WHERE job = 'purge-daily'"))[0]).toEqual({ status: 'failed', error: 'interrupted' });
  });

  it('다음 실행 시각: 매시 :17, 매일 KST 04:10, N분마다', () => {
    const from = new Date('2026-10-05T10:20:00Z');
    expect(nextRunAt({ kind: 'hourly', minute: 17 }, from).toISOString()).toBe('2026-10-05T11:17:00.000Z');
    expect(nextRunAt({ kind: 'hourly', minute: 17 }, new Date('2026-10-05T10:10:00Z')).toISOString()).toBe('2026-10-05T10:17:00.000Z');
    // 04:10 KST = 19:10 UTC(전날)
    expect(nextRunAt({ kind: 'daily_kst', hour: 4, minute: 10 }, from).toISOString()).toBe('2026-10-05T19:10:00.000Z');
    expect(nextRunAt({ kind: 'daily_kst', hour: 4, minute: 10 }, new Date('2026-10-05T19:10:00Z')).toISOString()).toBe('2026-10-06T19:10:00.000Z');
    expect(nextRunAt({ kind: 'every', minutes: 10 }, from).toISOString()).toBe('2026-10-05T10:30:00.000Z');
  });
});

describe('작업 관리자 API (OP2, OP3)', () => {
  it('수동 실행은 owner만, 허용 목록만, 작업당 1분에 1회. 재전송은 저장된 응답', async () => {
    const app = adminApp();
    const owner = await makeAdmin('owner');
    const viewer = await makeAdmin('viewer');
    const r = rid();
    const ok = await adminPost(app, owner, '/admin/ops/jobs/stale-runs/run', {}, r);
    expect(ok.status).toBe(200);
    expect(ok.body.data).toMatchObject({ job: 'stale-runs', status: 'ok' });
    expect((await adminPost(app, owner, '/admin/ops/jobs/stale-runs/run', {}, r)).headers['idempotent-replay']).toBe('true');
    expect((await adminPost(app, owner, '/admin/ops/jobs/stale-runs/run', {})).status).toBe(429);
    expect((await adminPost(app, owner, '/admin/ops/jobs/maintenance-close/run', {})).body.errors.code).toBe('JOB_NOT_FOUND');
    expect((await adminPost(app, viewer, '/admin/ops/jobs/purge-daily/run', {})).status).toBe(403);
    const jobs = await adminGet(app, viewer, '/admin/ops/jobs');
    const sr = jobs.body.data.jobs.find((j: { name: string }) => j.name === 'stale-runs');
    expect(sr.recent[0]).toMatchObject({ status: 'ok', started_by: 'manual' });
    expect(jobs.body.data.integrity).toBeNull();
    await adminPost(app, owner, '/admin/ops/jobs/integrity-nightly/run', { full: true });
    expect((await adminGet(app, viewer, '/admin/ops/jobs')).body.data.integrity.result.mismatches).toBe(0);
    const status = await adminGet(app, viewer, '/admin/ops/status');
    expect(status.body.data).toMatchObject({ websocket: { sessions: 0 }, maintenance: { phase: 'none' }, queues: { open_reports: 0 } });
  });
});
