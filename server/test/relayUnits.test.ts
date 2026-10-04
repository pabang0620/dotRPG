import { randomUUID } from 'node:crypto';
import { ADDR_ALL, CH, decodeFrame, encodeBatch, encodePing, encodePong, routePacket, splitBatches, type Packet } from '../src/domains/relay/relayFrames';
import { TokenBucket } from '../src/domains/relay/relaySession';
import { MemoryTicketStore, signTicket, verifyTicket } from '../src/domains/relay/relayTicket';
import { setMaintenance } from '../src/ops/maintenanceState';
import { buildApp, resetDb, shutdown } from './helpers';
import { formParty, newHero, startAndBegin, type Hero } from './partyHelpers';
import { RelayClient, ticketFor } from './relayHelpers';
import { startServer, type TestServer } from './wsHelpers';

const app = buildApp({ RELAY_HOST_GRACE_MS: '300' });
let srv: TestServer;
beforeAll(async () => {
  await resetDb();
  srv = await startServer(app);
});
afterAll(async () => {
  setMaintenance(null);
  await srv.stop();
  await shutdown();
});

const pkt = (channel: number, addr: number, ...bytes: number[]): Packet => ({ channel, addr, payload: Buffer.from(bytes) });

describe('프레임 코덱(클라이언트와 같은 바이트)', () => {
  it('BATCH 고정 벡터: [0x04][count][channel][addr][len u16 LE][payload]', () => {
    const buf = encodeBatch([pkt(5, 1, 0xaa, 0xbb, 0xcc), pkt(4, 0xff)]);
    expect(buf.toString('hex')).toBe('0402' + '050103 00aabbcc'.replace(' ', '') + '04ff0000');
    const d = decodeFrame(buf, 6144);
    expect(d.kind).toBe('batch');
    if (d.kind === 'batch') expect(d.packets.map((p) => [p.channel, p.addr, p.payload.toString('hex')])).toEqual([[5, 1, 'aabbcc'], [4, 255, '']]);
    expect(encodePing(0x01020304).toString('hex')).toBe('0204030201');
    expect(encodePong(7).toString('hex')).toBe('0307000000');
    expect(decodeFrame(encodePing(0x01020304), 10)).toEqual({ kind: 'ping', nonce: 0x01020304 });
  });

  it('잘못된 프레임: 빈 프레임, 알 수 없는 종류, 개수 0, 잘린 길이, 남는 바이트, 패킷 한도 초과', () => {
    const err = (b: Buffer, max = 6144) => (decodeFrame(b, max) as { reason: string }).reason;
    expect(err(Buffer.alloc(0))).toBe('empty');
    expect(err(Buffer.from([0x09]))).toBe('type');
    expect(err(Buffer.from([0x04, 0x00]))).toBe('count');
    expect(err(Buffer.from([0x04, 0x01, 0x05, 0x00, 0x05, 0x00, 0x01]))).toBe('length');
    expect(err(Buffer.concat([encodeBatch([pkt(5, 1, 1)]), Buffer.from([0])]))).toBe('length');
    expect(err(Buffer.from([0x02, 0x01]))).toBe('length');
    expect(err(encodeBatch([pkt(5, 1, 1, 2, 3)]), 2)).toBe('packet_too_big');
  });

  it('splitBatches: 프레임 한도(8192)와 255개를 넘으면 나눈다', () => {
    const big = Array.from({ length: 5 }, () => ({ channel: 5, addr: 0, payload: Buffer.alloc(3000, 1) }));
    const frames = splitBatches(big, 8192);
    expect(frames.length).toBe(3);
    expect(frames.every((f) => f.length <= 8192)).toBe(true);
    expect(splitBatches(Array.from({ length: 300 }, () => pkt(5, 0, 1)), 8192).length).toBe(2);
  });
});

describe('라우팅 규칙(헤더만 본다)', () => {
  const ctx: { hostSeat: number | null; staleHostSeat: number | null; connected: number[] } = { hostSeat: 0, staleHostSeat: null, connected: [0, 1, 2] };
  const r = (from: number, p: Packet, c = ctx) => routePacket(from, p, c);

  it('멤버는 호스트 좌석으로만: 멤버 -> 호스트 허용, 멤버 -> 멤버·전체·범위 밖 위반', () => {
    expect(r(1, pkt(CH.INPUT, 0))).toEqual({ to: [0] });
    expect(r(2, pkt(CH.MEMBER_STATE, 0))).toEqual({ to: [0] });
    expect(r(1, pkt(CH.EVENT, 0))).toEqual({ to: [0] });
    expect(r(1, pkt(CH.EVENT, 2))).toEqual({ violation: 'member_to_member' });
    expect(r(1, pkt(CH.EVENT, ADDR_ALL))).toEqual({ violation: 'broadcast' });
    expect(r(1, pkt(CH.EVENT, 9))).toEqual({ violation: 'addr' });
  });

  it('호스트 -> 지정 좌석·방 전체(자기 제외), 연결 없는 좌석은 버림, Input/MemberState 는 호스트가 보낼 수 없다', () => {
    expect(r(0, pkt(CH.SNAPSHOT, ADDR_ALL))).toEqual({ to: [1, 2] });
    expect(r(0, pkt(CH.CONTROL, 2))).toEqual({ to: [2] });
    expect(r(0, pkt(CH.EVENT, 3))).toEqual({ drop: 'target_absent' });
    expect(r(0, pkt(CH.INPUT, 1))).toEqual({ violation: 'host_only' });
    expect(r(0, pkt(CH.EVENT, 0))).toEqual({ violation: 'addr' });
  });

  it('채팅(2)·유령(3)·알 수 없는 채널은 금지, Snapshot 은 호스트만', () => {
    for (const ch of [0, CH.CHAT, CH.PRESENCE, 8, 255]) expect(r(0, pkt(ch, 1))).toEqual({ violation: 'channel' });
    expect(r(1, pkt(CH.SNAPSHOT, 0))).toEqual({ violation: 'host_only' });
  });

  it('인계 직후 옛 호스트의 호스트 전용 프레임과 옛 호스트 주소는 위반으로 세지 않고 버린다', () => {
    const c = { hostSeat: 1, staleHostSeat: 0, connected: [0, 1, 2] };
    expect(r(0, pkt(CH.SNAPSHOT, ADDR_ALL), c)).toEqual({ drop: 'stale_host' });
    expect(r(2, pkt(CH.INPUT, 0), c)).toEqual({ drop: 'stale_host' });
    expect(r(0, pkt(CH.EVENT, 1), c)).toEqual({ to: [1] });
    expect(r(2, pkt(CH.INPUT, 1), c)).toEqual({ to: [1] });
    expect(r(1, pkt(CH.INPUT, 0), { hostSeat: null, staleHostSeat: null, connected: [1] })).toEqual({ drop: 'no_host' });
  });
});

describe('토큰 버킷과 티켓 저장소', () => {
  it('TokenBucket: 버스트까지 쓰고, 시간이 지나면 채운다', () => {
    const b = new TokenBucket(10, 20, 0);
    expect(b.take(20, 0)).toBe(true);
    expect(b.take(1, 0)).toBe(false);
    expect(b.take(5, 500)).toBe(true);
    expect(b.take(1, 500)).toBe(false);
    expect(b.force(100, 500)).toBeLessThan(0);
  });

  it('티켓: 서명·검증, 같은 jti는 두 번째가 거절, 저장소는 2분 뒤 잊는다', () => {
    const t = signTicket({ accountUuid: randomUUID(), characterUuid: randomUUID(), kind: 'run', roomUuid: randomUUID(), seat: 2, epoch: 3 });
    const v = verifyTicket(t);
    expect(v.ok && v.claims).toMatchObject({ seat: 2, ep: 3, k: 'run' });
    expect(verifyTicket(`${t}x`)).toEqual({ ok: false, reason: 'invalid' });
    const s = new MemoryTicketStore();
    expect(s.consume('j1', 0)).toBe(true);
    expect(s.consume('j1', 1000)).toBe(false);
    expect(s.consume('j1', 130_000)).toBe(true);
  });
});

describe('점검', () => {
  it('점검이 시작되면 새 티켓은 503 MAINTENANCE, 접속 중인 중계 연결은 bye(1001, MAINTENANCE)로 닫힌다', async () => {
    const heroes: Hero[] = [await newHero(app), await newHero(app)];
    await formParty(app, heroes[0] as Hero, [heroes[1] as Hero]);
    const { runId } = await startAndBegin(app, heroes[0] as Hero, [heroes[1] as Hero]);
    const t = await ticketFor(app, heroes[0] as Hero, 'run', runId);
    const c = await RelayClient.open(srv.port);
    c.hello(t.body.data.ticket);
    await c.waitT('ready');
    const now = Date.now();
    setMaintenance({ uuid: randomUUID(), notice: '', blockLoginAt: new Date(now - 60_000), startsAt: new Date(now - 1000), endsAt: new Date(now + 600_000) });
    const blocked = await ticketFor(app, heroes[1] as Hero, 'run', runId);
    expect(blocked.status).toBe(503);
    expect(blocked.body.errors?.code).toBe('MAINTENANCE');
    const { relayHub } = await import('../src/domains/relay/relayHub');
    expect(relayHub().closeAll(1001, 'MAINTENANCE', true, 60_000)).toBe(1);
    const bye = await c.waitT('bye');
    expect(bye).toMatchObject({ code: 1001, reason: 'MAINTENANCE', reconnect: true });
    expect(await c.waitClose()).toBe(1001);
    // 새 연결은 업그레이드 단계에서 503 으로 거절된다
    await expect(RelayClient.open(srv.port)).rejects.toBeTruthy();
  });
});
