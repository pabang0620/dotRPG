import { randomBytes } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { buildApp, ver, resetDb, shutdown, registerAccount, auth } from './helpers';

// off 모드: 라우트 자체가 없다(404). 뒤에 mock 모드 앱을 만든다(설정은 전역이라 마지막 것이 쓰인다)
const appOff = buildApp({ STEAM_AUTH_MODE: 'off' });
const app = buildApp({ STEAM_AUTH_MODE: 'mock' });

beforeAll(resetDb);
afterAll(shutdown);

const steamId = () => `7656119${Math.floor(Math.random() * 1e10).toString().padStart(10, '0')}`;
const mockTicket = (id: string) => `mock:${id}:${randomBytes(8).toString('hex')}`;
const login = (ticket: string) => request(app).post('/auth/steam').set(ver()).send({ ticket });

describe('POST /auth/steam', () => {
  it('정상: 처음이면 계정을 만들고(201), 다시 로그인하면 같은 계정(200)', async () => {
    const id = steamId();
    const a = await login(mockTicket(id));
    expect(a.status).toBe(201);
    expect(a.body.data).toMatchObject({ created: true });
    expect(a.body.data.access_token).toBeTruthy();
    const me = await request(app).get('/me').set(auth({ access: a.body.data.access_token } as never));
    expect(me.body.data).toMatchObject({ provider: 'steam', login_id: id, steam_linked: true });
    const b = await login(mockTicket(id));
    expect(b.status).toBe(200);
    expect(b.body.data.created).toBe(false);
    expect(b.body.data.account.id).toBe(a.body.data.account.id);
  });

  it('입력 오류: 형식 불량, SteamID를 같이 보내도 받지 않는다, 잘못된 mock 티켓', async () => {
    expect((await request(app).post('/auth/steam').set(ver()).send({ ticket: 'zz' })).status).toBe(400);
    expect((await request(app).post('/auth/steam').set(ver()).send({ ticket: 'ab'.repeat(20), steam_id: '1' })).status).toBe(400);
    const bad = await login('abcdef0123456789abcdef');
    expect(bad.status).toBe(401);
    expect(bad.body.errors.code).toBe('STEAM_TICKET_INVALID');
    expect((await request(app).post('/auth/steam').send({ ticket: mockTicket(steamId()) })).status).toBe(400); // 버전 헤더 없음
  });

  it('재전송: 같은 티켓을 다시 보내면 TICKET_REPLAYED (계정이 또 만들어지지 않는다)', async () => {
    const t = mockTicket(steamId());
    expect((await login(t)).status).toBe(201);
    const again = await login(t);
    expect(again.status).toBe(401);
    expect(again.body.errors.code).toBe('TICKET_REPLAYED');
  });

  it('동시 요청: 같은 SteamID의 첫 로그인 두 개는 계정 하나로 합쳐진다', async () => {
    const id = steamId();
    const [a, b] = await Promise.all([login(mockTicket(id)), login(mockTicket(id))]);
    expect([a.status, b.status].every((s) => s === 200 || s === 201)).toBe(true);
    expect(a.body.data.account.id).toBe(b.body.data.account.id);
    const n = await getPool().query("SELECT count(*) AS n FROM auth_identities WHERE provider = 'steam' AND subject = $1", [id]);
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
  });

  it('off 모드에서는 라우트가 없다(404)', async () => {
    const res = await request(appOff).post('/auth/steam').set(ver()).send({ ticket: mockTicket(steamId()) });
    expect(res.status).toBe(404);
  });
});

describe('POST /auth/steam/link', () => {
  const link = (access: string, ticket: string) =>
    request(app).post('/auth/steam/link').set(ver({ Authorization: `Bearer ${access}` })).send({ ticket });

  it('정상: dev 계정에 Steam을 연결하고 /me 에 steam_linked, 이후 같은 Steam으로 로그인하면 그 계정', async () => {
    const s = await registerAccount(app);
    expect((await request(app).get('/me').set(auth(s))).body.data.steam_linked).toBe(false);
    const id = steamId();
    const res = await link(s.access, mockTicket(id));
    expect(res.status).toBe(200);
    expect(res.body.data).toEqual({ linked: true });
    expect((await request(app).get('/me').set(auth(s))).body.data.steam_linked).toBe(true);
    const viaSteam = await login(mockTicket(id));
    expect(viaSteam.body.data.account.id).toBe(s.accountId);
  });

  it('규칙 오류: 이미 다른 계정에 묶인 SteamID, 이미 Steam이 있는 계정, 로그인 필요, 입력 오류', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const id = steamId();
    expect((await link(a.access, mockTicket(id))).status).toBe(200);
    const taken = await link(b.access, mockTicket(id));
    expect(taken.status).toBe(409);
    expect(taken.body.errors.code).toBe('STEAM_ALREADY_LINKED');
    const dup = await link(a.access, mockTicket(steamId()));
    expect(dup.body.errors.code).toBe('ACCOUNT_ALREADY_LINKED');
    expect((await request(app).post('/auth/steam/link').set(ver()).send({ ticket: mockTicket(steamId()) })).status).toBe(401);
    expect((await link(b.access, 'xyz')).status).toBe(400);
  });

  it('동시 요청: 같은 계정에 서로 다른 Steam 두 개를 동시에 연결하면 하나만 성공', async () => {
    const s = await registerAccount(app);
    const [x, y] = await Promise.all([link(s.access, mockTicket(steamId())), link(s.access, mockTicket(steamId()))]);
    expect([x.status, y.status].sort()).toEqual([200, 409]);
  });
});
