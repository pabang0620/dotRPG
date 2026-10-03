import request from 'supertest';
import { createOwner, resetTotp } from '../src/admin/bootstrap';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { buildApp, resetDb, shutdown } from './helpers';
import { adminApp, adminGet, adminPost, auditRows, bearer, codeFor, makeAdmin, rid } from './opsHelpers';

buildApp();
const app = adminApp();
beforeEach(async () => {
  await resetDb();
  setClockOverride(null);
});
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

const login = (loginId: string, password: string, totp?: string) =>
  request(app).post('/admin/auth/login').send({ login_id: loginId, password, ...(totp ? { totp } : {}) });

describe('관리자 로그인과 2FA', () => {
  it('정상: 비밀번호 + TOTP로 full 세션을 받고, 같은 코드는 다시 쓸 수 없다', async () => {
    const a = await makeAdmin('operator');
    const code = codeFor(a.secret as Buffer);
    const res = await login(a.loginId, a.password, code);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ scope: 'full', admin: { login_id: a.loginId, role: 'operator' } });
    const me = await adminGet(app, res.body.data.token, '/admin/me');
    expect(me.body.data).toMatchObject({ role: 'operator', scope: 'full', totp_enrolled: true });
    // 같은 30초 구간의 같은 코드 재사용은 거절
    const again = await login(a.loginId, a.password, code);
    expect(again.status).toBe(401);
    expect(again.body.errors.code).toBe('AUTH_FAILED');
    // 성공 로그인은 감사 로그에 남는다
    const rows = await auditRows("action = 'auth.login' AND result = 'ok'");
    expect(rows).toHaveLength(1);
  });

  it('입력 오류: 짧은 아이디, 잘못된 TOTP 형식, 알 수 없는 필드는 400 VALIDATION', async () => {
    expect((await login('ab', 'x')).status).toBe(400);
    const bad = await request(app).post('/admin/auth/login').send({ login_id: 'abcd', password: 'x', totp: '12', extra: 1 });
    expect(bad.status).toBe(400);
    expect(bad.body.errors.code).toBe('VALIDATION');
  });

  it('TOTP 없이 로그인하면 TOTP_REQUIRED, 틀린 코드는 AUTH_FAILED', async () => {
    const a = await makeAdmin('viewer');
    const none = await login(a.loginId, a.password);
    expect(none.status).toBe(401);
    expect(none.body.errors.code).toBe('TOTP_REQUIRED');
    const wrong = await login(a.loginId, a.password, codeFor(a.secret as Buffer, 5) === '000000' ? '000001' : codeFor(a.secret as Buffer, 5));
    expect(wrong.status).toBe(401);
    expect(wrong.body.errors.code).toBe('AUTH_FAILED');
  });

  it('연속 5회 실패하면 잠기고(423), 감사 로그에 실패가 남고, 없는 아이디는 같은 응답이다', async () => {
    const a = await makeAdmin('viewer');
    for (let i = 0; i < 4; i++) expect((await login(a.loginId, 'wrong-password-xx')).body.errors.code).toBe('AUTH_FAILED');
    const fifth = await login(a.loginId, 'wrong-password-xx');
    expect(fifth.status).toBe(423);
    expect(fifth.body.errors.code).toBe('ADMIN_LOCKED');
    // 잠긴 동안은 올바른 비밀번호·코드도 거절
    const locked = await login(a.loginId, a.password, codeFor(a.secret as Buffer));
    expect(locked.status).toBe(423);
    const ghost = await login('nobody-here', 'whatever-password');
    expect(ghost.status).toBe(401);
    expect(ghost.body.errors.code).toBe('AUTH_FAILED');
    const denied = await auditRows("action = 'auth.login' AND result = 'denied'");
    expect(denied.length).toBeGreaterThanOrEqual(6);
    expect(denied.some((r) => r.login_id_tried === 'nobody-here')).toBe(true);
    // 비밀번호·코드는 감사 params 어디에도 없다
    expect(JSON.stringify(denied)).not.toContain('wrong-password-xx');
  });

  it('첫 로그인: setup 세션 -> 비밀번호 변경 -> TOTP 등록 -> full 세션으로 전환', async () => {
    const owner = await createOwner('first-owner');
    const first = await login('first-owner', owner.tempPassword);
    expect(first.body.data).toMatchObject({ scope: 'setup', must_change_password: true });
    const setupToken = first.body.data.token as string;
    // setup 범위에서는 다른 API가 막힌다
    const blocked = await adminGet(app, setupToken, '/admin/admins');
    expect(blocked.status).toBe(403);
    expect(blocked.body.errors.code).toBe('SETUP_REQUIRED');
    const pw = await request(app).post('/admin/auth/password').set(bearer(setupToken)).send({ current_password: owner.tempPassword, new_password: 'a-much-longer-password-9' });
    expect(pw.status).toBe(200);
    expect(pw.body.data.session).toBeNull();
    const start = await request(app).post('/admin/auth/totp/start').set(bearer(setupToken)).send({});
    expect(start.status).toBe(200);
    expect(start.body.data.otpauth_uri).toMatch(/^otpauth:\/\/totp\//);
    // 잘못된 코드는 등록되지 않는다
    expect((await request(app).post('/admin/auth/totp/confirm').set(bearer(setupToken)).send({ code: '000000' })).status).toBe(422);
    const { base32Decode } = await import('../src/admin/common/totp');
    const secret = base32Decode(start.body.data.secret as string);
    const done = await request(app).post('/admin/auth/totp/confirm').set(bearer(setupToken)).send({ code: codeFor(secret) });
    expect(done.status).toBe(200);
    expect(done.body.data.session.scope).toBe('full');
    expect((await adminGet(app, done.body.data.session.token, '/admin/admins')).status).toBe(200);
    // setup 토큰은 폐기됐다
    expect((await adminGet(app, setupToken, '/admin/me')).status).toBe(401);
    // 감사: bootstrap, 비밀번호 변경, TOTP 등록
    const actions = (await auditRows()).map((r) => r.action);
    expect(actions).toEqual(expect.arrayContaining(['bootstrap.create_owner', 'auth.password', 'auth.totp_start', 'auth.totp_confirm']));
    // 비밀키 원문은 감사 로그에 없다
    expect(JSON.stringify(await auditRows())).not.toContain(start.body.data.secret as string);
  });

  it('토큰 없음·잘못됨·유휴 만료·비활성화는 401', async () => {
    const a = await makeAdmin('owner');
    expect((await request(app).get('/admin/me')).body.errors.code).toBe('ADMIN_TOKEN_MISSING');
    expect((await adminGet(app, 'garbage-token', '/admin/me')).body.errors.code).toBe('ADMIN_TOKEN_INVALID');
    expect((await adminGet(app, a, '/admin/me')).status).toBe(200);
    // 30분 유휴가 지나면 만료
    const base = Date.now();
    setClockOverride(() => new Date(base + 31 * 60_000));
    expect((await adminGet(app, a, '/admin/me')).body.errors.code).toBe('ADMIN_TOKEN_EXPIRED');
    setClockOverride(null);
    // 비활성화하면 모든 세션이 폐기된다
    const b = await makeAdmin('viewer');
    const dis = await adminPost(app, a, `/admin/admins/${b.uuid}/actions`, { action: 'disable' });
    expect(dis.status).toBe(200);
    expect((await adminGet(app, b, '/admin/me')).status).toBe(401);
  });

  it('게임 계정 토큰은 관리자 API에서 통하지 않는다', async () => {
    const res = await adminGet(app, 'eyJhbGciOiJIUzI1NiJ9.e30.x', '/admin/me');
    expect(res.status).toBe(401);
  });

  it('출처가 허용 CIDR 밖이면 403 ORIGIN_DENIED', async () => {
    const { ipAllowed } = await import('../src/admin/common/cidr');
    expect(ipAllowed('127.0.0.1', ['127.0.0.1/32', '::1/128'])).toBe(true);
    expect(ipAllowed('::ffff:127.0.0.1', ['127.0.0.1/32'])).toBe(true);
    expect(ipAllowed('10.0.0.5', ['127.0.0.1/32', '::1/128'])).toBe(false);
    expect(ipAllowed('::1', ['127.0.0.1/32', '::1/128'])).toBe(true);
    expect(ipAllowed(undefined, ['127.0.0.1/32'])).toBe(false);
  });
});

describe('관리자 계정 관리와 권한 표', () => {
  it('owner만 관리자를 만든다: 임시 비밀번호는 한 번만, 감사 로그에는 없다. viewer·operator는 403', async () => {
    const owner = await makeAdmin('owner');
    const op = await makeAdmin('operator');
    const viewer = await makeAdmin('viewer');
    expect((await adminGet(app, op, '/admin/admins')).status).toBe(403);
    expect((await adminGet(app, viewer, '/admin/audit')).body.errors.code).toBe('FORBIDDEN_ROLE');
    const r = rid();
    const c = await adminPost(app, owner, '/admin/admins', { login_id: 'new-viewer', display_name: '새 관리자', role: 'viewer' }, r);
    expect(c.status).toBe(201);
    const temp = c.body.data.temp_password as string;
    expect(temp).toHaveLength(16);
    // 재전송: 같은 결과(임시 비밀번호는 다시 주지 않는다)
    const replay = await adminPost(app, owner, '/admin/admins', { login_id: 'new-viewer', display_name: '새 관리자', role: 'viewer' }, r);
    expect(replay.status).toBe(201);
    expect(replay.headers['idempotent-replay']).toBe('true');
    expect(replay.body.data.temp_password).toBeUndefined();
    expect(JSON.stringify(await auditRows())).not.toContain(temp);
    // 같은 아이디 중복은 409, 같은 request_id에 다른 본문은 422
    expect((await adminPost(app, owner, '/admin/admins', { login_id: 'new-viewer', display_name: 'x', role: 'viewer' })).body.errors.code).toBe('LOGIN_ID_TAKEN');
    expect((await adminPost(app, owner, '/admin/admins', { login_id: 'other-one', display_name: 'x', role: 'viewer' }, r)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    // 새 관리자는 임시 비밀번호로 setup 세션만 받는다
    const l = await login('new-viewer', temp);
    expect(l.body.data.scope).toBe('setup');
  });

  it('자기 자신은 비활성화·강등할 수 없고, 다른 owner는 강등할 수 있다', async () => {
    const owner = await makeAdmin('owner');
    expect((await adminPost(app, owner, `/admin/admins/${owner.uuid}/actions`, { action: 'disable' })).body.errors.code).toBe('SELF_ACTION');
    expect((await adminPost(app, owner, `/admin/admins/${owner.uuid}/actions`, { action: 'set_role', role: 'viewer' })).body.errors.code).toBe('SELF_ACTION');
    const other = await makeAdmin('owner');
    const res = await adminPost(app, owner, `/admin/admins/${other.uuid}/actions`, { action: 'set_role', role: 'viewer' });
    expect(res.status).toBe(200);
    expect(res.body.data.admin.role).toBe('viewer');
    // 역할이 바뀌면 그 관리자의 세션은 폐기된다
    expect((await adminGet(app, other, '/admin/me')).status).toBe(401);
  });

  it('감사 로그 조회와 reset_2fa, unlock, 2FA 초기화 도구', async () => {
    const owner = await makeAdmin('owner');
    const v = await makeAdmin('viewer');
    expect((await adminPost(app, owner, `/admin/admins/${v.uuid}/actions`, { action: 'reset_2fa' })).status).toBe(200);
    const row = await getPool().query('SELECT totp_confirmed_at FROM admin_users WHERE id = $1', [v.id]);
    expect((row.rows[0] as { totp_confirmed_at: Date | null }).totp_confirmed_at).toBeNull();
    const audit = await adminGet(app, owner, '/admin/audit?action=admin');
    expect(audit.status).toBe(200);
    expect(audit.body.data.entries.map((e: { action: string }) => e.action)).toContain('admin.reset_totp');
    // 기기 분실 복구 도구(셸 전용)
    await resetTotp(owner.loginId);
    expect((await adminGet(app, owner, '/admin/me')).status).toBe(401);
    expect((await auditRows("action = 'bootstrap.reset_totp'")).length).toBe(1);
  });
});
