// 15단계(Docs/server/phase15_load_and_logging.md): 클라이언트 오류 보고, 잦은 경로 request_log 보관, 캐릭터 단위 경제 한도, 요청 로그 레벨.
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { QUIET_ROUTES } from '../src/app';
import { getPool } from '../src/db/pool';
import { runJob, resetJobStop } from '../src/ops/jobRunner';
import { registerAllJobs } from '../src/ops/jobs';
import { metrics, routeKey } from '../src/ops/metrics';
import { logger } from '../src/utils/logger';
import { newHero } from './economyHelpers';
import { auth, buildApp, registerAccount, resetDb, shutdown, ver } from './helpers';

let app = buildApp();
registerAllJobs();

beforeEach(async () => {
  await resetDb();
  resetJobStop();
  app = buildApp();
});
afterAll(shutdown);

const q = async <T = Record<string, unknown>>(sql: string, p: unknown[] = []): Promise<T[]> => (await getPool().query(sql, p)).rows as T[];
const count = async (table: string, where = 'true'): Promise<number> => Number((await q<{ n: string }>(`SELECT count(*) AS n FROM ${table} WHERE ${where}`))[0]?.n);

const report = { version: '0.3.1', platform: 'Android', message: 'NullReferenceException: Object reference not set', stack: 'at DotRPG.Foo.Bar ()', scene: 'village' };

describe('POST /client-errors', () => {
  it('인증이 없으면 401, 있으면 204로 저장하고 버전 헤더가 없어도 받는다', async () => {
    expect((await request(app).post('/client-errors').set(ver()).send(report)).status).toBe(401);
    const s = await registerAccount(app);
    const r = await request(app).post('/client-errors').set({ Authorization: `Bearer ${s.access}` }).send({ ...report, at: new Date().toISOString() });
    expect(r.status).toBe(204);
    const rows = await q<{ message: string; platform: string; scene: string }>('SELECT message, platform, scene FROM client_errors');
    expect(rows).toEqual([{ message: report.message, platform: 'Android', scene: 'village' }]);
  });

  it('계정당 분당 5건, 넘으면 429', async () => {
    const s = await registerAccount(app);
    for (let i = 0; i < 5; i++) expect((await request(app).post('/client-errors').set(auth(s)).send(report)).status).toBe(204);
    const r = await request(app).post('/client-errors').set(auth(s)).send(report);
    expect(r.status).toBe(429);
    expect(await count('client_errors')).toBe(5);
  });

  it('4KB를 넘는 본문은 413, 길이 초과·모르는 필드는 검증 오류', async () => {
    const s = await registerAccount(app);
    const big = await request(app).post('/client-errors').set(auth(s)).send({ ...report, stack: 'x'.repeat(2000), scene: 'y'.repeat(64), message: 'z'.repeat(500), pad: 'p'.repeat(2000) });
    expect(big.status).toBe(413);
    expect((await request(app).post('/client-errors').set(auth(s)).send({ ...report, message: 'm'.repeat(501) })).status).toBe(400);
    expect((await request(app).post('/client-errors').set(auth(s)).send({ ...report, extra: 1 })).status).toBe(400);
    expect(await count('client_errors')).toBe(0);
  });
});

describe('보관 정리(purge-hourly)', () => {
  it('잦은 경로 request_log는 24시간, 그 밖은 7일, client_errors는 14일', async () => {
    const h = await newHero(app);
    const acct = Number((await q<{ account_id: string }>('SELECT account_id FROM characters WHERE id = $1', [h.dbId]))[0]?.account_id);
    const at = (hours: number): Date => new Date(Date.now() - hours * 3_600_000);
    const log = (endpoint: string, hours: number) =>
      q(`INSERT INTO request_log (account_id, request_id, endpoint, request_hash, status_code, response, created_at) VALUES ($1, $2, $3, 'h', 200, '{}'::jsonb, $4)`, [acct, randomUUID(), endpoint, at(hours)]);
    await log('POST /characters/:uuid/kills', 25);
    await log('POST /characters/:uuid/kills', 23);
    await log('POST /characters/:uuid/drops/claim', 30);
    await log('POST /characters/:uuid/quests/:quest_id/claim', 30);
    await log('POST /characters/:uuid/quests/:quest_id/claim', 24 * 8);
    for (const d of [15, 13]) {
      await q(`INSERT INTO client_errors (account_id, version, platform, message, created_at) VALUES ($1, '0.3.1', 'Android', 'e', $2)`, [acct, at(d * 24)]);
    }
    const before = await count('request_log'); // 캐릭터 생성 기록 포함
    expect((await runJob('purge-hourly', 'manual')).status).toBe('ok');
    expect(await count('request_log', "endpoint = 'POST /characters/:uuid/kills'")).toBe(1);
    expect(await count('request_log', "endpoint = 'POST /characters/:uuid/drops/claim'")).toBe(0);
    expect(await count('request_log', "endpoint = 'POST /characters/:uuid/quests/:quest_id/claim'")).toBe(1);
    expect(await count('request_log')).toBe(before - 3);
    expect(await count('client_errors')).toBe(1);
  });
});

describe('경제 경로 캐릭터 단위 한도', () => {
  it('같은 캐릭터 경로는 분당 RATE_ECONOMY_CHAR_MAX를 넘으면 429, 다른 캐릭터는 따로 센다', async () => {
    app = buildApp({ RATE_ECONOMY_CHAR_MAX: '5' });
    const a = await newHero(app);
    const b = await newHero(app);
    const hit = (id: string, s: typeof a.s) => request(app).get(`/characters/${id}/no-such-route`).set(auth(s));
    for (let i = 0; i < 5; i++) expect((await hit(a.id, a.s)).status).toBe(404);
    const over = await hit(a.id, a.s);
    expect(over.status).toBe(429);
    expect(over.body.errors.code).toBe('RATE_LIMITED');
    expect((await hit(b.id, b.s)).status).toBe(404);
  });
});

describe('요청 로그', () => {
  it('경로 이름은 uuid·숫자를 :id로 바꾸고, 잦은 경로 목록과 맞는다', () => {
    expect(routeKey('POST', `/characters/${randomUUID()}/kills`)).toBe('POST /characters/:id/kills');
    expect(routeKey('GET', '/mails/12/attachments')).toBe('GET /mails/:id/attachments');
    expect(QUIET_ROUTES.has(routeKey('POST', `/characters/${randomUUID()}/drops/claim`))).toBe(true);
    expect(QUIET_ROUTES.has(routeKey('POST', `/characters/${randomUUID()}/quests/c1_morning/claim`))).toBe(false);
  });

  it('4xx는 warn과 오류 코드, 2xx는 info로 남고 경로별 집계에 들어간다', async () => {
    metrics.clear();
    const warn = jest.spyOn(logger, 'warn');
    const info = jest.spyOn(logger, 'info');
    try {
      await request(app).get('/no-such-path').set(ver());
      await new Promise((r) => setImmediate(r));
      const call = warn.mock.calls.find((c) => c[1] === 'request');
      expect(call?.[0]).toMatchObject({ status: 404, code: 'NOT_FOUND', path: '/no-such-path' });
      await request(app).get('/meta').set(ver());
      await new Promise((r) => setImmediate(r));
      expect(info.mock.calls.some((c) => c[1] === 'request' && (c[0] as { path?: string }).path === '/meta')).toBe(true);
      const routes = metrics.routes();
      expect(routes.find((r) => r.route === 'GET /no-such-path')).toMatchObject({ count: 1, errors: 1 });
    } finally {
      warn.mockRestore();
      info.mockRestore();
    }
  });
});
