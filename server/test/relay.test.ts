import http from 'node:http';
import type { AddressInfo } from 'node:net';
import jwt from 'jsonwebtoken';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getConfig } from '../src/config/env';
import { attachRelay } from '../src/domains/relay/relayServer';
import { relayHub } from '../src/domains/relay/relayHub';
import { relayMetrics } from '../src/domains/relay/relayMetrics';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { formParty, newHero, post, startAndBegin, type Hero } from './partyHelpers';
import { joinRoom, RelayClient, ticketFor } from './relayHelpers';
import { sleep, startServer, until, type TestServer } from './wsHelpers';

const OVERRIDES = { RELAY_MEMBER_PKT_PER_SEC: '10', RELAY_STRIKES_PER_MIN: '6', RELAY_HOST_GRACE_MS: '300' };
let app = buildApp(OVERRIDES);
let srv: TestServer;

beforeAll(async () => {
  await resetDb();
  srv = await startServer(app);
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

interface Room {
  heroes: Hero[];
  runId: string;
  conns: RelayClient[];
}

/** 방장(자리 0) + 멤버를 만들고 REST로 판을 시작한 뒤 모두 중계에 붙인다 */
async function room(n = 3, connect = true): Promise<Room> {
  const heroes: Hero[] = [];
  for (let i = 0; i < n; i++) heroes.push(await newHero(app));
  await formParty(app, heroes[0] as Hero, heroes.slice(1));
  const { runId } = await startAndBegin(app, heroes[0] as Hero, heroes.slice(1));
  const conns: RelayClient[] = [];
  if (connect) for (const h of heroes) conns.push((await joinRoom(app, srv.port, h, 'run', runId)).c);
  return { heroes, runId, conns };
}

const closeAll = (r: Room): void => r.conns.forEach((c) => c.close());

describe('T1 입장 티켓과 hello', () => {
  it('정상: 티켓 발급, 연결, ready(좌석·호스트·한도), peer.joined, PING/PONG', async () => {
    const r = await room(2, false);
    const t = await ticketFor(app, r.heroes[1] as Hero, 'run', r.runId);
    expect(t.status).toBe(200);
    expect(t.body.data).toMatchObject({ expires_in: 60, room: { seat: 1, host_seat: 0, host_epoch: 1, transport: { current: 'relay', epoch: 1 } } });
    expect(t.body.data.relay_url).toMatch(/\/relay$/);
    const host = await joinRoom(app, srv.port, r.heroes[0] as Hero, 'run', r.runId);
    expect(host.ready).toMatchObject({ t: 'ready', v: 1, seat: 0, host_seat: 0, host_epoch: 1, peers: [], flush_ms: 20 });
    expect((host.ready.limits as { packet_max_bytes: number }).packet_max_bytes).toBe(6144);
    const c = await RelayClient.open(srv.port);
    c.hello(t.body.data.ticket);
    const ready = await c.waitT('ready');
    expect(ready).toMatchObject({ seat: 1, peers: [0] });
    expect(await host.c.waitT('peer.joined')).toMatchObject({ seat: 1 });
    c.ping(4242);
    await until(() => c.pongs.includes(4242));
    host.c.close();
    c.close();
  });

  it('입력 오류: T1 본문에 값을 보내면 400, 남의 방·없는 방은 404 ROOM_NOT_FOUND', async () => {
    const r = await room(2, false);
    const bad = await request(app).post(`/characters/${(r.heroes[0] as Hero).id}/rooms/run/${r.runId}/relay-ticket`).set(auth((r.heroes[0] as Hero).s)).send({ seat: 2 });
    expect(bad.status).toBe(400);
    const stranger = await newHero(app);
    expect((await ticketFor(app, stranger, 'run', r.runId)).body.errors?.code).toBe('ROOM_NOT_FOUND');
    expect((await ticketFor(app, r.heroes[0] as Hero, 'run', '00000000-0000-4000-8000-000000000000')).body.errors?.code).toBe('ROOM_NOT_FOUND');
    expect((await ticketFor(app, r.heroes[0] as Hero, 'field', r.runId)).body.errors?.code).toBe('ROOM_NOT_FOUND');
  });

  it('티켓은 단일 사용이고, 위조·만료·좌석 불일치는 거절한다', async () => {
    const r = await room(2, false);
    const h = r.heroes[1] as Hero;
    const t = await ticketFor(app, h, 'run', r.runId);
    const a = await RelayClient.open(srv.port);
    a.hello(t.body.data.ticket);
    await a.waitT('ready');
    const b = await RelayClient.open(srv.port);
    b.hello(t.body.data.ticket); // 같은 티켓 재사용
    expect(await b.waitClose()).toBe(4004);
    const secret = getConfig().relay.ticketSecret;
    const claims = { v: 1, cid: h.id, k: 'run', rid: r.runId, seat: 1, ep: 1 };
    const sign = (over: Record<string, unknown>, opt: jwt.SignOptions = {}) =>
      jwt.sign({ ...claims, ...over }, secret, { algorithm: 'HS256', audience: 'dotrpg-relay', subject: h.s.accountId, jwtid: Math.random().toString(36), expiresIn: 60, ...opt });
    const tryTicket = async (ticket: string): Promise<number> => {
      const c = await RelayClient.open(srv.port);
      c.hello(ticket);
      return c.waitClose();
    };
    expect(await tryTicket('garbage')).toBe(4004);
    expect(await tryTicket(sign({}, { expiresIn: -5 }))).toBe(4004);
    expect(await tryTicket(jwt.sign(claims, 'x'.repeat(40), { algorithm: 'HS256', audience: 'dotrpg-relay', subject: h.s.accountId, jwtid: 'q', expiresIn: 60 }))).toBe(4004);
    expect(await tryTicket(sign({ seat: 2 }))).toBe(4011); // 좌석이 방 상태와 다르다
    expect(await tryTicket(sign({ ep: 9 }))).toBe(4014); // 전송 세대가 다르다
    a.close();
  });

  it('같은 좌석의 새 연결이 옛 연결을 4001로 대체하고, 와이어가 다르면 4015', async () => {
    const r = await room(2, false);
    const h = r.heroes[1] as Hero;
    const first = await joinRoom(app, srv.port, h, 'run', r.runId);
    const second = await joinRoom(app, srv.port, h, 'run', r.runId, { resume: true });
    expect(await first.c.waitClose()).toBe(4001);
    expect(second.ready).toMatchObject({ seat: 1 });
    const t = await ticketFor(app, r.heroes[0] as Hero, 'run', r.runId);
    const odd = await RelayClient.open(srv.port);
    odd.hello(t.body.data.ticket, { wire: 2 });
    expect(await odd.waitClose()).toBe(4015);
    second.c.close();
  });

  it('hello 형식: 첫 프레임이 hello가 아니면 NOT_AUTHENTICATED, 버전이 낮으면 4426', async () => {
    const c = await RelayClient.open(srv.port);
    c.send(1, 0);
    expect(await c.waitText((f) => f.t === 'error' && f.code === 'NOT_AUTHENTICATED')).toBeTruthy();
    c.close();
    const d = await RelayClient.open(srv.port);
    d.hello('x', { client_version: '0.0.1' });
    expect(await d.waitClose()).toBe(4426);
  });
});

describe('라우팅: 좌석 도장, 별 모양, 호스트 전용', () => {
  it('서버가 보낸 좌석을 도장 찍고, 호스트는 지정 좌석 또는 방 전체로 보낸다', async () => {
    const r = await room(3);
    const [host, m1, m2] = r.conns as [RelayClient, RelayClient, RelayClient];
    m1.send(1, 0, 'from-m1');
    m2.send(7, 0, 'from-m2');
    const p1 = await host.waitPacket((p) => p.from === 1);
    expect(p1).toMatchObject({ channel: 1, from: 1 });
    expect(p1.payload.toString()).toBe('from-m1');
    expect(await host.waitPacket((p) => p.from === 2)).toMatchObject({ channel: 7 });
    host.send(5, 2, 'only-m2');
    expect((await m2.waitPacket((p) => p.channel === 5)).from).toBe(0);
    await m1.expectNoPacket((p) => p.channel === 5);
    host.send(4, 0xff, 'snap');
    expect((await m1.waitPacket((p) => p.channel === 4)).payload.toString()).toBe('snap');
    expect((await m2.waitPacket((p) => p.channel === 4)).from).toBe(0);
    closeAll(r);
  });

  it('멤버 -> 멤버, 멤버의 방 전체 전송, 금지 채널(채팅)은 전달되지 않는다', async () => {
    const r = await room(3);
    const [host, m1, m2] = r.conns as [RelayClient, RelayClient, RelayClient];
    m1.send(5, 2, 'to-m2');
    m1.send(5, 0xff, 'to-all');
    m1.send(2, 0, 'chat');
    await m2.expectNoPacket();
    await host.expectNoPacket();
    closeAll(r);
  });

  it('호스트 전용 채널(Snapshot)은 현재 호스트에게서만 받는다', async () => {
    const r = await room(3);
    const [host, m1, m2] = r.conns as [RelayClient, RelayClient, RelayClient];
    m1.send(4, 0, 'fake-snapshot');
    m1.send(4, 2, 'fake-snapshot');
    await host.expectNoPacket((p) => p.channel === 4);
    await m2.expectNoPacket((p) => p.channel === 4);
    // 호스트가 Input(멤버 -> 호스트 채널)을 보내는 것도 위반이다
    host.send(1, 1, 'bad');
    await m1.expectNoPacket((p) => p.channel === 1);
    closeAll(r);
  });

  it('규칙 위반이 분당 한도를 넘으면 4006으로 끊고 relay_abuse 이상 기록을 남긴다', async () => {
    const r = await room(3);
    const m1 = r.conns[1] as RelayClient;
    m1.sendBatch(Array.from({ length: 8 }, () => ({ channel: 2, addr: 0, payload: Buffer.from('x') })));
    expect(await m1.waitClose()).toBe(4006);
    await until(async () => Number((await getPool().query("SELECT count(*) AS n FROM anomaly_log WHERE kind = 'relay_abuse'")).rows[0].n) > 0);
    closeAll(r);
  });
});

describe('크기·속도 한도', () => {
  it('프레임이 RELAY_FRAME_MAX_BYTES(8192)를 넘으면 1009로 끊는다', async () => {
    const r = await room(2);
    const m1 = r.conns[1] as RelayClient;
    m1.ws.send(Buffer.alloc(9000, 4), { binary: true });
    expect(await m1.waitClose()).toBe(1009);
    closeAll(r);
  });

  it('패킷이 RELAY_PACKET_MAX_BYTES(6144)를 넘으면 BAD_FRAME, 반복하면 4005', async () => {
    const r = await room(2);
    const [host, m1] = r.conns as [RelayClient, RelayClient];
    for (let i = 0; i < 3; i++) m1.send(1, 0, Buffer.alloc(7000, 1));
    expect(await m1.waitText((f) => f.t === 'error' && f.code === 'BAD_FRAME')).toBeTruthy();
    expect(await m1.waitClose()).toBe(4005);
    await host.expectNoPacket((p) => p.channel === 1);
    closeAll(r);
  });

  it('멤버가 속도 한도(초당 10, 버스트 20)를 넘으면 초과분을 버리고 4006으로 끊는다', async () => {
    const r = await room(2);
    const [host, m1] = r.conns as [RelayClient, RelayClient];
    m1.sendBatch(Array.from({ length: 60 }, (_, i) => ({ channel: 7, addr: 0, payload: Buffer.from([i]) })));
    expect(await m1.waitClose()).toBe(4006);
    await sleep(300);
    // 방 송신은 최신 하나만 남기므로 개수보다 "한도 밖 패킷이 끝까지 전달되지 않았음"을 본다
    expect(host.packets.every((p) => (p.payload[0] as number) < 21)).toBe(true);
    closeAll(r);
  });
});

describe('끊김과 호스트 인계', () => {
  const hostRow = async (runId: string) =>
    (await getPool().query<{ host: string; epoch: number }>('SELECT c.uuid AS host, r.host_epoch AS epoch FROM party_runs r JOIN characters c ON c.id = r.host_character_id WHERE r.uuid = $1', [runId])).rows[0] as { host: string; epoch: number };

  it('호스트 연결이 grace(300ms) 넘게 없으면 서버가 가장 작은 활성 좌석에게 인계하고, 옛 호스트의 호스트 전용 프레임은 막는다', async () => {
    const r = await room(3);
    const [host, m1, m2] = r.conns as [RelayClient, RelayClient, RelayClient];
    host.terminate();
    expect(await m1.waitText((f) => f.t === 'peer.left')).toMatchObject({ seat: 0, reason: 'closed' });
    const ch = await m1.waitT('host.changed');
    expect(ch).toMatchObject({ seat: 1, epoch: 2 });
    expect(await m2.waitT('host.changed')).toMatchObject({ seat: 1, epoch: 2 });
    expect(await hostRow(r.runId)).toEqual({ host: (r.heroes[1] as Hero).id, epoch: 2 });
    const st = await getPool().query("SELECT state FROM party_run_members m JOIN characters c ON c.id = m.character_id WHERE c.uuid = $1", [(r.heroes[0] as Hero).id]);
    expect(st.rows[0].state).toBe('disconnected');
    // 새 호스트는 방 전체로 Snapshot을 보낼 수 있다
    m1.send(4, 0xff, 'new-host-snap');
    expect((await m2.waitPacket((p) => p.channel === 4)).from).toBe(1);
    // 옛 호스트가 돌아와도(resume) 호스트가 아니고, 그 Snapshot은 버려진다
    const back = await joinRoom(app, srv.port, r.heroes[0] as Hero, 'run', r.runId, { resume: true });
    expect(back.ready).toMatchObject({ seat: 0, host_seat: 1, host_epoch: 2 });
    back.c.send(4, 0xff, 'stale-snap');
    await m2.expectNoPacket((p) => p.channel === 4 && p.payload.toString() === 'stale-snap');
    back.c.send(5, 1, 'event-to-new-host');
    expect((await m1.waitPacket((p) => p.channel === 5)).from).toBe(0);
    await until(async () => (await getPool().query("SELECT state FROM party_run_members m JOIN characters c ON c.id = m.character_id WHERE c.uuid = $1", [(r.heroes[0] as Hero).id])).rows[0].state === 'playing');
    back.c.close();
    closeAll(r);
  });

  it('grace 안에 돌아오면 호스트를 바꾸지 않는다(Wi-Fi 순간 끊김)', async () => {
    const r = await room(2);
    const [host, m1] = r.conns as [RelayClient, RelayClient];
    host.terminate();
    await m1.waitT('peer.left');
    const back = await joinRoom(app, srv.port, r.heroes[0] as Hero, 'run', r.runId, { resume: true });
    await m1.expectNoText((f) => f.t === 'host.changed', 700);
    expect(await hostRow(r.runId)).toEqual({ host: (r.heroes[0] as Hero).id, epoch: 1 });
    back.c.close();
    closeAll(r);
  });

  it('멤버가 끊기면 즉시 disconnected, 다시 붙으면 playing, 후보가 없는 호스트 상실은 인계하지 않는다', async () => {
    const r = await room(2);
    const [host, m1] = r.conns as [RelayClient, RelayClient];
    m1.terminate();
    const memberState = async (i: number) => (await getPool().query("SELECT state FROM party_run_members m JOIN characters c ON c.id = m.character_id WHERE c.uuid = $1", [(r.heroes[i] as Hero).id])).rows[0].state as string;
    await until(async () => (await memberState(1)) === 'disconnected');
    expect(await host.waitText((f) => f.t === 'peer.left')).toMatchObject({ seat: 1 });
    const back = await joinRoom(app, srv.port, r.heroes[1] as Hero, 'run', r.runId, { resume: true });
    await until(async () => (await memberState(1)) === 'playing');
    // 호스트만 남기고 멤버를 영구 이탈시키면 후보가 없어 인계는 일어나지 않는다
    back.c.close();
    await until(async () => (await memberState(1)) === 'disconnected');
    host.terminate();
    await sleep(700);
    expect(await hostRow(r.runId)).toEqual({ host: (r.heroes[0] as Hero).id, epoch: 1 });
  });
});

describe('판 변화가 중계 방에 반영된다', () => {
  it('R3 자동 입장: 중계에 붙는 것이 입장 확인이고 마지막 사람이 붙으면 판이 시작된다', async () => {
    const heroes = [await newHero(app), await newHero(app)];
    await formParty(app, heroes[0] as Hero, [heroes[1] as Hero]);
    const st = await post(app, heroes[0] as Hero, '/party/start', { ai_count: 0 });
    expect(st.status).toBe(201);
    expect(st.body.data.run.transport.current).toBe('relay');
    const runId = st.body.data.run.id as string;
    const a = await joinRoom(app, srv.port, heroes[0] as Hero, 'run', runId);
    const b = await joinRoom(app, srv.port, heroes[1] as Hero, 'run', runId);
    await until(async () => (await getPool().query('SELECT state FROM party_runs WHERE uuid = $1', [runId])).rows[0].state === 'playing');
    a.c.close();
    b.c.close();
  });

  it('멤버가 판을 떠나면 4011로 내보내고, 판이 끝나면 방을 닫는다(4012). 끝난 방의 티켓은 409 ROOM_CLOSED', async () => {
    const r = await room(2);
    const [host, m1] = r.conns as [RelayClient, RelayClient];
    const left = await post(app, r.heroes[1] as Hero, `/party-runs/${r.runId}/leave`, {});
    expect(left.status).toBe(200);
    expect(await m1.waitClose()).toBe(4011);
    expect(await host.waitText((f) => f.t === 'peer.left' && f.reason === 'removed')).toMatchObject({ seat: 1 });
    await getPool().query("UPDATE party_runs SET state = 'ended', ended_at = now() WHERE uuid = $1", [r.runId]);
    relayHub().resync('run', r.runId);
    expect(await host.waitT('room.closed')).toBeTruthy();
    expect(await host.waitClose()).toBe(4012);
    expect((await ticketFor(app, r.heroes[0] as Hero, 'run', r.runId)).body.errors?.code).toBe('ROOM_CLOSED');
    await until(async () => Number((await getPool().query('SELECT count(*) AS n FROM relay_room_stats WHERE room_ref = $1', [r.runId])).rows[0].n) === 1);
    const stat = (await getPool().query('SELECT peak_peers, frames_out FROM relay_room_stats WHERE room_ref = $1', [r.runId])).rows[0];
    expect(stat.peak_peers).toBe(2);
  });

  it('지표: 종료 코드와 티켓 거절 사유를 센다', () => {
    expect(relayMetrics.closesSince(3_600_000)['4004']).toBeGreaterThan(0);
    expect(relayMetrics.rejectsSince(3_600_000).replayed).toBeGreaterThan(0);
  });
});

// 별도 서버를 띄우므로(중계 허브는 프로세스 하나에 하나) 마지막에 둔다
describe('유휴와 수용 한도', () => {
  it('유휴(PING 없음): 한도 시간 뒤 4013, 수용 한도: 연결이 가득 차면 새 티켓은 503 RELAY_UNAVAILABLE', async () => {
    const r = await room(3, false);
    const app2 = buildApp({ ...OVERRIDES, RELAY_IDLE_TIMEOUT_MS: '700', RELAY_MAX_CONNECTIONS: '3', RELAY_ADMISSION_RATIO: '0.6' });
    // /ws 리스너는 프로세스에 하나라 중계만 따로 띄운다(중계 허브는 마지막에 붙은 것이 쓰인다)
    const http2 = http.createServer(app2);
    const relay2 = await attachRelay(http2);
    await new Promise<void>((resolve) => http2.listen(0, '127.0.0.1', resolve));
    const srv2 = { port: (http2.address() as AddressInfo).port };
    try {
      const a = await joinRoom(app2, srv2.port, r.heroes[0] as Hero, 'run', r.runId);
      const b = await joinRoom(app2, srv2.port, r.heroes[1] as Hero, 'run', r.runId);
      // 연결 2개 >= 3 x 0.6 이므로 새 티켓을 받지 않는다
      const full = await request(app2).post(`/characters/${(r.heroes[2] as Hero).id}/rooms/run/${r.runId}/relay-ticket`).set(auth((r.heroes[2] as Hero).s)).send({});
      expect(full.status).toBe(503);
      expect(full.body.errors.code).toBe('RELAY_UNAVAILABLE');
      expect(full.headers['retry-after']).toBeTruthy();
      // a는 PING을 계속 보내 살아 있고, b는 조용해서 4013
      const keep = setInterval(() => a.c.ping(1), 200);
      expect(await b.c.waitClose(4000)).toBe(4013);
      clearInterval(keep);
      a.c.close();
    } finally {
      await relay2.close();
      await new Promise<void>((resolve) => {
        http2.close(() => resolve());
        http2.closeAllConnections();
      });
      app = buildApp(OVERRIDES);
    }
  });
});
