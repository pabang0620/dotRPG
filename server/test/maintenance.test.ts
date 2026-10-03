import request from 'supertest';
import { getPool } from '../src/db/pool';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { announceTick, computeMarks, resetAnnouncer, START_TEXT } from '../src/ops/maintenanceAnnouncer';
import { getMaintenance, maintPhase, maintView, setMaintenance, type MaintWindow } from '../src/ops/maintenanceState';
import { maintenanceCloseJob } from '../src/ops/jobs/maintenanceClose';
import { setClockOverride } from '../src/utils/clock';
import { newHero } from './economyHelpers';
import { auth, buildApp, resetDb, shutdown, ver } from './helpers';
import { adminApp, adminGet, adminPost, makeAdmin, rid, type TestAdmin } from './opsHelpers';
import { connect, startServer, until, WsClient, type TestServer } from './wsHelpers';

const pub = buildApp();
const app = adminApp();
let srv: TestServer;
let operator: TestAdmin;
let viewer: TestAdmin;
const open: WsClient[] = [];
const MIN = 60_000;
const T0 = new Date('2026-10-05T03:00:00Z').getTime(); // 12:00 KST

const win = (startsInMin: number, durMin = 20, over: Partial<MaintWindow> = {}): MaintWindow => ({
  uuid: '11111111-1111-1111-1111-111111111111',
  notice: '',
  blockLoginAt: new Date(T0 + (startsInMin - 10) * MIN),
  startsAt: new Date(T0 + startsInMin * MIN),
  endsAt: new Date(T0 + (startsInMin + durMin) * MIN),
  ...over,
});
const at = (offsetMin: number): void => setClockOverride(() => new Date(T0 + offsetMin * MIN));

beforeAll(async () => {
  srv = await startServer(pub);
});
beforeEach(async () => {
  await resetDb();
  getRateLimitStore().clear();
  setMaintenance(null);
  resetAnnouncer();
  setClockOverride(null);
  [operator, viewer] = [await makeAdmin('operator'), await makeAdmin('viewer')];
});
afterEach(async () => {
  for (const c of open.splice(0)) c.close();
  await until(() => registry.size() === 0);
  setClockOverride(null);
  setMaintenance(null);
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

describe('점검 단계 전이(시계 주입)', () => {
  it('none -> scheduled -> pre_block -> active -> none, ends_at이 지나면 행이 열려 있어도 풀린다', () => {
    expect(maintPhase()).toBe('none');
    setMaintenance(win(30));
    const phases: [number, string][] = [[0, 'scheduled'], [19, 'scheduled'], [20, 'pre_block'], [29, 'pre_block'], [30, 'active'], [49, 'active'], [50, 'none']];
    for (const [t, p] of phases) {
      at(t);
      expect(maintPhase()).toBe(p);
    }
    at(35);
    expect(maintView()).toMatchObject({ phase: 'active', starts_at: new Date(T0 + 30 * MIN).toISOString() });
    at(60);
    expect(maintView()).toBeNull();
  });
});

describe('maintenanceGuard (허용/차단 표)', () => {
  it('pre_block: 새 로그인·새 판은 503 MAINTENANCE_PENDING, 토큰 갱신·다른 REST·헬스·/meta는 허용', async () => {
    const h = await newHero(pub);
    setMaintenance(win(30));
    at(25);
    const login = await request(pub).post('/auth/dev/login').set(ver()).send({ login_id: h.s.loginId, password: h.s.password });
    expect(login.status).toBe(503);
    expect(login.body.errors).toMatchObject({ code: 'MAINTENANCE_PENDING', notice: '' });
    expect(login.body.message).toContain('(KST)');
    expect(login.headers['retry-after']).toBeDefined();
    expect(Number(login.headers['retry-after'])).toBeLessThanOrEqual(300);
    for (const p of [`/characters/${h.id}/dungeon-runs`, `/characters/${h.id}/match/queue`, `/characters/${h.id}/party/start`, `/characters/${h.id}/match/fill-ai`]) {
      const r = await request(pub).post(p).set(auth(h.s)).send({ request_id: rid() });
      expect([p, r.body.errors.code]).toEqual([p, 'MAINTENANCE_PENDING']);
    }
    // 이미 접속한 사람의 토큰 갱신, 일반 REST, 로그아웃, 헬스, /meta
    expect((await request(pub).post('/auth/refresh').set(ver()).send({ refresh_token: h.s.refresh })).status).toBe(200);
    expect((await request(pub).get('/characters').set(auth(h.s))).status).toBe(200);
    expect((await request(pub).get('/health/live')).status).toBe(200);
    const meta = await request(pub).get('/meta');
    expect(meta.body.data.maintenance).toMatchObject({ phase: 'pre_block' });
    // WebSocket 재연결은 pre_block에서 허용
    const { c } = await connect(srv.port, h);
    open.push(c);
  });

  it('active: 거의 모든 요청이 503 MAINTENANCE(버전 검사보다 앞), /health·/meta·/auth/logout만 예외, WebSocket 업그레이드는 거절', async () => {
    const h = await newHero(pub);
    setMaintenance(win(30));
    at(35);
    const noHeader = await request(pub).get('/characters');
    expect(noHeader.status).toBe(503);
    expect(noHeader.body.errors.code).toBe('MAINTENANCE');
    expect(noHeader.body.message).toMatch(/\d\d:\d\d\(KST\)/);
    expect((await request(pub).get('/characters').set(auth(h.s))).body.errors.code).toBe('MAINTENANCE');
    expect((await request(pub).post('/auth/refresh').set(ver()).send({ refresh_token: h.s.refresh })).body.errors.code).toBe('MAINTENANCE');
    expect((await request(pub).post('/auth/steam').set(ver()).send({ ticket: 'a'.repeat(20) })).status).toBe(503);
    expect((await request(pub).get('/health/ready')).status).toBe(200);
    expect((await request(pub).get('/health')).status).toBe(200);
    expect((await request(pub).get('/meta')).body.data.maintenance.phase).toBe('active');
    expect((await request(pub).post('/auth/logout').send({ refresh_token: h.s.refresh })).status).not.toBe(503);
    await expect(WsClient.open(srv.port)).rejects.toThrow(/503/);
    // 점검이 끝나면(시각 경과) 다시 열린다
    at(60);
    expect((await request(pub).get('/characters').set(auth(h.s))).status).toBe(200);
  });

  it('scheduled(예고만): 아무것도 막지 않고 /meta에 예고가 나온다', async () => {
    const h = await newHero(pub);
    setMaintenance(win(60));
    at(0);
    expect((await request(pub).post('/auth/dev/login').set(ver()).send({ login_id: h.s.loginId, password: h.s.password })).status).toBe(200);
    expect((await request(pub).get('/meta')).body.data.maintenance).toMatchObject({ phase: 'scheduled' });
  });
});

describe('공지 시점 계산과 chat.sys, bye(MAINTENANCE)', () => {
  it('기본 값: 30·10·5·1분 전 공지, 10분 전에 새 로그인 제한 문장이 합쳐진다', () => {
    const marks = computeMarks(win(60, 20, { notice: '업데이트' }), [30, 10, 5, 1]);
    expect(marks.map((m) => (m.at.getTime() - T0) / MIN)).toEqual([30, 50, 55, 59, 60]);
    expect(marks[0]?.text).toContain('서버 점검이 30분 뒤(');
    expect(marks[0]?.text).toContain('업데이트');
    expect(marks[1]?.text).toContain('새 던전 입장과 새 로그인이 제한됩니다');
    expect(marks[4]?.text).toBe(START_TEXT);
    // block_login_at이 10분 전이 아니면 따로 한 줄
    const other = computeMarks(win(60, 20, { blockLoginAt: new Date(T0 + 52 * MIN) }), [30, 10, 5, 1]);
    expect(other.filter((m) => m.text.includes('제한됩니다'))).toHaveLength(1);
    expect(other).toHaveLength(6);
  });

  it('접속 중인 플레이어에게 공지가 시점마다 한 번씩 chat.sys로 가고, 시작 때 bye(MAINTENANCE)가 온다', async () => {
    const h = await newHero(pub);
    const { c } = await connect(srv.port, h);
    open.push(c);
    setMaintenance(win(30));
    at(0);
    const sys = (): string[] => c.frames.filter((f) => f.t === 'chat.sys').map((f) => f.text as string);
    expect(announceTick().texts).toHaveLength(1); // 30분 전
    expect(announceTick().texts).toHaveLength(0); // 같은 시점은 다시 보내지 않는다
    at(20);
    expect(announceTick().texts[0]).toContain('새 던전 입장과 새 로그인');
    at(25);
    announceTick();
    at(29);
    announceTick();
    await until(() => sys().length === 4);
    expect(sys()[0]).toContain('30분 뒤');
    expect(sys()[3]).toContain('1분 뒤');
    at(30);
    expect(announceTick().texts).toEqual([START_TEXT]);
    // 시작 + MAINT_BYE_DELAY_SECONDS(3초) 뒤에 bye
    expect(announceTick().byeSent).toBe(false);
    setClockOverride(() => new Date(T0 + 30 * MIN + 4000));
    expect(announceTick().byeSent).toBe(true);
    const bye = await c.waitT('bye');
    expect(bye).toMatchObject({ code: 1001, reason: 'MAINTENANCE', reconnect: true });
    expect(bye.retry_after_ms as number).toBeGreaterThanOrEqual(30_000);
    expect(bye.retry_after_ms as number).toBeLessThanOrEqual(630_000);
  });

  it('공지 시점을 창을 알기 전에 지났으면 건너뛴다(재시작·긴급 점검)', async () => {
    const h = await newHero(pub);
    const { c } = await connect(srv.port, h);
    open.push(c);
    setMaintenance(win(3)); // 3분 뒤 시작: 30·10·5분 전은 이미 지났다
    at(0);
    const r = announceTick();
    expect(r.texts.every((t) => !t.includes('30분 뒤') && !t.includes('5분 뒤'))).toBe(true);
    at(2);
    expect(announceTick().texts[0]).toContain('1분 뒤');
  });
});

describe('점검 관리자 API (MN1~MN7)', () => {
  it('예약 -> 중복 409 -> 취소(시작 전) -> 즉시 점검 -> 연장 -> 종료. 공개 서버에 곧바로 반영된다', async () => {
    const sched = await adminPost(app, operator, '/admin/maintenance', { start_in_minutes: 30, duration_minutes: 20, notice: '업데이트\n 안내' });
    expect(sched.status).toBe(201);
    expect(sched.body.data.window.notice).toBe('업데이트 안내');
    expect(sched.body.data.marks).toHaveLength(5);
    const id = sched.body.data.window.id as string;
    expect(getMaintenance()?.uuid).toBe(id);
    expect((await request(pub).get('/meta')).body.data.maintenance).toMatchObject({ phase: 'scheduled', notice: expect.any(String) });
    // 입력: 둘 다/아무것도 없음, 짧은 기간
    expect((await adminPost(app, operator, '/admin/maintenance', { duration_minutes: 20 })).status).toBe(400);
    expect((await adminPost(app, operator, '/admin/maintenance', { start_in_minutes: 5, duration_minutes: 1 })).status).toBe(400);
    expect((await adminPost(app, operator, '/admin/maintenance', { start_in_minutes: 5, duration_minutes: 20 })).body.errors.code).toBe('MAINTENANCE_EXISTS');
    expect((await adminPost(app, viewer, '/admin/maintenance', { start_in_minutes: 5, duration_minutes: 20 })).status).toBe(403);
    expect((await adminPost(app, operator, `/admin/maintenance/${id}/end`)).body.errors.code).toBe('MAINTENANCE_NOT_STARTED');
    expect((await adminPost(app, operator, `/admin/maintenance/${id}/cancel`)).status).toBe(200);
    expect(getMaintenance()).toBeNull();
    expect((await adminPost(app, operator, `/admin/maintenance/${id}/cancel`)).body.errors.code).toBe('MAINTENANCE_CLOSED');

    // 즉시 점검(start_in_minutes=0)
    const now = await adminPost(app, operator, '/admin/maintenance', { start_in_minutes: 0, duration_minutes: 10 });
    expect(maintPhase()).toBe('active');
    const nid = now.body.data.window.id as string;
    expect((await request(pub).get('/characters')).body.errors.code).toBe('MAINTENANCE');
    const ext = await adminPost(app, operator, `/admin/maintenance/${nid}/extend`, { extend_minutes: 30 });
    expect(Date.parse(ext.body.data.window.ends_at)).toBeGreaterThan(Date.now() + 35 * MIN);
    expect((await adminPost(app, operator, `/admin/maintenance/${nid}/cancel`)).body.errors.code).toBe('MAINTENANCE_STARTED');
    const status = await adminGet(app, viewer, '/admin/maintenance');
    expect(status.body.data).toMatchObject({ phase: 'active', window: { id: nid } });
    const end = await adminPost(app, operator, `/admin/maintenance/${nid}/end`);
    expect(end.status).toBe(200);
    expect(maintPhase()).toBe('none');
    expect((await request(pub).get('/health/ready')).status).toBe(200);
    const closed = await getPool().query('SELECT state, closed_by FROM maintenance_windows WHERE uuid = $1', [nid]);
    expect(closed.rows[0]).toMatchObject({ state: 'ended' });
    expect((await adminGet(app, viewer, '/admin/maintenance')).body.data.recent).toHaveLength(2);
  });

  it('드레인: 점검 중이고 접속·요청·판이 0일 때만 ready_to_stop', async () => {
    const h = await newHero(pub);
    const { c } = await connect(srv.port, h);
    open.push(c);
    await adminPost(app, operator, '/admin/maintenance', { start_in_minutes: 0, duration_minutes: 10 });
    expect((await adminGet(app, viewer, '/admin/maintenance/drain')).body.data).toMatchObject({ phase: 'active', ws_sessions: 1, ready_to_stop: false });
    c.close();
    await until(() => registry.size() === 0);
    const d = await adminGet(app, viewer, '/admin/maintenance/drain');
    expect(d.body.data).toMatchObject({ ws_sessions: 0, in_flight_requests: 0, playing_runs: 0, ready_to_stop: true });
    expect(d.body.data.running_ticks).toEqual({ match: 0, settle: 0, auction: 0, jobs: 0 });
  });

  it('즉시 방송: 접속 세션에 chat.sys, 100자 제한·제어 문자 제거, 10초에 한 번', async () => {
    const h = await newHero(pub);
    const { c } = await connect(srv.port, h);
    open.push(c);
    const r = await adminPost(app, operator, '/admin/broadcast', { text: '긴급\u0007 점검 안내' });
    expect(r.body.data.delivered).toBe(1);
    const f = await c.waitT('chat.sys');
    expect(f.text).toBe('긴급 점검 안내');
    expect((await adminPost(app, operator, '/admin/broadcast', { text: '또' })).status).toBe(429);
    expect((await adminPost(app, operator, '/admin/broadcast', { text: 'x'.repeat(101) })).status).toBe(400);
    expect((await adminPost(app, viewer, '/admin/broadcast', { text: 'x' })).status).toBe(403);
  });

  it('maintenance-close 작업은 종료 예정이 지난 창을 ended로 닫는다(closed_by 없음)', async () => {
    const admin = await makeAdmin('owner');
    await getPool().query(
      `INSERT INTO maintenance_windows (block_login_at, starts_at, ends_at, created_by, created_at)
       VALUES (now() - interval '2 hours', now() - interval '1 hour', now() - interval '10 minutes', $1, now() - interval '3 hours')`,
      [admin.id],
    );
    const r = await maintenanceCloseJob();
    expect(r.rows).toBe(1);
    const row = (await getPool().query('SELECT state, closed_by FROM maintenance_windows')).rows[0];
    expect(row).toEqual({ state: 'ended', closed_by: null });
    expect(getMaintenance()).toBeNull();
  });
});

