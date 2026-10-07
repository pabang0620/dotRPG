// 9단계 3절: 기기·로그인 기록, 계정당 세션 하나(SESSION_REPLACED), 리프레시 기기 바인딩
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import jwt from 'jsonwebtoken';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { hmacDevice } from '../src/domains/antiabuse/deviceRecords';
import { handleSessionNotice } from '../src/domains/antiabuse/sessionListener';
import { auth, buildApp, createChar, randomName, randomLoginId, resetDb, shutdown, ver } from './helpers';
import { connect, startServer, sleep, until, type TestServer, WsClient } from './wsHelpers';
import { newHero } from './economyHelpers';

const rawDevice = (): { install_id: string; device_hash: string } => ({
  install_id: randomUUID(),
  device_hash: createHash('sha256').update(randomBytes(8)).digest('hex'),
});

let app: Express;
let srv: TestServer;
const sockets: WsClient[] = [];

beforeAll(async () => {
  app = buildApp();
  srv = await startServer(app);
});
beforeEach(async () => {
  await resetDb();
  app = buildApp();
});
afterEach(() => {
  for (const c of sockets.splice(0)) c.close();
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

const register = (a: Express, device?: object, loginId = randomLoginId()) =>
  request(a).post('/auth/dev/register').set(ver()).send({ login_id: loginId, password: 'password-1234', ...(device ? { device } : {}) });
const login = (a: Express, loginId: string, device?: object) =>
  request(a).post('/auth/dev/login').set(ver()).send({ login_id: loginId, password: 'password-1234', ...(device ? { device } : {}) });
const events = async (accountUuid: string): Promise<Record<string, unknown>[]> =>
  (
    await getPool().query(
      `SELECT e.kind, e.device_hash, host(e.ip) AS ip, e.install_id, e.flags, e.client_version FROM login_events e JOIN accounts a ON a.id = e.account_id
        WHERE a.uuid = $1 ORDER BY e.id`,
      [accountUuid],
    )
  ).rows;
const me = (a: Express, access: string) => request(a).get('/me').set(ver({ Authorization: `Bearer ${access}` }));

describe('기기·로그인 기록 (3.2)', () => {
  it('정상: 가입·로그인마다 login_events 1줄과 account_devices·account_ips upsert, 기기 해시는 HMAC으로만 저장된다', async () => {
    const d = rawDevice();
    const loginId = randomLoginId();
    const reg = await register(app, d, loginId);
    expect(reg.status).toBe(201);
    expect(reg.body.data.session).toEqual({ replaced_other: false });
    const acct = reg.body.data.account.id as string;
    let ev = await events(acct);
    expect(ev).toHaveLength(1);
    expect(ev[0]).toMatchObject({ kind: 'register', device_hash: hmacDevice(d.device_hash), install_id: d.install_id, ip: '127.0.0.1', client_version: '0.2.0' });
    expect(ev[0]?.flags).toEqual([]);
    // 원문 해시는 어디에도 저장되지 않는다
    const raw = await getPool().query('SELECT count(*) AS n FROM login_events WHERE device_hash = $1', [d.device_hash]);
    expect(Number((raw.rows[0] as { n: string }).n)).toBe(0);

    expect((await login(app, loginId, d)).status).toBe(200);
    ev = await events(acct);
    expect(ev.map((e) => e.kind)).toEqual(['register', 'login']);
    const dev = await getPool().query('SELECT seen_count FROM account_devices WHERE device_hash = $1', [hmacDevice(d.device_hash)]);
    expect(dev.rows).toEqual([{ seen_count: 2 }]);
    const ips = await getPool().query("SELECT host(ip) AS ip, seen_count FROM account_ips WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1)", [acct]);
    expect(ips.rows).toEqual([{ ip: '127.0.0.1', seen_count: 2 }]);
    const active = await getPool().query('SELECT active_device_hash, active_install_id FROM accounts WHERE uuid = $1', [acct]);
    expect(active.rows[0]).toEqual({ active_device_hash: hmacDevice(d.device_hash), active_install_id: d.install_id });
  });

  it('device가 없으면 device_missing 플래그(옛 클라이언트), "n/a" 기기는 install_id만 보낸다', async () => {
    const reg = await register(app);
    expect(reg.status).toBe(201);
    expect((await events(reg.body.data.account.id))[0]?.flags).toEqual(['device_missing']);
    const install = randomUUID();
    const noHash = await register(app, { install_id: install });
    expect(noHash.status).toBe(201);
    const row = (await events(noHash.body.data.account.id))[0];
    // device_hash를 생략하면 install_id에서 만든 기기 키를 대신 쓴다(기기 한도 우회 방지)
    expect(row).toMatchObject({ device_hash: hmacDevice(`install:${install}`), flags: ['device_missing'] });
    expect(await getPool().query('SELECT 1 FROM account_devices')).toHaveProperty('rowCount', 1);
  });

  it('입력 오류: 잘못된 device 형식은 400, DEVICE_INFO_REQUIRED=true면 device 없는 요청도 400', async () => {
    const bad = await register(app, { install_id: 'not-a-uuid', device_hash: 'zz' });
    expect(bad.status).toBe(400);
    expect(bad.body.errors.fields.map((f: { path: string }) => f.path).sort()).toEqual(['device.device_hash', 'device.install_id']);
    expect((await register(app, { install_id: randomUUID(), steam: 1 })).status).toBe(400);
    const strict = buildApp({ DEVICE_INFO_REQUIRED: 'true' });
    const none = await register(strict);
    expect(none.status).toBe(400);
    expect(none.body.errors.fields).toEqual([{ path: 'device', message: '필수 값입니다' }]);
    expect((await register(strict, rawDevice())).status).toBe(201);
  });
});

describe('같은 계정은 세션이 하나다 (3.3)', () => {
  it('두 번째 로그인: 첫 세션의 리프레시와 액세스 토큰이 SESSION_REPLACED, 접속 기록이 끝나고, replaced_other=true', async () => {
    const loginId = randomLoginId();
    const reg = await register(app, rawDevice(), loginId);
    const acct = reg.body.data.account.id as string;
    expect((await me(app, reg.body.data.access_token)).status).toBe(200);
    // 접속 중(프레즌스 진입)이던 계정
    const made = await createChar(app, { access: reg.body.data.access_token } as never, randomName());
    const charId = made.body.data.character.id as string;
    const enter = await request(app).post(`/characters/${charId}/presence`).set(auth({ access: reg.body.data.access_token } as never)).send({ map_id: 'village', auto_play: false, input_recent: true });
    expect(enter.status).toBe(200);

    const second = await login(app, loginId, rawDevice());
    expect(second.status).toBe(200);
    expect(second.body.data.session).toEqual({ replaced_other: true });
    // 옛 리프레시: REFRESH_REUSED(도난 의심)가 아니라 정상 교체
    const rf = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: reg.body.data.refresh_token });
    expect(rf.status).toBe(401);
    expect(rf.body.errors.code).toBe('SESSION_REPLACED');
    // 옛 액세스 토큰(15분 남았어도)도 즉시 무효
    const old = await me(app, reg.body.data.access_token);
    expect(old.status).toBe(401);
    expect(old.body.errors.code).toBe('SESSION_REPLACED');
    // 새 세션은 정상
    expect((await me(app, second.body.data.access_token)).status).toBe(200);
    expect((await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: second.body.data.refresh_token })).status).toBe(200);
    const on = await getPool().query('SELECT ended_at IS NOT NULL AS ended, end_reason FROM online_sessions WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1)', [acct]);
    expect(on.rows).toEqual([{ ended: true, end_reason: 'replaced' }]);
    expect((await events(acct)).find((e) => e.kind === 'login')?.flags).toEqual(['replaced_other', 'new_device']);
  });

  it('동시 로그인 두 개: 정확히 하나의 가족만 살아남는다(active_family_id와 일치)', async () => {
    const loginId = randomLoginId();
    const reg = await register(app, rawDevice(), loginId);
    const [a, b] = await Promise.all([login(app, loginId, rawDevice()), login(app, loginId, rawDevice())]);
    expect([a.status, b.status]).toEqual([200, 200]);
    const alive = await getPool().query(
      `SELECT DISTINCT family_id FROM refresh_tokens WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1) AND revoked_at IS NULL AND used_at IS NULL`,
      [reg.body.data.account.id],
    );
    expect(alive.rows).toHaveLength(1);
    const active = await getPool().query('SELECT active_family_id FROM accounts WHERE uuid = $1', [reg.body.data.account.id]);
    expect(alive.rows[0]?.family_id).toBe(active.rows[0]?.active_family_id);
    const statuses = await Promise.all([me(app, a.body.data.access_token), me(app, b.body.data.access_token)]);
    expect(statuses.map((r) => r.status).sort()).toEqual([200, 401]);
  });

  it('열려 있던 /ws 소켓이 4001로 닫히고 새 세션의 소켓은 닫히지 않는다(통지가 새 연결 뒤에 와도)', async () => {
    const h = await newHero(app);
    const first = await connect(srv.port, h);
    sockets.push(first.c);
    const second = await login(app, h.s.loginId);
    expect(second.status).toBe(200);
    expect(await first.c.waitClose()).toBe(4001);
    // 새 세션이 먼저 연결한 뒤 같은 통지가 늦게 도착해도 familyId가 같아 닫히지 않는다
    const h2 = { ...h, s: { ...h.s, access: second.body.data.access_token } };
    const fresh = await connect(srv.port, h2);
    sockets.push(fresh.c);
    const row = await getPool().query('SELECT id, active_family_id FROM accounts WHERE uuid = $1', [h.s.accountId]);
    handleSessionNotice(`${(row.rows[0] as { id: string }).id}:${(row.rows[0] as { active_family_id: string }).active_family_id}`);
    await sleep(200);
    expect(fresh.c.closed).toBeNull();
    // 다른 가족 id의 통지는 닫는다
    handleSessionNotice(`${(row.rows[0] as { id: string }).id}:${randomUUID()}`);
    expect(await fresh.c.waitClose()).toBe(4001);
  });

  it('sid 없는 옛 토큰은 active_family_id가 비어 있을 때만 통과하고, 설정된 뒤에는 거절한다', async () => {
    const reg = await register(app);
    const uuid = reg.body.data.account.id as string;
    const legacy = jwt.sign({}, process.env.JWT_SECRET as string, { algorithm: 'HS256', subject: uuid, expiresIn: 60 });
    await getPool().query('UPDATE accounts SET active_family_id = NULL WHERE uuid = $1', [uuid]);
    expect((await me(app, legacy)).status).toBe(200);
    await getPool().query('UPDATE accounts SET active_family_id = $2 WHERE uuid = $1', [uuid, randomUUID()]);
    const rejected = await me(app, legacy);
    expect(rejected.status).toBe(401);
    expect(rejected.body.errors.code).toBe('SESSION_REPLACED');
  });

  it('SESSION_SINGLE_MODE=off: 두 세션이 공존한다', async () => {
    const off = buildApp({ SESSION_SINGLE_MODE: 'off' });
    const loginId = randomLoginId();
    const reg = await register(off, rawDevice(), loginId);
    const second = await login(off, loginId, rawDevice());
    expect(second.body.data.session).toEqual({ replaced_other: false });
    expect((await me(off, reg.body.data.access_token)).status).toBe(200);
    expect((await me(off, second.body.data.access_token)).status).toBe(200);
    expect((await request(off).post('/auth/refresh').set(ver()).send({ refresh_token: reg.body.data.refresh_token })).status).toBe(200);
  });
});

describe('리프레시 기기 바인딩과 기록 증폭 (3.2, 3.3)', () => {
  it('log(기본): 다른 기기로 리프레시해도 통과하고 device_mismatch를 기록한다. 같은 (기기, IP)의 리프레시는 기록을 늘리지 않는다', async () => {
    const d = rawDevice();
    const reg = await register(app, d);
    const acct = reg.body.data.account.id as string;
    const same = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: reg.body.data.refresh_token, device: d });
    expect(same.status).toBe(200);
    expect((await events(acct)).map((e) => e.kind)).toEqual(['register']);
    const other = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: same.body.data.refresh_token, device: rawDevice() });
    expect(other.status).toBe(200);
    const ev = await events(acct);
    expect(ev.map((e) => e.kind)).toEqual(['register', 'refresh']);
    expect(ev[1]?.flags).toEqual(['device_mismatch', 'new_device']);
  });

  it('enforce: 가족을 만든 기기와 다르면 가족을 폐기하고 401 REFRESH_INVALID', async () => {
    const strict = buildApp({ REFRESH_DEVICE_BIND: 'enforce' });
    const d = rawDevice();
    const reg = await register(strict, d);
    const bad = await request(strict).post('/auth/refresh').set(ver()).send({ refresh_token: reg.body.data.refresh_token, device: rawDevice() });
    expect(bad.status).toBe(401);
    expect(bad.body.errors.code).toBe('REFRESH_INVALID');
    const reason = await getPool().query('SELECT DISTINCT revoke_reason FROM refresh_tokens WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1)', [reg.body.data.account.id]);
    expect(reason.rows).toEqual([{ revoke_reason: 'device_mismatch' }]);
    // 기기가 같으면 통과
    const ok = await register(strict, d);
    expect((await request(strict).post('/auth/refresh').set(ver()).send({ refresh_token: ok.body.data.refresh_token, device: d })).status).toBe(200);
  });

  it('로그아웃으로 폐기된 가족의 재사용은 기존대로 REFRESH_REUSED다(replaced만 SESSION_REPLACED)', async () => {
    const reg = await register(app);
    expect((await request(app).post('/auth/logout').send({ refresh_token: reg.body.data.refresh_token })).status).toBe(200);
    const again = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: reg.body.data.refresh_token });
    expect(again.body.errors.code).toBe('REFRESH_REUSED');
    void until;
  });
});
