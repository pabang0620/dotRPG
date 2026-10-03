import { createHash, randomUUID } from 'node:crypto';
import jwt from 'jsonwebtoken';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { DATA_VERSION, buildApp, randomLoginId, registerAccount, resetDb, shutdown, ver } from './helpers';

const app = buildApp();

beforeAll(resetDb);
afterAll(shutdown);

const sha = (s: string): string => createHash('sha256').update(s).digest('hex');

describe('POST /auth/dev/register', () => {
  it('정상: 201 + 토큰 쌍, 비밀번호는 argon2id 해시로 저장', async () => {
    const loginId = randomLoginId();
    const res = await request(app)
      .post('/auth/dev/register')
      .set(ver())
      .send({ login_id: loginId.toUpperCase(), password: 'password-1234' });
    expect(res.status).toBe(201);
    expect(res.body.data.access_expires_in).toBe(900);
    expect(res.body.data.refresh_token).toEqual(expect.any(String));
    expect(res.body.data.account.id).toMatch(/^[0-9a-f-]{36}$/);
    expect(JSON.stringify(res.body)).not.toMatch(/"id":\d+/);
    const row = await getPool().query('SELECT subject, secret_hash FROM auth_identities WHERE subject = $1', [loginId]);
    expect(row.rows[0].secret_hash).toMatch(/^\$argon2id\$/);
  });

  it('입력 오류: 400 VALIDATION + fields', async () => {
    const res = await request(app)
      .post('/auth/dev/register')
      .set(ver())
      .send({ login_id: 'ab', password: 'short' });
    expect(res.status).toBe(400);
    expect(res.body.errors.code).toBe('VALIDATION');
    expect(res.body.errors.fields.map((f: { path: string }) => f.path).sort()).toEqual(['login_id', 'password']);
  });

  it('중복 아이디: 409 LOGIN_ID_TAKEN', async () => {
    const s = await registerAccount(app);
    const res = await request(app)
      .post('/auth/dev/register')
      .set(ver())
      .send({ login_id: s.loginId, password: 'password-1234' });
    expect(res.status).toBe(409);
    expect(res.body.errors.code).toBe('LOGIN_ID_TAKEN');
  });

  it('깨진 JSON은 400, 64KB 초과는 413', async () => {
    const bad = await request(app).post('/auth/dev/register').set(ver()).set('Content-Type', 'application/json').send('{oops');
    expect(bad.status).toBe(400);
    const big = await request(app)
      .post('/auth/dev/register')
      .set(ver())
      .send({ login_id: 'abcd', password: 'x'.repeat(70_000) });
    expect(big.status).toBe(413);
    expect(big.body.errors.code).toBe('PAYLOAD_TOO_LARGE');
  });
});

describe('POST /auth/dev/login', () => {
  it('정상: 200, last_login_at 갱신', async () => {
    const s = await registerAccount(app);
    const res = await request(app).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: s.password });
    expect(res.status).toBe(200);
    expect(res.body.data.account.last_login_at).toEqual(expect.any(String));
    expect(res.body.data.refresh_token).not.toBe(s.refresh);
  });

  it('비밀번호 틀림·없는 아이디는 같은 401', async () => {
    const s = await registerAccount(app);
    const a = await request(app).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: 'wrong-password' });
    const b = await request(app).post('/auth/dev/login').set(ver()).send({ login_id: randomLoginId(), password: 'wrong-password' });
    expect(a.status).toBe(401);
    expect(b.status).toBe(401);
    expect(a.body).toEqual(b.body);
    expect(a.body.errors.code).toBe('INVALID_CREDENTIALS');
  });

  it('입력 오류: 400', async () => {
    const res = await request(app).post('/auth/dev/login').set(ver()).send({ login_id: 'a b', password: 'x' });
    expect(res.status).toBe(400);
  });

  it('정지 계정: 비밀번호가 맞을 때만 403 ACCOUNT_BANNED', async () => {
    const s = await registerAccount(app);
    await getPool().query(
      `UPDATE accounts SET banned_until = now() + interval '1 day' WHERE uuid = $1`,
      [s.accountId],
    );
    const ok = await request(app).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: s.password });
    expect(ok.status).toBe(403);
    expect(ok.body.errors.code).toBe('ACCOUNT_BANNED');
    expect(ok.body.errors.banned_until).toEqual(expect.any(String));
    const bad = await request(app).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: 'wrong-password' });
    expect(bad.status).toBe(401);
  });

  it('아이디당 실패 5회 후 429 + Retry-After, 성공하면 초기화', async () => {
    const limited = buildApp({ RATE_LOGIN_FAIL_MAX: '3' });
    const s = await registerAccount(limited);
    for (let i = 0; i < 2; i++) {
      await request(limited).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: 'wrong-password' });
    }
    // 성공하면 실패 횟수가 초기화된다
    const ok = await request(limited).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: s.password });
    expect(ok.status).toBe(200);
    for (let i = 0; i < 3; i++) {
      const r = await request(limited).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: 'wrong-password' });
      expect(r.status).toBe(401);
    }
    const blocked = await request(limited).post('/auth/dev/login').set(ver()).send({ login_id: s.loginId, password: s.password });
    expect(blocked.status).toBe(429);
    expect(blocked.body.errors.code).toBe('RATE_LIMITED');
    expect(Number(blocked.headers['retry-after'])).toBeGreaterThan(0);
    buildApp();
  });
});

describe('개발용 로그인 끄기', () => {
  it('AUTH_DEV_ENABLED=false 면 register/login 라우트가 없어 404', async () => {
    const off = buildApp({ AUTH_DEV_ENABLED: 'false' });
    const a = await request(off).post('/auth/dev/register').set(ver()).send({ login_id: 'abcd1234', password: 'password-1234' });
    const b = await request(off).post('/auth/dev/login').set(ver()).send({ login_id: 'abcd1234', password: 'password-1234' });
    expect(a.status).toBe(404);
    expect(b.status).toBe(404);
    const meta = await request(off).get('/meta');
    expect(meta.body.data.auth.dev_login_enabled).toBe(false);
    buildApp();
  });
});

describe('POST /auth/refresh', () => {
  it('정상: 새 토큰 쌍, 이전 토큰은 used_at 기록', async () => {
    const s = await registerAccount(app);
    const res = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(res.status).toBe(200);
    expect(res.body.data.refresh_token).not.toBe(s.refresh);
    const old = await getPool().query('SELECT used_at FROM refresh_tokens WHERE token_hash = $1', [sha(s.refresh)]);
    expect(old.rows[0].used_at).not.toBeNull();
    const me = await request(app).get('/me').set(ver({ Authorization: `Bearer ${res.body.data.access_token}` }));
    expect(me.status).toBe(200);
  });

  it('재사용 감지: 이미 쓴 토큰 -> 401 REFRESH_REUSED + 같은 묶음 전체 폐기', async () => {
    const s = await registerAccount(app);
    const first = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(first.status).toBe(200);
    const reuse = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(reuse.status).toBe(401);
    expect(reuse.body.errors.code).toBe('REFRESH_REUSED');
    // 정상 교체로 받은 새 토큰도 폐기되어 쓸 수 없다 (폐기가 커밋되었음)
    const sibling = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: first.body.data.refresh_token });
    expect(sibling.status).toBe(401);
    expect(sibling.body.errors.code).toBe('REFRESH_REUSED');
    const rows = await getPool().query(
      `SELECT count(*)::int AS n FROM refresh_tokens
        WHERE family_id = (SELECT family_id FROM refresh_tokens WHERE token_hash = $1) AND revoked_at IS NULL`,
      [sha(s.refresh)],
    );
    expect(rows.rows[0].n).toBe(0);
  });

  it('동시에 같은 토큰으로 refresh 2회: 하나만 성공', async () => {
    const s = await registerAccount(app);
    const [a, b] = await Promise.all([
      request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh }),
      request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh }),
    ]);
    expect([a.status, b.status].sort()).toEqual([200, 401]);
  });

  it('없는 토큰 401 REFRESH_INVALID, 만료 401 REFRESH_EXPIRED', async () => {
    const none = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: 'nope' });
    expect(none.status).toBe(401);
    expect(none.body.errors.code).toBe('REFRESH_INVALID');
    const s = await registerAccount(app);
    await getPool().query(`UPDATE refresh_tokens SET expires_at = now() - interval '1 second' WHERE token_hash = $1`, [sha(s.refresh)]);
    const exp = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(exp.status).toBe(401);
    expect(exp.body.errors.code).toBe('REFRESH_EXPIRED');
  });

  it('입력 오류 400, 정지 계정 403', async () => {
    const bad = await request(app).post('/auth/refresh').set(ver()).send({});
    expect(bad.status).toBe(400);
    const s = await registerAccount(app);
    await getPool().query(`UPDATE accounts SET banned_until = now() + interval '1 hour' WHERE uuid = $1`, [s.accountId]);
    const res = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(res.status).toBe(403);
  });

  it('계정당 속도 제한 429', async () => {
    const limited = buildApp({ RATE_REFRESH_ACCOUNT_MAX: '2' });
    const s = await registerAccount(limited);
    let token = s.refresh;
    for (let i = 0; i < 2; i++) {
      const r = await request(limited).post('/auth/refresh').set(ver()).send({ refresh_token: token });
      expect(r.status).toBe(200);
      token = r.body.data.refresh_token;
    }
    const r3 = await request(limited).post('/auth/refresh').set(ver()).send({ refresh_token: token });
    expect(r3.status).toBe(429);
    expect(r3.headers['retry-after']).toBeDefined();
    buildApp();
  });
});

describe('POST /auth/logout', () => {
  it('정상: 묶음 폐기, 반복 호출도 성공, 버전 헤더 없이도 가능', async () => {
    const s = await registerAccount(app);
    const res = await request(app).post('/auth/logout').send({ refresh_token: s.refresh });
    expect(res.status).toBe(200);
    expect(res.body.data).toEqual({ logged_out: true });
    const again = await request(app).post('/auth/logout').send({ refresh_token: s.refresh });
    expect(again.status).toBe(200);
    const unknown = await request(app).post('/auth/logout').send({ refresh_token: 'unknown' });
    expect(unknown.status).toBe(200);
    const use = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: s.refresh });
    expect(use.status).toBe(401);
  });

  it('입력 오류 400', async () => {
    const res = await request(app).post('/auth/logout').send({});
    expect(res.status).toBe(400);
  });
});

describe('GET /me와 액세스 토큰', () => {
  it('정상: 계정 정보', async () => {
    const s = await registerAccount(app);
    const res = await request(app).get('/me').set(ver({ Authorization: `Bearer ${s.access}` }));
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({
      account: { id: s.accountId },
      provider: 'dev',
      login_id: s.loginId,
      character_count: 0,
      character_limit: 4,
    });
  });

  it('토큰 없음/만료/위조: 401 코드가 각각 다르다', async () => {
    const none = await request(app).get('/me').set(ver());
    expect(none.status).toBe(401);
    expect(none.body.errors.code).toBe('TOKEN_MISSING');

    const s = await registerAccount(app);
    const expired = jwt.sign({}, process.env.JWT_SECRET as string, {
      algorithm: 'HS256',
      subject: s.accountId,
      expiresIn: -10,
    });
    const e = await request(app).get('/me').set(ver({ Authorization: `Bearer ${expired}` }));
    expect(e.body.errors.code).toBe('TOKEN_EXPIRED');

    const forged = jwt.sign({}, 'another-secret-another-secret-123456', { subject: s.accountId, expiresIn: 60 });
    const f = await request(app).get('/me').set(ver({ Authorization: `Bearer ${forged}` }));
    expect(f.body.errors.code).toBe('TOKEN_INVALID');

    const ghost = jwt.sign({}, process.env.JWT_SECRET as string, { subject: randomUUID(), expiresIn: 60 });
    const g = await request(app).get('/me').set(ver({ Authorization: `Bearer ${ghost}` }));
    expect(g.status).toBe(401);
  });

  it('정지·삭제 계정은 유효한 토큰이라도 막힌다', async () => {
    const s = await registerAccount(app);
    await getPool().query(`UPDATE accounts SET banned_until = now() + interval '1 hour' WHERE uuid = $1`, [s.accountId]);
    const banned = await request(app).get('/me').set(ver({ Authorization: `Bearer ${s.access}` }));
    expect(banned.status).toBe(403);
    expect(banned.body.errors.code).toBe('ACCOUNT_BANNED');
    await getPool().query(`UPDATE accounts SET banned_until = NULL, deleted_at = now() WHERE uuid = $1`, [s.accountId]);
    const deleted = await request(app).get('/me').set(ver({ Authorization: `Bearer ${s.access}` }));
    expect(deleted.status).toBe(401);
    expect(deleted.body.errors.code).toBe('TOKEN_INVALID');
  });
});

describe('버전 헤더 / 426', () => {
  it('클라이언트 버전: 없음 400, 낮음 426, 같거나 높음 통과', async () => {
    const none = await request(app).get('/me');
    expect(none.status).toBe(400);
    expect(none.body.errors.code).toBe('CLIENT_VERSION_MISSING');
    const bad = await request(app).get('/me').set({ 'X-Client-Version': 'abc' });
    expect(bad.status).toBe(400);

    const old = await request(app).post('/auth/dev/login').set({ 'X-Client-Version': '0.1.9' }).send({});
    expect(old.status).toBe(426);
    expect(old.body.errors).toMatchObject({
      code: 'CLIENT_OUTDATED',
      client_version: '0.1.9',
      min_client_version: '0.2.0',
    });
    // 숫자 비교(문자열 비교 아님): 0.10.0 > 0.2.0
    const newer = await request(app).get('/me').set({ 'X-Client-Version': '0.10.0' });
    expect(newer.status).toBe(401);
  });

  it('/auth 와 /me 는 데이터 버전을 검사하지 않는다', async () => {
    const res = await request(app).get('/me').set({ 'X-Client-Version': '0.2.0', 'X-Data-Version': 'ffffffffffffffff' });
    expect(res.status).toBe(401);
  });

  it('DATA_VERSION 상수가 서버 값과 같다', async () => {
    const meta = await request(app).get('/meta');
    expect(meta.body.data.data_version).toBe(DATA_VERSION);
  });
});
