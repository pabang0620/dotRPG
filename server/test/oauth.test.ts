import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setOauthFetch } from '../src/domains/auth/oauthProvider';
import { auth, buildApp, registerAccount, resetDb, shutdown, ver } from './helpers';

// 0045 카카오·Google 로그인: 공급자 HTTP는 가짜 fetch로 바꾼다(코드 -> 사용자 번호)
const BASE = 'https://game.example.test';
const ENV = {
  OAUTH_PUBLIC_BASE: BASE,
  KAKAO_CLIENT_ID: 'kakao-client',
  KAKAO_CLIENT_SECRET: 'kakao-secret',
  GOOGLE_CLIENT_ID: 'google-client.apps.googleusercontent.com',
  GOOGLE_CLIENT_SECRET: 'google-secret',
  RATE_OAUTH_START_IP_MAX: '1000',
};
const app = buildApp(ENV);

/** code 값 = 공급자 사용자 번호(가짜). 'bad' 는 토큰 교환 실패 */
const calls: string[] = [];
function fakeFetch(url: string, init?: RequestInit): Promise<Response> {
  calls.push(url);
  const j = (status: number, body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));
  if (url === 'https://kauth.kakao.com/oauth/token' || url === 'https://oauth2.googleapis.com/token') {
    const form = new URLSearchParams(String(init?.body ?? ''));
    const code = form.get('code') ?? '';
    if (code === 'bad') return j(400, { error: 'invalid_grant' });
    if (url.includes('kakao')) {
      expect(form.get('client_secret')).toBe('kakao-secret');
      expect(form.get('redirect_uri')).toBe(`${BASE}/auth/oauth/callback/kakao`);
      return j(200, { access_token: `kat-${code}` });
    }
    return j(200, { id_token: `gid-${code}` });
  }
  if (url === 'https://kapi.kakao.com/v2/user/me') {
    const h = new Headers(init?.headers);
    const code = (h.get('Authorization') ?? '').replace('Bearer kat-', '');
    return j(200, { id: Number(code) });
  }
  if (url.startsWith('https://oauth2.googleapis.com/tokeninfo?id_token=')) {
    const code = decodeURIComponent(url.split('=')[1] ?? '').replace('gid-', '');
    const aud = code === '999' ? 'someone-else' : 'google-client.apps.googleusercontent.com';
    return j(200, { aud, iss: 'https://accounts.google.com', sub: code, exp: String(Math.floor(Date.now() / 1000) + 600) });
  }
  return j(404, {});
}

beforeAll(async () => {
  await resetDb();
  setOauthFetch(fakeFetch as never);
});
afterAll(async () => {
  setOauthFetch(null);
  await shutdown();
});

const num = () => String(1e9 + Math.floor(Math.random() * 1e9));
const start = (provider: string, headers: Record<string, string> = ver(), mode?: string) =>
  request(app).post('/auth/oauth/start').set(headers).send(mode ? { provider, mode } : { provider });
const callback = (provider: string, q: Record<string, string>) => request(app).get(`/auth/oauth/callback/${provider}`).query(q);
const poll = (s: { state: string; poll_secret: string }) => request(app).post('/auth/oauth/poll').set(ver()).send({ state: s.state, poll_secret: s.poll_secret });

/** 시작 -> 콜백 -> 폴링 한 바퀴 */
async function full(provider: 'kakao' | 'google', subject: string, headers?: Record<string, string>, mode?: string) {
  const s = (await start(provider, headers, mode)).body.data;
  expect((await callback(provider, { code: subject, state: s.state })).status).toBe(200);
  return poll(s);
}

describe('카카오·Google 로그인 (0045)', () => {
  it('providers: 설정이 없으면 둘 다 false, start 는 404', async () => {
    // 설정은 전역이라 마지막에 만든 앱의 값이 쓰인다: 끄고 확인한 뒤 다시 켠다
    const off = buildApp({});
    expect((await request(off).get('/auth/oauth/providers').set(ver())).body.data).toEqual({ kakao: false, google: false });
    const st = await request(off).post('/auth/oauth/start').set(ver()).send({ provider: 'kakao' });
    expect(st.status).toBe(404);
    expect(st.body.errors.code).toBe('OAUTH_PROVIDER_DISABLED');
    buildApp(ENV);
    expect((await request(app).get('/auth/oauth/providers').set(ver())).body.data).toEqual({ kakao: true, google: true });
  });

  it('start: 인가 주소에 state·redirect_uri, 카카오는 추가 동의 항목을 요청하지 않는다', async () => {
    const r = await start('kakao');
    expect(r.status).toBe(200);
    const u = new URL(r.body.data.authorize_url);
    expect(u.origin + u.pathname).toBe('https://kauth.kakao.com/oauth/authorize');
    expect(u.searchParams.get('state')).toBe(r.body.data.state);
    expect(u.searchParams.get('redirect_uri')).toBe(`${BASE}/auth/oauth/callback/kakao`);
    expect(u.searchParams.get('scope')).toBeNull();
    expect(r.body.data.poll_secret).toMatch(/^[A-Za-z0-9_-]{43}$/);
    const g = new URL((await start('google')).body.data.authorize_url);
    expect(g.searchParams.get('scope')).toBe('openid');
  });

  it('카카오: 처음이면 계정을 만들고, 다시 하면 같은 계정. /me 에 provider kakao', async () => {
    const id = num();
    const a = await full('kakao', id);
    expect(a.status).toBe(200);
    expect(a.body.data).toMatchObject({ status: 'done', mode: 'login', provider: 'kakao', created: true });
    expect(a.body.data.access_token).toBeTruthy();
    const me = await request(app).get('/me').set(ver({ Authorization: `Bearer ${a.body.data.access_token}` }));
    expect(me.body.data).toMatchObject({ provider: 'kakao', providers: ['kakao'] });
    const b = await full('kakao', id);
    expect(b.body.data.created).toBe(false);
    expect(b.body.data.account.id).toBe(a.body.data.account.id);
    const ev = await getPool().query("SELECT count(*) AS n FROM login_events WHERE kind = 'oauth_login'");
    expect(Number((ev.rows[0] as { n: string }).n)).toBeGreaterThanOrEqual(2);
  });

  it('Google: id_token 확인, 발급 대상이 다르면 실패', async () => {
    const ok = await full('google', num());
    expect(ok.body.data).toMatchObject({ status: 'done', provider: 'google', created: true });
    const s = (await start('google')).body.data;
    expect((await callback('google', { code: '999', state: s.state })).status).toBe(400);
    expect((await poll(s)).body.data).toEqual({ status: 'error', code: 'exchange_failed' });
  });

  it('폴링: 콜백 전 pending, 비밀이 틀리면 404, 가져간 뒤 다시 하면 409', async () => {
    const s = (await start('kakao')).body.data;
    expect((await poll(s)).body.data).toEqual({ status: 'pending' });
    const wrong = await poll({ state: s.state, poll_secret: 'A'.repeat(43) });
    expect(wrong.status).toBe(404);
    expect(wrong.body.errors.code).toBe('OAUTH_SESSION_NOT_FOUND');
    await callback('kakao', { code: num(), state: s.state });
    expect((await poll(s)).body.data.status).toBe('done');
    const again = await poll(s);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('OAUTH_SESSION_USED');
  });

  it('콜백: 사용자가 취소, 교환 실패, 다른 공급자 주소, 같은 콜백 두 번', async () => {
    const c = (await start('kakao')).body.data;
    expect((await callback('kakao', { error: 'access_denied', state: c.state })).status).toBe(400);
    expect((await poll(c)).body.data).toEqual({ status: 'error', code: 'cancelled' });

    const b = (await start('kakao')).body.data;
    await callback('kakao', { code: 'bad', state: b.state });
    expect((await poll(b)).body.data).toEqual({ status: 'error', code: 'exchange_failed' });

    const x = (await start('kakao')).body.data;
    expect((await callback('google', { code: num(), state: x.state })).status).toBe(400);
    expect((await poll(x)).body.data).toEqual({ status: 'pending' });

    const id = num();
    const d = (await start('kakao')).body.data;
    expect((await callback('kakao', { code: id, state: d.state })).status).toBe(200);
    expect((await callback('kakao', { code: num(), state: d.state })).status).toBe(400); // 첫 결과가 남는다
    const done = await poll(d);
    const row = await getPool().query("SELECT subject FROM auth_identities i JOIN accounts a ON a.id = i.account_id WHERE a.uuid = $1 AND i.provider = 'kakao'", [done.body.data.account.id]);
    expect((row.rows[0] as { subject: string }).subject).toBe(id);
  });

  it('콜백 화면에는 토큰도 번호도 없다', async () => {
    const id = num();
    const s = (await start('kakao')).body.data;
    const page = await callback('kakao', { code: id, state: s.state });
    expect(page.headers['content-type']).toMatch(/html/);
    expect(page.text).not.toContain(id);
    expect(page.text).not.toContain(s.poll_secret);
  });

  it('만료: 10분이 지나면 expired, 그 뒤 콜백은 받지 않는다', async () => {
    const s = (await start('kakao')).body.data;
    await getPool().query("UPDATE oauth_sessions SET expires_at = now() - interval '1 second' WHERE state = $1", [s.state]);
    expect((await poll(s)).body.data).toEqual({ status: 'error', code: 'expired' });
    expect((await callback('kakao', { code: num(), state: s.state })).status).toBe(400);
  });

  it('연동: dev 계정에 카카오를 붙이고 그 카카오로 로그인하면 같은 계정', async () => {
    const dev = await registerAccount(app);
    expect((await start('kakao', ver(), 'link')).status).toBe(401); // 연동은 로그인 필요
    const id = num();
    const l = await full('kakao', id, auth(dev), 'link');
    expect(l.body.data).toEqual({ status: 'done', mode: 'link', provider: 'kakao', linked: true });
    expect((await request(app).get('/me').set(auth(dev))).body.data.providers).toEqual(['dev', 'kakao']);
    const login = await full('kakao', id);
    expect(login.body.data.account.id).toBe(dev.accountId);
  });

  it('연동: 이미 다른 계정에 붙은 카카오 -> OAUTH_ALREADY_LINKED, 이미 카카오가 있는 계정 -> ACCOUNT_ALREADY_LINKED', async () => {
    const taken = num();
    await full('kakao', taken); // 카카오 계정이 따로 생김
    const dev = await registerAccount(app);
    const a = await full('kakao', taken, auth(dev), 'link');
    expect(a.status).toBe(409);
    expect(a.body.errors.code).toBe('OAUTH_ALREADY_LINKED');
    expect((await full('kakao', num(), auth(dev), 'link')).body.data.linked).toBe(true);
    const b = await full('kakao', num(), auth(dev), 'link');
    expect(b.status).toBe(409);
    expect(b.body.errors.code).toBe('ACCOUNT_ALREADY_LINKED');
  });

  it('정지된 계정은 카카오로도 로그인할 수 없다', async () => {
    const id = num();
    const first = await full('kakao', id);
    await getPool().query("UPDATE accounts SET banned_until = now() + interval '1 day' WHERE uuid = $1", [first.body.data.account.id]);
    const r = await full('kakao', id);
    expect(r.status).toBe(403);
    expect(r.body.errors.code).toBe('ACCOUNT_BANNED');
  });

  it('입력 오류: 모르는 공급자, 형식이 틀린 state', async () => {
    expect((await start('naver')).status).toBe(400);
    expect((await request(app).post('/auth/oauth/poll').set(ver()).send({ state: 'x', poll_secret: 'y' })).status).toBe(400);
    expect((await request(app).get('/auth/oauth/callback/naver').query({ code: '1', state: 'x' })).status).toBe(400);
  });
});
