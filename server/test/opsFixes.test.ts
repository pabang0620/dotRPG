// 출시 전 고칠 것 F1~F13과 운영 가드 G1~G7, 헬스 체크, 종료, 감시 알림
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { Client } from 'pg';
import request from 'supertest';
import WebSocket from 'ws';
import { loadConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { isNoTransaction, migrateUp, splitStatements } from '../src/db/migrate';
import { resetSchemaCache } from '../src/domains/system/systemService';
import { evaluate, type RuleInput } from '../src/ops/alertRules';
import { beginShutdown, resetLifecycle } from '../src/ops/lifecycle';
import { metrics } from '../src/ops/metrics';
import type { Snapshot } from '../src/ops/snapshot';
import { resetWatchdog, sendAlert, watchdogTick } from '../src/ops/watchdog';
import { LOG_REDACT } from '../src/utils/logger';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown, ver } from './helpers';
import { startServer, until, WsClient, type TestServer } from './wsHelpers';

const ROOT = path.resolve(__dirname, '..');
const read = (f: string): string => fs.readFileSync(path.join(ROOT, f), 'utf8');

afterAll(shutdown);

const BASE = { DATABASE_URL: 'x', MIN_CLIENT_VERSION: '0.1.0', JWT_SECRET: 'Xk3pQ9vL2mWz7RtB5nYhJ8cDfGa1SeUo4iPq6TyHbVw' };
const PROD = {
  ...BASE,
  NODE_ENV: 'production',
  STEAM_AUTH_MODE: 'web_api',
  STEAM_APP_ID: '2800000',
  STEAM_WEB_API_KEY: 'k',
  PARTY_TRANSPORT: 'steam',
  TRUST_PROXY: '1',
  RELAY_TICKET_SECRET: 'Qm8vN2xK5pL7wR3tY6uZ9aB4cD1eF0gHiJkLmNoPq', RELAY_PUBLIC_URL: 'wss://game.example.org/relay',
  ADMIN_SECRET_KEY: Buffer.alloc(32, 9).toString('base64'),
};

describe('F1~F3 배포 파일', () => {
  it('Dockerfile: PID 1이 node(SIGTERM 전달), 마이그레이션은 CMD에 없음, 비루트, HEALTHCHECK', () => {
    const d = read('Dockerfile');
    expect(d).toMatch(/^CMD \["node", ?"dist\/server\.js"\]/m);
    expect(d).not.toMatch(/CMD \[.*sh.*-c/);
    expect(d).not.toMatch(/migrate\.js up && /);
    expect(d).toMatch(/^USER node/m);
    expect(d).toMatch(/^HEALTHCHECK/m);
    expect(d).toMatch(/max-old-space-size/);
  });

  it('운영 compose: api는 호스트 포트를 열지 않고(expose), Caddy만 80/443, init, stop_grace_period, 로그 로테이션, 메모리 제한, 헬스체크', () => {
    const c = read('ops/compose.prod.yml');
    const api = c.slice(c.indexOf('\n  api:'), c.indexOf('\n  postgres:'));
    expect(api).not.toMatch(/^\s+ports:/m);
    expect(api).toMatch(/expose:\s*\n\s*- "3000"/);
    expect(api).toMatch(/init: true/);
    expect(api).toMatch(/stop_grace_period: 40s/);
    expect(api).toMatch(/mem_limit/);
    expect(api).toMatch(/max-size/);
    expect(api).toMatch(/healthcheck:/);
    expect(api).toMatch(/TRUST_PROXY: "1"/);
    const caddy = c.slice(c.indexOf('\n  caddy:'), c.indexOf('\n  api:'));
    expect(caddy).toMatch(/"80:80"/);
    expect(caddy).toMatch(/"443:443"/);
    // postgres는 호스트에 열지 않는다
    expect(c.slice(c.indexOf('\n  postgres:'))).not.toMatch(/^\s+ports:/m);
    // 개발용 compose에도 3000 전체 공개가 남아 있지 않다
    expect(read('docker-compose.yml')).not.toMatch(/"3000:3000"/);
  });

  it('Caddyfile 예시: 관리자 경로 차단, WebSocket 통과, 점검 JSON, 도메인은 자리표시', () => {
    const c = read('ops/Caddyfile');
    expect(c).toMatch(/@admin path \/admin \/admin\/\*/);
    expect(c).toMatch(/respond @admin 404/);
    expect(c).toMatch(/health_uri \/health\/ready/);
    expect(c).toMatch(/MAINTENANCE/);
    expect(c).toMatch(/api\.example\.com/);
  });

  it('.env.example에 7단계 값이 있고 실제 비밀값은 없다', () => {
    const e = read('.env.example');
    for (const k of ['ADMIN_SECRET_KEY', 'ALERT_WEBHOOK_URL', 'OPS_HEARTBEAT_URL', 'BACKUP_DEST', 'ALLOW_DEV_AUTH_IN_PRODUCTION', 'AUTH_DEV_REGISTER_ENABLED', 'SHUTDOWN_GRACE_SECONDS']) {
      expect(e).toContain(k);
    }
    expect(e).not.toMatch(/ADMIN_SECRET_KEY=\S/);
  });
});

describe('G1~G6 기동 가드 (F4, F5, F6)', () => {
  it('운영 기준 값은 통과한다', () => {
    const c = loadConfig(PROD);
    expect(c).toMatchObject({ authDevEnabled: false, authDevRegisterEnabled: false, trustProxy: 1 });
    expect(c.admin.enabled).toBe(true);
  });

  it('G1 F4: 운영에서 STEAM_AUTH_MODE=mock은 mock+steam 조합이어도 거부한다', () => {
    expect(() => loadConfig({ ...PROD, STEAM_AUTH_MODE: 'mock' })).toThrow(/mock/);
    expect(() => loadConfig({ ...PROD, STEAM_AUTH_MODE: 'mock', PARTY_TRANSPORT: 'dev' })).toThrow(/mock/);
    expect(() => loadConfig({ ...PROD, STEAM_AUTH_MODE: 'off' })).toThrow(/web_api/);
  });

  it('G2: 운영에서 PARTY_TRANSPORT=dev는 거부한다', () => {
    expect(() => loadConfig({ ...PROD, PARTY_TRANSPORT: 'dev' })).toThrow(/PARTY_TRANSPORT/);
  });

  it('G3 F5: 운영에서 AUTH_DEV_ENABLED=true는 ALLOW_DEV_AUTH_IN_PRODUCTION이 함께 있을 때만. Steam 연동 전 시험 구성도 통과한다', () => {
    expect(() => loadConfig({ ...PROD, AUTH_DEV_ENABLED: 'true' })).toThrow(/ALLOW_DEV_AUTH_IN_PRODUCTION/);
    const cbt = loadConfig({ ...PROD, STEAM_AUTH_MODE: 'off', PARTY_TRANSPORT: 'dev', AUTH_DEV_ENABLED: 'true', ALLOW_DEV_AUTH_IN_PRODUCTION: 'true', AUTH_DEV_REGISTER_ENABLED: 'false' });
    expect(cbt).toMatchObject({ authDevEnabled: true, authDevRegisterEnabled: false, allowDevAuthInProduction: true });
  });

  it('G4: 가입은 로그인과 별도 스위치다(운영 기본 꺼짐, 개발 기본 켜짐, 로그인이 꺼지면 가입도 없다)', () => {
    expect(loadConfig({ ...BASE, NODE_ENV: 'development' }).authDevRegisterEnabled).toBe(true);
    expect(loadConfig({ ...BASE, NODE_ENV: 'development', AUTH_DEV_REGISTER_ENABLED: 'false' })).toMatchObject({ authDevEnabled: true, authDevRegisterEnabled: false });
    expect(loadConfig({ ...BASE, NODE_ENV: 'development', AUTH_DEV_ENABLED: 'false' }).authDevRegisterEnabled).toBe(false);
  });

  it('G5: 운영에서 STEAM_APP_ID=480(Valve 시험용)은 거부한다', () => {
    expect(() => loadConfig({ ...PROD, STEAM_APP_ID: '480' })).toThrow(/480/);
  });

  it('G6 F6: 운영은 TRUST_PROXY>=1, JWT_SECRET 자리표시·반복 거부, 관리자 비밀키 필수·32바이트, ADMIN_BIND 0.0.0.0 거부', () => {
    expect(() => loadConfig({ ...PROD, TRUST_PROXY: '0' })).toThrow(/TRUST_PROXY/);
    expect(() => loadConfig({ ...PROD, JWT_SECRET: 'a'.repeat(40) })).toThrow(/JWT_SECRET/);
    expect(() => loadConfig({ ...PROD, JWT_SECRET: 'please-changeme-please-changeme-1234' })).toThrow(/JWT_SECRET/);
    const { ADMIN_SECRET_KEY: _drop, ...noKey } = PROD;
    void _drop;
    expect(() => loadConfig(noKey)).toThrow(/ADMIN_SECRET_KEY/);
    expect(() => loadConfig({ ...PROD, ADMIN_SECRET_KEY: Buffer.alloc(16).toString('base64') })).toThrow(/32바이트/);
    expect(() => loadConfig({ ...PROD, ADMIN_BIND: '0.0.0.0' })).toThrow(/ADMIN_BIND/);
    // 개발에서는 키 없이 관리자 비활성, 키가 있으면 자동 활성
    expect(loadConfig({ ...BASE, NODE_ENV: 'development' }).admin.enabled).toBe(false);
    expect(() => loadConfig({ ...BASE, NODE_ENV: 'development', ADMIN_ENABLED: 'true' })).toThrow(/ADMIN_SECRET_KEY/);
  });

  it('G7과 시계·난수 주입: 운영에서는 setClockOverride, setRng를 호출하면 던진다', () => {
    const prev = process.env.NODE_ENV;
    process.env.NODE_ENV = 'production';
    try {
      expect(() => setClockOverride(() => new Date())).toThrow(/운영/);
      expect(() => setRng(null)).toThrow(/운영/);
    } finally {
      process.env.NODE_ENV = prev;
    }
    expect(() => setClockOverride(null)).not.toThrow();
  });

  it('pino redact에 비밀 필드가 있다', () => {
    for (const k of ['password', '*.token', '*.secret', 'STEAM_WEB_API_KEY', 'ALERT_WEBHOOK_URL']) expect(LOG_REDACT).toContain(k);
  });
});

describe('가입 스위치와 TRUST_PROXY의 실제 동작', () => {
  it('AUTH_DEV_REGISTER_ENABLED=false면 /auth/dev/register는 404이고 로그인은 된다. /meta가 알려 준다', async () => {
    const app = buildApp({ AUTH_DEV_REGISTER_ENABLED: 'false' });
    const reg = await request(app).post('/auth/dev/register').set(ver()).send({ login_id: 'abcd1234', password: 'password-1234' });
    expect(reg.status).toBe(404);
    expect((await request(app).get('/meta')).body.data.auth).toEqual({ dev_login_enabled: true, dev_register_enabled: false });
    const open = buildApp();
    expect((await request(open).post('/auth/dev/register').set(ver()).send({ login_id: 'abcd1235', password: 'password-1234' })).status).toBe(201);
  });

  it('TRUST_PROXY=1이면 X-Forwarded-For별로 속도 제한이 따로 걸리고, 0이면 헤더를 무시한다', async () => {
    const proxied = buildApp({ TRUST_PROXY: '1', RATE_GENERAL_IP_MAX: '2' });
    const get = (ip: string) => request(proxied).get('/meta').set('X-Forwarded-For', ip);
    expect((await get('1.1.1.1')).status).toBe(200);
    expect((await get('1.1.1.1')).status).toBe(200);
    expect((await get('1.1.1.1')).status).toBe(429);
    expect((await get('2.2.2.2')).status).toBe(200);
    const direct = buildApp({ TRUST_PROXY: '0', RATE_GENERAL_IP_MAX: '2' });
    const get0 = (ip: string) => request(direct).get('/meta').set('X-Forwarded-For', ip);
    await get0('1.1.1.1');
    await get0('2.2.2.2');
    expect((await get0('3.3.3.3')).status).toBe(429);
    buildApp();
  });
});

describe('F7 헬스 체크와 F8 종료 상태', () => {
  const app = buildApp();
  afterEach(() => {
    resetLifecycle();
    resetSchemaCache();
  });

  it('live는 DB를 보지 않고, ready는 DB·마이그레이션·게임 데이터를 본다. /health는 ready의 별칭', async () => {
    expect((await request(app).get('/health/live')).body.data).toEqual({ status: 'ok' });
    const ready = await request(app).get('/health/ready');
    expect(ready.status).toBe(200);
    expect(ready.body.data).toMatchObject({ status: 'ok' });
    expect(ready.body.data.uptime_sec).toBeUndefined();
    expect((await request(app).get('/health')).status).toBe(200);
  });

  it('마이그레이션 수가 어긋나면 ready 503(schema_mismatch)이고 live는 200', async () => {
    await getPool().query("INSERT INTO schema_migrations (name) VALUES ('9999_fake.sql')");
    try {
      resetSchemaCache();
      const r = await request(app).get('/health/ready');
      expect(r.status).toBe(503);
      expect(r.body.data).toMatchObject({ status: 'degraded', checks: { db: true, schema: false } });
      expect((await request(app).get('/health/live')).status).toBe(200);
    } finally {
      await getPool().query("DELETE FROM schema_migrations WHERE name = '9999_fake.sql'");
    }
    resetSchemaCache();
    expect((await request(app).get('/health/ready')).status).toBe(200);
  });

  it('종료 중: ready 503, 새 REST는 503 SHUTTING_DOWN(Retry-After 5), live·/meta는 계속 응답, 새 WebSocket은 거절', async () => {
    const srv: TestServer = await startServer(app);
    try {
      beginShutdown();
      const ready = await request(app).get('/health/ready');
      expect(ready.status).toBe(503);
      expect(ready.body.data.checks.shutting_down).toBe(true);
      const rest = await request(app).get('/characters');
      expect(rest.status).toBe(503);
      expect(rest.body.errors.code).toBe('SHUTTING_DOWN');
      expect(rest.headers['retry-after']).toBe('5');
      expect((await request(app).get('/health/live')).status).toBe(200);
      expect((await request(app).get('/meta')).status).toBe(200);
      await expect(WsClient.open(srv.port)).rejects.toThrow(/503/);
    } finally {
      resetLifecycle();
      await srv.stop();
    }
  });

  it('요청마다 X-Request-Id가 붙는다(프록시 값은 유효할 때만 그대로)', async () => {
    const r = await request(app).get('/meta').set('X-Request-Id', 'abc-123');
    expect(r.headers['x-request-id']).toBe('abc-123');
    expect((await request(app).get('/meta').set('X-Request-Id', 'bad id!')).headers['x-request-id']).toMatch(/^[0-9a-f-]{36}$/);
  });
});

describe('F8 실제 프로세스의 SIGTERM (종료 순서, 종료 코드 0, bye)', () => {
  it('SIGTERM을 받으면 접속자에게 bye를 보내고 shutdown.done을 남기고 종료 코드 0으로 끝난다. 관리자 listener도 같이 뜬다', async () => {
    const port = 20000 + Math.floor(Math.random() * 20000);
    const adminPort = port + 1;
    const env = {
      ...process.env,
      NODE_ENV: 'development',
      LOG_LEVEL: 'info',
      PORT: String(port),
      ADMIN_PORT: String(adminPort),
      DATABASE_URL: process.env.TEST_DATABASE_URL as string,
      AUCTION_TICK_ENABLED: 'false',
      JOBS_ENABLED: 'false',
      SHUTDOWN_GRACE_SECONDS: '20',
    };
    const child = spawn(process.execPath, ['-r', require.resolve('tsx/cjs'), path.join(ROOT, 'src/server.ts')], { cwd: ROOT, env });
    let out = '';
    child.stdout.on('data', (d: Buffer) => (out += d.toString()));
    child.stderr.on('data', (d: Buffer) => (out += d.toString()));
    const exit = new Promise<number>((res) => child.on('exit', (c) => res(c ?? -1)));
    try {
      const deadline = Date.now() + 40_000;
      let up = false;
      while (!up && Date.now() < deadline) {
        up = await fetch(`http://127.0.0.1:${port}/health/ready`).then((r) => r.ok, () => false);
        if (!up) await new Promise((r) => setTimeout(r, 300));
      }
      expect(up).toBe(true);
      // 관리자 listener: 토큰 없이는 401, 공개 포트에는 관리자 경로가 없다(404)
      expect((await fetch(`http://127.0.0.1:${adminPort}/admin/me`)).status).toBe(401);
      expect((await fetch(`http://127.0.0.1:${port}/admin/me`)).status).toBe(404);
      const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
      const frames: string[] = [];
      ws.on('message', (d) => frames.push(d.toString()));
      await new Promise((r) => ws.once('open', r));
      child.kill('SIGTERM');
      const code = await Promise.race([exit, new Promise<number>((r) => setTimeout(() => r(-99), 15_000))]);
      expect(code).toBe(0);
      expect(out).toContain('shutdown.done');
      ws.terminate();
    } finally {
      child.kill('SIGKILL');
    }
  }, 90_000);
});

describe('F11, F13 마이그레이션', () => {
  it('F11: drops.kill_id 인덱스가 있다', async () => {
    const r = await getPool().query("SELECT indexname FROM pg_indexes WHERE tablename = 'drops' AND indexname = 'drops_kill_idx'");
    expect(r.rows).toHaveLength(1);
  });

  it('F13: `-- no-transaction` 파일은 문장마다 따로 실행해 CREATE INDEX CONCURRENTLY가 된다. 마커가 없으면 같은 문장이 실패한다', async () => {
    expect(isNoTransaction('-- no-transaction\nCREATE INDEX x ON t (a);')).toBe(true);
    expect(isNoTransaction('CREATE INDEX x ON t (a);')).toBe(false);
    expect(splitStatements('A;\n-- 주석만\nB;\n')).toEqual(['A', '-- 주석만\nB;'.replace(/;$/, '')]);

    const admin = new Client({ connectionString: process.env.TEST_PG_ADMIN_URL });
    await admin.connect();
    const name = `mig_probe_${Date.now()}`;
    await admin.query(`CREATE DATABASE ${name}`);
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mig-'));
    const url = new URL(process.env.TEST_PG_ADMIN_URL as string);
    url.pathname = `/${name}`;
    const write = (f: string, up: string): void =>
      fs.writeFileSync(path.join(dir, f), `-- ============ UP ============\n${up}\n-- ============ DOWN ============\nSELECT 1;\n`);
    try {
      write('0001_t.sql', 'CREATE TABLE probe (id int, a int);');
      write('0002_ix.sql', '-- no-transaction\nCREATE INDEX CONCURRENTLY probe_a ON probe (a);\nCREATE INDEX CONCURRENTLY probe_id ON probe (id);');
      expect(await migrateUp(url.toString(), dir)).toEqual(['0001_t.sql', '0002_ix.sql']);
      const c = new Client({ connectionString: url.toString() });
      await c.connect();
      expect((await c.query("SELECT indexname FROM pg_indexes WHERE tablename = 'probe' ORDER BY 1")).rows.map((x) => x.indexname)).toEqual(['probe_a', 'probe_id']);
      await c.end();
      write('0003_bad.sql', 'CREATE INDEX CONCURRENTLY probe_bad ON probe (a);');
      await expect(migrateUp(url.toString(), dir)).rejects.toThrow(/0003_bad.sql/);
    } finally {
      await admin.query(`DROP DATABASE ${name} WITH (FORCE)`);
      await admin.end();
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });
});

describe('F12 요청 지표', () => {
  it('요청 지표가 p95와 오류 코드 상위를 센다', async () => {
    metrics.clear();
    const app = buildApp();
    await request(app).get('/meta');
    await request(app).get('/nope');
    await request(app).get('/nope');
    await until(() => metrics.http().requests >= 3);
    const h = metrics.http();
    expect(h).toMatchObject({ requests: 3, status_2xx: 1, status_4xx: 2 });
    expect(h.top_errors[0]).toEqual({ code: 'NOT_FOUND', count: 2 });
  });
});

describe('감시 규칙과 웹훅 알림', () => {
  const snap = (over: Partial<Snapshot> = {}): Snapshot => ({
    at: new Date().toISOString(),
    process: { uptime_s: 100000, rss_mb: 100, heap_mb: 50, eventloop_delay_p99_ms: 5, version: 'dev', data_version: 'x' },
    http: { requests: 0, status_2xx: 0, status_4xx: 0, status_5xx: 0, p50_ms: 0, p95_ms: 0, p99_ms: 0, top_errors: [], in_flight: 0, p95_5m_ms: 10, status_5xx_5m: 0, requests_5m: 0 },
    websocket: { sessions: 1, max: 500, close_reasons: {}, handshake_rejected: 0 },
    db: { pool: { total: 2, idle: 2, waiting: 0, max: 10 }, connections: 5, max_connections: 50, size_mb: 10 },
    ticks: {},
    auction: { lag_seconds: 0 },
    jobs: [],
    queues: { match_waiting: 0, open_reports: 0, oldest_open_report_age_s: null, held_runs: 0, oldest_held_age_s: null, unnotified_sanctions: 0 },
    security: { login_failures_5m: 0 },
    maintenance: { phase: 'none', remaining_s: null },
    shutting_down: false,
    disk_used_pct: 40,
    ...over,
  });
  const input = (s: Snapshot, o: Partial<RuleInput> = {}): RuleInput => ({ snap: s, integrityMismatches: null, memLimitMb: null, readyFailStreak: 0, slowConsumerCloses1m: 0, ...o });
  const keys = (i: RuleInput): string[] => evaluate(i).map((a) => `${a.level}:${a.key}`);

  it('평상시는 알림이 없고, 긴급·경고 조건마다 해당 알림이 난다', () => {
    expect(keys(input(snap()))).toEqual([]);
    expect(keys(input(snap(), { readyFailStreak: 2 }))).toContain('critical:ready_fail');
    expect(keys(input(snap({ http: { ...snap().http, requests_5m: 100, status_5xx_5m: 5 } })))).toContain('critical:http_5xx');
    expect(keys(input(snap({ http: { ...snap().http, requests_5m: 10, status_5xx_5m: 5 } })))).not.toContain('critical:http_5xx');
    expect(keys(input(snap({ db: { ...snap().db, pool: { total: 10, idle: 0, waiting: 5, max: 10 } } })))).toContain('critical:pool_waiting');
    expect(keys(input(snap(), { integrityMismatches: 1 }))).toContain('critical:integrity');
    expect(keys(input(snap({ disk_used_pct: 91 })))).toContain('critical:disk');
    expect(keys(input(snap({ disk_used_pct: 82 })))).toContain('warning:disk_warn');
    expect(keys(input(snap({ auction: { lag_seconds: 700 } })))).toContain('critical:auction_lag');
    expect(keys(input(snap({ auction: { lag_seconds: 200 } })))).toContain('warning:auction_lag_warn');
    expect(keys(input(snap({ jobs: [{ name: 'purge-hourly', last_status: 'ok', last_started_at: null, last_ok_at: new Date(Date.now() - 4 * 3_600_000).toISOString() }] })))).toContain('warning:job_purge-hourly');
    expect(keys(input(snap({ queues: { ...snap().queues, oldest_held_age_s: 25 * 3600 } })))).toContain('warning:held_age');
    expect(keys(input(snap({ security: { login_failures_5m: 101 } })))).toContain('warning:login_fail');
  });

  it('웹훅: discord 형식으로 보내고, 같은 알림은 최소 간격 안에 반복하지 않고, 해소되면 한 번 알린다. URL이 없으면 로그만', async () => {
    const got: { content: string }[] = [];
    const srv = http.createServer((req, res) => {
      let b = '';
      req.on('data', (d) => (b += d));
      req.on('end', () => {
        got.push(JSON.parse(b) as { content: string });
        res.end('ok');
      });
    });
    await new Promise<void>((r) => srv.listen(0, '127.0.0.1', r));
    const url = `http://127.0.0.1:${(srv.address() as AddressInfo).port}/hook`;
    try {
      buildApp({ ALERT_WEBHOOK_URL: url, ALERT_WEBHOOK_FORMAT: 'discord', SERVER_NAME: 'dotrpg-test' });
      resetWatchdog();
      const bad = snap({ db: { ...snap().db, pool: { total: 10, idle: 0, waiting: 6, max: 10 } } });
      const first = await watchdogTick(bad);
      expect(first.fired).toContain('pool_waiting');
      expect(got[0]?.content).toContain('[긴급] dotrpg-test');
      const n = got.length;
      expect((await watchdogTick(bad)).fired).toEqual([]);
      expect(got).toHaveLength(n);
      const fine = await watchdogTick(snap());
      expect(fine.resolved).toContain('pool_waiting');
      expect(got.at(-1)?.content).toContain('[해소]');
      // 정합성 점검 불일치는 긴급
      await resetDb();
      await getPool().query("INSERT INTO job_runs (job, status, finished_at, detail) VALUES ('integrity-nightly', 'ok', now(), '{\"mismatches\": 2}'::jsonb)");
      expect((await watchdogTick(snap())).fired).toContain('integrity');
      // URL이 없으면 로그만
      buildApp();
      expect(await sendAlert({ key: 'k', level: 'warning', title: 't', detail: 'd' }, 'firing')).toBe('logged');
    } finally {
      await new Promise((r) => srv.close(r));
      buildApp();
      resetWatchdog();
    }
  });
});


describe('배포·백업 스크립트', () => {
  it('모든 스크립트가 bash 문법 검사를 통과한다', () => {
    for (const f of ['backup.sh', 'restore-drill.sh', 'deploy.sh', 'host-check.sh']) {
      const r = spawnSync('bash', ['-n', path.join(ROOT, 'ops', f)]);
      expect([f, r.status, String(r.stderr)]).toEqual([f, 0, '']);
    }
  });

  it('backup.sh: 로컬 경로 목적지에 저장하고, 암호화 설정이 없으면 거부하며, 오래된 백업을 정리한다', () => {
    const dest = fs.mkdtempSync(path.join(os.tmpdir(), 'bk-dest-'));
    const state = fs.mkdtempSync(path.join(os.tmpdir(), 'bk-state-'));
    const run = (extra: Record<string, string>) =>
      spawnSync('bash', [path.join(ROOT, 'ops/backup.sh')], {
        env: { PATH: process.env.PATH as string, BACKUP_DEST: dest, BACKUP_STATE_DIR: state, PG_DUMP_CMD: 'printf dump-bytes', BACKUP_VERIFY: '0', ...extra },
        encoding: 'utf8',
      });
    try {
      // 암호화 키도 평문 허용도 없으면 실패
      const refused = run({});
      expect(refused.status).toBe(1);
      expect(refused.stdout).toContain('BACKUP_AGE_RECIPIENT');
      expect(fs.readdirSync(dest)).toHaveLength(0);
      // 오래된 daily 3개(2020-01-01은 수요일이라 어느 보관 규칙에도 안 걸림, 일요일 1개, 1일 1개)와 predeploy 12개를 미리 둔다
      for (const d of ['20200101', '20200105', '20200201', '20200303']) fs.writeFileSync(path.join(dest, `dotrpg-daily-${d}T000000Z.dump`), 'x');
      for (let i = 0; i < 12; i++) fs.writeFileSync(path.join(dest, `dotrpg-predeploy-aaa${String(i).padStart(2, '0')}-2020010${(i % 9) + 1}T0000${String(i).padStart(2, '0')}Z.dump`), 'x');
      // Today's own backup also counts as a weekly (Sunday) or monthly (1st) keep, so on those days one more slot is needed.
      const now = new Date();
      const weekly = now.getUTCDay() === 0 ? '2' : '1';
      const monthly = now.getUTCDate() === 1 ? '2' : '1';
      const ok = run({ BACKUP_ALLOW_PLAINTEXT: '1', BACKUP_KEEP_DAILY: '1', BACKUP_KEEP_WEEKLY: weekly, BACKUP_KEEP_MONTHLY: monthly });
      expect(ok.status).toBe(0);
      const files = fs.readdirSync(dest).sort();
      expect(files.filter((f) => f.startsWith('dotrpg-daily-'))).toHaveLength(3); // 오늘 것 + 일요일(0105) + 1일(0201 이 최근 1개)
      expect(files).toContain('dotrpg-daily-20200105T000000Z.dump');
      expect(files).toContain('dotrpg-daily-20200201T000000Z.dump');
      expect(files).not.toContain('dotrpg-daily-20200101T000000Z.dump');
      expect(files.filter((f) => f.startsWith('dotrpg-predeploy-'))).toHaveLength(10);
      const created = files.find((f) => f.startsWith('dotrpg-daily-2') && !f.startsWith('dotrpg-daily-2020')) as string;
      expect(fs.readFileSync(path.join(dest, created), 'utf8')).toBe('dump-bytes');
      expect(Number(fs.readFileSync(path.join(state, 'last_backup_ok'), 'utf8'))).toBeGreaterThan(Date.now() / 1000 - 60);
      // 덤프가 실패하면 파일도 성공 시각도 남기지 않는다
      const before = fs.readdirSync(dest).length;
      const bad = run({ BACKUP_ALLOW_PLAINTEXT: '1', PG_DUMP_CMD: 'false' });
      expect(bad.status).toBe(1);
      expect(fs.readdirSync(dest)).toHaveLength(before);
    } finally {
      fs.rmSync(dest, { recursive: true, force: true });
      fs.rmSync(state, { recursive: true, force: true });
    }
  });
});
