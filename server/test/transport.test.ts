import { randomBytes, randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { resetSwitchCooldowns } from '../src/domains/transport/fallbackService';
import { auth, buildApp, createChar, randomName, resetDb, shutdown, ver, type Session } from './helpers';
import { formParty, get, newHero, post, startAndBegin, type Hero } from './partyHelpers';
import { joinRoom, ticketFor } from './relayHelpers';
import { helloFrame, startServer, WsClient, type TestServer } from './wsHelpers';

const BASE = { RELAY_HOST_GRACE_MS: '300' };
let app = buildApp({ ...BASE, COMBAT_TRANSPORT_ORDER: 'relay,steam' });
let srv: TestServer;
const sockets: WsClient[] = [];

beforeAll(async () => {
  await resetDb();
  srv = await startServer(app);
});
afterAll(async () => {
  sockets.forEach((s) => s.close());
  await srv.stop();
  await shutdown();
});
beforeEach(() => resetSwitchCooldowns());

/** Steam 연결이 있는 계정의 캐릭터(mock 티켓으로 로그인) */
async function steamHero(): Promise<Hero> {
  const steamId = `7656119${Math.floor(Math.random() * 1e10).toString().padStart(10, '0')}`;
  const login = await request(app).post('/auth/steam').set(ver()).send({ ticket: `mock:${steamId}:${randomBytes(8).toString('hex')}` });
  const s: Session = { loginId: steamId, password: '', accountId: login.body.data.account.id, access: login.body.data.access_token, refresh: login.body.data.refresh_token };
  const c = await createChar(app, s, randomName());
  const id = c.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { s, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' };
}

/** /ws hello 에 caps.steam_p2p 를 실어 접속(전송 선택이 접속 중인 세션의 능력을 본다) */
async function withCaps(h: Hero): Promise<void> {
  const c = await WsClient.open(srv.port);
  c.send({ ...helloFrame(h), caps: { steam_p2p: true } });
  await c.waitT('ready');
  sockets.push(c);
}

const setup = async (order: string, n = 2, caps = true): Promise<{ heroes: Hero[]; start: Awaited<ReturnType<typeof post>> }> => {
  app = buildApp({ ...BASE, COMBAT_TRANSPORT_ORDER: order });
  const heroes: Hero[] = [];
  for (let i = 0; i < n; i++) heroes.push(await steamHero());
  if (caps) for (const h of heroes) await withCaps(h);
  await formParty(app, heroes[0] as Hero, heroes.slice(1));
  return { heroes, start: await post(app, heroes[0] as Hero, '/party/start', { ai_count: 0 }) };
};

const t2 = (h: Hero, runId: string, body: Record<string, unknown>, rid = randomUUID(), alias = false) =>
  post(app, h, `/rooms/run/${runId}/transport${alias ? '/fallback' : ''}`, body, rid);

describe('pickTransport: 서버가 판마다 정한다', () => {
  it('relay,steam: 모두 Steam 연결과 caps가 있으면 순서 [relay, steam], 한 명이라도 없으면 [relay]', async () => {
    const { start } = await setup('relay,steam');
    expect(start.status).toBe(201);
    expect(start.body.data.run.transport).toMatchObject({ current: 'relay', epoch: 1, order: ['relay', 'steam'] });
    expect(start.body.data.run.members[0].steam_id).toMatch(/^\d{17}$/);
    const lone = await steamHero();
    await withCaps(lone);
    const dev = await newHero(app);
    await formParty(app, dev, [lone]);
    const st = await post(app, dev, '/party/start', { ai_count: 0 });
    expect(st.body.data.run.transport.order).toEqual(['relay']);
  });

  it('steam,relay: Steam 자격이 있으면 steam 우선(host_key 응답, 입장 토큰 필수), 자격이 없으면 relay', async () => {
    const { heroes, start } = await setup('steam,relay');
    expect(start.body.data.run.transport).toMatchObject({ current: 'steam', order: ['steam', 'relay'] });
    const noCaps = await setup('steam,relay', 2, false);
    expect(noCaps.start.body.data.run.transport).toMatchObject({ current: 'relay', order: ['relay'] });
    expect(heroes).toHaveLength(2);
  });

  it('후보가 하나도 없으면 422 TRANSPORT_UNAVAILABLE(steam 만 허용하는데 Steam 자격이 없는 경우)', async () => {
    app = buildApp({ ...BASE, COMBAT_TRANSPORT_ORDER: 'steam' });
    const a = await newHero(app);
    const b = await newHero(app);
    await formParty(app, a, [b]);
    const st = await post(app, a, '/party/start', { ai_count: 0 });
    expect(st.status).toBe(422);
    expect(st.body.errors.code).toBe('TRANSPORT_UNAVAILABLE');
  });
});

describe('Steam 전송의 R3(join): 입장 토큰과 방장 SteamID', () => {
  it('steam 방은 entry_token 필수, host_steam_id 필수, 다르면 422 HOST_MISMATCH', async () => {
    const { heroes, start } = await setup('steam,relay');
    const runId = start.body.data.run.id as string;
    const m = heroes[1] as Hero;
    const token = (await get(app, m, '/party')).body.data.party.run.me.entry_token as string;
    expect((await post(app, m, `/party-runs/${runId}/join`, { host_steam_id: start.body.data.run.host.steam_id })).status).toBe(400);
    expect((await post(app, m, `/party-runs/${runId}/join`, { entry_token: token })).status).toBe(400);
    const wrong = await post(app, m, `/party-runs/${runId}/join`, { entry_token: token, host_steam_id: '76561190000000000' });
    expect(wrong.body.errors.code).toBe('HOST_MISMATCH');
    const ok = await post(app, m, `/party-runs/${runId}/join`, { entry_token: token, host_steam_id: start.body.data.run.host.steam_id });
    expect(ok.status).toBe(200);
    expect(ok.body.data.begun).toBe(true);
    // R2: 호스트에게만 host_key
    expect((await get(app, heroes[0] as Hero, `/party-runs/${runId}`)).body.data.host_key).toBeTruthy();
    expect((await get(app, m, `/party-runs/${runId}`)).body.data.host_key).toBeNull();
  });
});

describe('T2 전송 전환', () => {
  it('relay -> steam: 중계 연결에 transport.changed 와 4014, 새 티켓은 409 TRANSPORT_NOT_RELAY(현재 전송 안내)', async () => {
    const { heroes, start } = await setup('relay,steam');
    const runId = start.body.data.run.id as string;
    const [a, b] = heroes as [Hero, Hero];
    const ca = await joinRoom(app, srv.port, a, 'run', runId);
    const cb = await joinRoom(app, srv.port, b, 'run', runId);
    await ca.c.waitT('peer.joined');
    const sw = await t2(b, runId, { failed: 'relay', epoch: 1, reason: 'connect_failed' });
    expect(sw.status).toBe(200);
    expect(sw.body.data.transport).toMatchObject({ current: 'steam', epoch: 2, order: ['relay', 'steam'] });
    expect(await ca.c.waitT('transport.changed')).toMatchObject({ current: 'steam', epoch: 2 });
    expect(await ca.c.waitClose()).toBe(4014);
    expect(await cb.c.waitClose()).toBe(4014);
    const t = await ticketFor(app, a, 'run', runId);
    expect(t.status).toBe(409);
    expect(t.body.errors).toMatchObject({ code: 'TRANSPORT_NOT_RELAY', current: { transport: 'steam', epoch: 2 } });
    expect((await get(app, a, `/party-runs/${runId}`)).body.data.run.transport).toMatchObject({ current: 'steam', epoch: 2 });
    expect(Number((await getPool().query('SELECT transport_switches AS n FROM party_runs WHERE uuid = $1', [runId])).rows[0].n)).toBe(1);
  });

  it('steam -> relay(설계 문서 경로 /transport/fallback 도 같다), 더 갈 곳이 없으면 TRANSPORT_EXHAUSTED, 옛 세대는 TRANSPORT_CHANGED', async () => {
    const { heroes, start } = await setup('steam,relay');
    const runId = start.body.data.run.id as string;
    const m = heroes[1] as Hero;
    const rid = randomUUID();
    const bad = await t2(m, runId, { failed: 'relay', epoch: 1 }); // 현재 전송이 steam 이므로 이미 바뀐 것으로 본다
    expect(bad.status).toBe(409);
    expect(bad.body.errors).toMatchObject({ code: 'TRANSPORT_CHANGED', current: { transport: 'steam', epoch: 1 } });
    const ok = await t2(m, runId, { observed_epoch: 1, reason: 'connect_timeout' }, rid, true);
    expect(ok.status).toBe(200);
    expect(ok.body.data.transport).toMatchObject({ current: 'relay', epoch: 2 });
    expect((await t2(m, runId, { observed_epoch: 1, reason: 'connect_timeout' }, rid, true)).body).toEqual(ok.body); // 재전송
    const stale = await t2(heroes[0] as Hero, runId, { failed: 'steam', epoch: 1 });
    expect(stale.body.errors).toMatchObject({ code: 'TRANSPORT_CHANGED', current: { transport: 'relay', epoch: 2 } });
    const end = await t2(heroes[0] as Hero, runId, { failed: 'relay', epoch: 2 });
    expect(end.status).toBe(409);
    expect(end.body.errors.code).toBe('TRANSPORT_EXHAUSTED');
    // 이제 중계로 붙는다(되돌아가지 않는다)
    const j = await joinRoom(app, srv.port, m, 'run', runId);
    expect(j.ready).toMatchObject({ room: { transport_epoch: 2 } });
    j.c.close();
  });

  it('입력 오류와 권한: 알 수 없는 사유·필드 400, 남의 방 404, host_unreachable 은 호스트가 살아 있을 때만, 쿨다운 429', async () => {
    const { heroes, start } = await setup('steam,relay');
    const runId = start.body.data.run.id as string;
    const m = heroes[1] as Hero;
    expect((await t2(m, runId, { epoch: 1, reason: 'because' })).status).toBe(400);
    expect((await t2(m, runId, { epoch: 1, transport: 'relay' })).status).toBe(400);
    expect((await t2(m, runId, { epoch: 0 })).status).toBe(400);
    expect((await t2(await newHero(app), runId, { epoch: 1 })).body.errors.code).toBe('ROOM_NOT_FOUND');
    expect((await t2(m, runId, { epoch: 1, reason: 'host_unreachable' })).body.errors.code).toBe('HOST_NOT_ALIVE');
    app = buildApp({ ...BASE, COMBAT_TRANSPORT_ORDER: 'steam,relay', TRANSPORT_SWITCH_COOLDOWN_SECONDS: '30' });
    const ok = await t2(m, runId, { epoch: 1 });
    expect(ok.status).toBe(200);
    const again = await t2(m, runId, { epoch: 2 });
    expect(again.status).toBe(429);
    expect(again.body.errors.code).toBe('TRANSPORT_SWITCH_COOLDOWN');
    expect(again.headers['retry-after']).toBeTruthy();
  });

  it('동시 요청: 같은 세대로 두 멤버가 동시에 전환을 요청해도 한 번만 바뀐다', async () => {
    const { heroes, start } = await setup('steam,relay');
    const runId = start.body.data.run.id as string;
    const [x, y] = await Promise.all([t2(heroes[0] as Hero, runId, { epoch: 1 }), t2(heroes[1] as Hero, runId, { epoch: 1 })]);
    expect([x.status, y.status].sort()).toEqual([200, 409]);
    expect(Number((await getPool().query('SELECT transport_epoch AS n FROM party_runs WHERE uuid = $1', [runId])).rows[0].n)).toBe(2);
  });

  it('필드 세션: steam 전송이면 entry_token 을 주고, 전환하면 중계로 간다', async () => {
    app = buildApp({ ...BASE, COMBAT_TRANSPORT_ORDER: 'steam,relay' });
    const heroes = [await steamHero(), await steamHero()];
    for (const h of heroes) await withCaps(h);
    await formParty(app, heroes[0] as Hero, [heroes[1] as Hero]);
    const e = await post(app, heroes[0] as Hero, '/field-sessions/enter', { map_id: 'forest' });
    expect(e.body.data.session.transport).toMatchObject({ current: 'steam', order: ['steam', 'relay'] });
    expect(e.body.data.session.me.entry_token).toMatch(/^[A-Za-z0-9_-]{22}$/);
    const sid = e.body.data.session.id as string;
    await post(app, heroes[1] as Hero, '/field-sessions/enter', { map_id: 'forest' });
    const sw = await post(app, heroes[1] as Hero, `/rooms/field/${sid}/transport`, { epoch: 1, failed: 'steam' });
    expect(sw.body.data.transport).toMatchObject({ current: 'relay', epoch: 2 });
    const g = await get(app, heroes[0] as Hero, `/field-sessions/${sid}`);
    expect(g.body.data.session).toMatchObject({ transport: { current: 'relay', epoch: 2 }, me: { entry_token: null } });
    expect((await ticketFor(app, heroes[0] as Hero, 'field', sid)).status).toBe(200);
  });
});

describe('R5/R6 보강', () => {
  it('R5 하트비트 응답에 transport 와 members[].level, gear_hash 가 실린다', async () => {
    app = buildApp({ ...BASE, COMBAT_TRANSPORT_ORDER: 'relay,steam' });
    const a = await newHero(app);
    const b = await newHero(app);
    await formParty(app, a, [b]);
    const { runId } = await startAndBegin(app, a, [b]);
    const r = await request(app).post(`/characters/${b.id}/party-runs/${runId}/heartbeat`).set(auth(b.s)).send({ seen_epoch: 1 });
    expect(r.body.data).toMatchObject({ transport: { current: 'relay', epoch: 1 }, host_changed: false });
    expect(r.body.data.members[0]).toMatchObject({ level: 1 });
    expect(r.body.data.members[0].gear_hash).toMatch(/^[0-9a-f]{16}$/);
  });
});
