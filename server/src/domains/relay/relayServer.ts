// 전투 중계 서버(/relay): 같은 HTTP 서버의 upgrade 이벤트에 붙는 별도 WebSocket 경로. 텍스트 프레임 = 제어(JSON), 바이너리 = 데이터(BATCH/PING/PONG).
// 내용은 보지 않는다(라우팅만). 방의 멤버십 권한은 DB이고 중계는 메모리 상태만 가진다(phase8_api.md 3~4절).
import type { IncomingMessage, Server } from 'node:http';
import type { Duplex } from 'node:stream';
import { randomInt } from 'node:crypto';
import { WebSocketServer, type RawData, type WebSocket } from 'ws';
import { z } from 'zod';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getRateLimitStore } from '../../middleware/rateLimiter';
import { isShuttingDown } from '../../ops/lifecycle';
import { maintPhase, maintRetryAfterMs } from '../../ops/maintenanceState';
import { logger } from '../../utils/logger';
import { compareSemver } from '../../utils/semver';
import { clientIp } from '../chat/wsServer';
import { decodeFrame, encodePong } from './relayFrames';
import { relayMetrics } from './relayMetrics';
import { accountByUuid, characterOf, type RoomSnapshot } from './relayRepository';
import { getDirectory } from './roomDirectory';
import { CLOSE_CODE, RelayRoom } from './relayRoom';
import { RelaySession } from './relaySession';
import { setRelayHub, setRelayState, type RelayHub } from './relayHub';
import { getTicketStore, verifyTicket, type RoomKind } from './relayTicket';

export interface RelayHandle {
  close(): Promise<void>;
  roomCount(): number;
  connCount(): number;
}

const helloFrame = z.strictObject({
  t: z.literal('hello'),
  v: z.number().int(),
  ticket: z.string().min(1).max(2048),
  client_version: z.string().regex(/^\d+\.\d+\.\d+$/),
  wire: z.number().int().min(0),
  resume: z.boolean().optional(),
});
const leaveFrame = z.strictObject({ t: z.literal('leave') });
const controlFrame = z.union([helloFrame, leaveFrame]);

const reject = (socket: Duplex, status: string): void => {
  socket.write(`HTTP/1.1 ${status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
  socket.destroy();
};

const rawSend = (ws: WebSocket, frame: Record<string, unknown>): void => {
  if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(frame));
};

const rawBye = (ws: WebSocket, code: number, reason: string, reconnect: boolean, retryAfterMs?: number): void => {
  const f: Record<string, unknown> = { t: 'bye', code, reason, reconnect };
  if (retryAfterMs !== undefined) f.retry_after_ms = retryAfterMs;
  rawSend(ws, f);
  ws.close(code, reason.slice(0, 100));
  relayMetrics.close(code);
};

const ticketHead = (t: string): string => t.slice(0, 8);

export async function attachRelay(server: Server): Promise<RelayHandle> {
  const cfg = getConfig().relay;
  if (!cfg.enabled) {
    return { close: async () => undefined, roomCount: () => 0, connCount: () => 0 };
  }
  const wss = new WebSocketServer({ noServer: true, maxPayload: cfg.frameMaxBytes, perMessageDeflate: false });
  const rooms = new Map<string, RelayRoom>();
  const unauthByIp = new Map<string, number>();
  const authByIp = new Map<string, number>();
  let closing = false;
  let total = 0;
  const dir = () => getDirectory();

  const bump = (m: Map<string, number>, k: string, d: number): void => {
    const n = (m.get(k) ?? 0) + d;
    if (n <= 0) m.delete(k);
    else m.set(k, n);
  };

  server.on('upgrade', (req: IncomingMessage, socket: Duplex, head: Buffer) => {
    const path = new URL(req.url ?? '/', 'http://localhost').pathname;
    if (path !== '/relay') return;
    if (closing || isShuttingDown() || maintPhase() === 'active') return reject(socket, '503 Service Unavailable');
    const ip = clientIp(req);
    const store = getRateLimitStore();
    const key = `relay-handshake:${ip}`;
    if (store.count(key, 60_000).count >= cfg.handshakePerMinIp) return reject(socket, '429 Too Many Requests');
    store.add(key, 60_000);
    if (total >= cfg.maxConnections) return reject(socket, '503 Service Unavailable');
    if ((unauthByIp.get(ip) ?? 0) >= cfg.unauthPerIp) return reject(socket, '429 Too Many Requests');
    req.socket.setNoDelay(true);
    wss.handleUpgrade(req, socket, head, (ws) => onConnection(ws, ip));
  });

  // ---------- 방 상태를 DB 스냅샷에 맞춘다(서비스가 바뀌었다고 알릴 때, 그리고 hello 때) ----------

  async function destroyRoom(room: RelayRoom): Promise<void> {
    rooms.delete(room.key);
    await room.finish();
  }

  function applySnapshot(room: RelayRoom, snap: RoomSnapshot | null): void {
    if (room.closed) return;
    if (!snap || snap.closed) {
      room.broadcastText({ t: 'room.closed', reason: 'closed' });
      for (const s of room.conns.values()) {
        s.cause = 'removed';
        s.bye(CLOSE_CODE.ROOM_CLOSED, 'ROOM_CLOSED', false);
      }
      void destroyRoom(room);
      return;
    }
    if (snap.transport !== 'relay' || snap.transportEpoch !== room.transportEpoch) {
      room.broadcastText({ t: 'transport.changed', current: snap.transport, epoch: snap.transportEpoch });
      for (const s of room.conns.values()) {
        s.cause = 'removed';
        s.bye(CLOSE_CODE.TRANSPORT_CHANGED, 'TRANSPORT_CHANGED', false);
      }
      void destroyRoom(room);
      return;
    }
    for (const s of [...room.conns.values()]) {
      const m = snap.members.find((x) => x.characterId === s.characterId);
      if (!m || !m.active) {
        s.cause = 'removed';
        s.bye(CLOSE_CODE.MEMBER_REMOVED, 'MEMBER_REMOVED', false);
      }
    }
    if (room.setHost(snap.hostSeat, snap.hostEpoch)) logger.info({ room: room.key, seat: snap.hostSeat, epoch: snap.hostEpoch }, 'field.host_changed');
  }

  const hub: RelayHub = {
    hasRooms: (kind) => [...rooms.values()].some((r) => r.kind === kind),
    resync: (kind, uuid) => {
      const room = rooms.get(`${kind}:${uuid}`);
      if (!room) return;
      dir()
        .snapshot(kind, uuid)
        .then((snap) => applySnapshot(room, snap))
        .catch((err: unknown) => logger.error({ err }, 'relay resync failed'));
    },
    hostPresence: (kind, uuid) => rooms.get(`${kind}:${uuid}`)?.hostPresence() ?? null,
    kickAccount: (accountId, code, reason) => {
      let n = 0;
      for (const room of rooms.values()) {
        for (const s of room.conns.values()) {
          if (s.accountId !== accountId) continue;
          s.cause = 'removed';
          s.bye(code, reason, false);
          n++;
        }
      }
      return n;
    },
    counts: () => ({ rooms: rooms.size, conns: total }),
    admission: () => {
      if (closing) return { ok: false, reason: 'disabled' };
      if (total >= cfg.maxConnections * cfg.admissionRatio) return { ok: false, reason: 'connections' };
      if (rooms.size >= cfg.maxRooms * cfg.admissionRatio) return { ok: false, reason: 'rooms' };
      return { ok: true };
    },
    closeAll: (code, reason, reconnect, retryAfterMs) => {
      let n = 0;
      for (const room of rooms.values()) {
        for (const s of room.conns.values()) {
          s.cause = 'closed';
          s.bye(code, reason, reconnect, retryAfterMs);
          n++;
        }
      }
      return n;
    },
  };
  setRelayHub(hub);
  setRelayState('attached');

  // ---------- 연결 ----------

  function onConnection(ws: WebSocket, ip: string): void {
    total++;
    bump(unauthByIp, ip, 1);
    let unauthCounted = true;
    const uncount = (): void => {
      if (!unauthCounted) return;
      unauthCounted = false;
      bump(unauthByIp, ip, -1);
    };
    let session: RelaySession | null = null;
    let room: RelayRoom | null = null;
    let helloing = false;
    let bad: number[] = [];
    let ended = false;

    const helloTimer = setTimeout(() => {
      if (!session && !ended) rawBye(ws, CLOSE_CODE.HELLO_TIMEOUT, 'HELLO_TIMEOUT', true);
    }, cfg.helloTimeoutMs);

    const preBad = (): boolean => {
      const now = Date.now();
      bad = bad.filter((t) => now - t < 10_000);
      bad.push(now);
      return bad.length >= 3;
    };
    const err = (code: string, message: string, ref?: string): void => {
      const f: Record<string, unknown> = { t: 'error', code, message };
      if (ref) f.ref = ref;
      if (session) session.sendText(f);
      else rawSend(ws, f);
    };
    const fail = (code: number, reason: string, reconnect: boolean, retryAfterMs?: number): void => {
      clearTimeout(helloTimer);
      rawBye(ws, code, reason, reconnect, retryAfterMs);
    };

    async function doHello(f: z.infer<typeof helloFrame>): Promise<void> {
      if (f.v !== 1) return fail(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false);
      if (compareSemver(f.client_version, getConfig().minClientVersion) < 0) {
        err('CLIENT_OUTDATED', '게임을 업데이트해 주세요.');
        return fail(CLOSE_CODE.CLIENT_OUTDATED, 'CLIENT_OUTDATED', false);
      }
      const v = verifyTicket(f.ticket);
      if (!v.ok) {
        relayMetrics.ticketReject(v.reason);
        logger.info({ reason: v.reason, ticket: ticketHead(f.ticket) }, 'relay.ticket_rejected');
        return fail(CLOSE_CODE.TOKEN_INVALID, 'TICKET_INVALID', false);
      }
      const c = v.claims;
      // 종료·점검 중에는 티켓을 소비하지 않는다(재접속 때 같은 티켓이 아직 유효하다)
      if (closing || isShuttingDown() || maintPhase() === 'active') {
        return fail(CLOSE_CODE.SERVER_BUSY, 'SERVER_BUSY', true, maintPhase() === 'active' ? maintRetryAfterMs() : randomInt(3000, 8001));
      }
      if (!getTicketStore().consume(c.jti, Date.now())) {
        relayMetrics.ticketReject('replayed');
        logger.info({ reason: 'replayed', jti: c.jti.slice(0, 8) }, 'relay.ticket_rejected');
        return fail(CLOSE_CODE.TOKEN_INVALID, 'TICKET_REPLAYED', false);
      }
      const acct = await accountByUuid(getPool(), c.sub);
      if (!acct) return fail(CLOSE_CODE.TOKEN_INVALID, 'TICKET_INVALID', false);
      if (acct.banned_until && acct.banned_until.getTime() > Date.now()) return fail(CLOSE_CODE.BANNED, 'BANNED', false);
      const ch = await characterOf(getPool(), acct.id, c.cid);
      if (!ch) return fail(CLOSE_CODE.TOKEN_INVALID, 'TICKET_INVALID', false);
      const snap = await dir().snapshot(c.k, c.rid);
      if (ws.readyState !== ws.OPEN) return;
      if (!snap || snap.closed) {
        relayMetrics.ticketReject('room_closed');
        return fail(CLOSE_CODE.ROOM_CLOSED, 'ROOM_CLOSED', false);
      }
      const me = snap.members.find((m) => m.characterId === ch.id);
      if (!me || !me.active || me.seat !== c.seat) return fail(CLOSE_CODE.MEMBER_REMOVED, 'MEMBER_REMOVED', false);
      if (snap.transport !== 'relay' || snap.transportEpoch !== c.ep) return fail(CLOSE_CODE.TRANSPORT_CHANGED, 'TRANSPORT_CHANGED', false);
      const key = `${c.k}:${c.rid}`;
      let r = rooms.get(key);
      // 같은 방의 다른 연결과 PartyNet 와이어 버전이 같아야 한다(다른 빌드가 바이트를 잘못 해석하는 사고 방지)
      if (r && r.conns.size > 0 && r.wire !== null && r.wire !== f.wire) {
        relayMetrics.ticketReject('wire');
        return fail(CLOSE_CODE.WIRE_MISMATCH, 'WIRE_MISMATCH', false);
      }
      const old = r?.conns.get(c.seat);
      if (!old) {
        if (!r && rooms.size >= cfg.maxRooms) return fail(CLOSE_CODE.SERVER_BUSY, 'SERVER_BUSY', true, randomInt(3000, 8001));
        if (total > cfg.maxConnections) return fail(CLOSE_CODE.SERVER_BUSY, 'SERVER_BUSY', true, randomInt(3000, 8001));
      }
      if ((authByIp.get(ip) ?? 0) >= cfg.connPerIp && !old) return fail(CLOSE_CODE.SERVER_BUSY, 'SERVER_BUSY', true, randomInt(3000, 8001));
      if (!r) {
        r = new RelayRoom(c.k, c.rid, snap);
        rooms.set(key, r);
        logger.info({ room: key }, 'relay.room_opened');
      }
      r.setHost(snap.hostSeat, snap.hostEpoch);
      // 같은 좌석의 옛 연결은 새 연결(티켓이 유효하다)이 대체한다
      if (old) {
        old.cause = 'replaced';
        r.conns.delete(c.seat);
        r.absorb(old);
        r.broadcastText({ t: 'peer.left', seat: c.seat, reason: 'replaced' });
        old.bye(CLOSE_CODE.REPLACED, 'REPLACED', false);
      }
      if (f.resume === true || old) r.noteReconnect();
      const s = new RelaySession(ws, ip, acct.id, c.sub, ch.id, c.cid, c.k, c.rid, c.seat, f.wire, snap.hostSeat === c.seat);
      r.wire = f.wire;
      clearTimeout(helloTimer);
      uncount();
      bump(authByIp, ip, 1);
      session = s;
      room = r;
      const peers = r.connectedSeats();
      r.add(s);
      s.sendText({
        t: 'ready',
        v: 1,
        server_time: new Date().toISOString(),
        room: { kind: c.k, id: c.rid, transport_epoch: snap.transportEpoch },
        seat: c.seat,
        host_seat: snap.hostSeat,
        host_epoch: snap.hostEpoch,
        peers,
        limits: {
          frame_max_bytes: cfg.frameMaxBytes,
          packet_max_bytes: cfg.packetMaxBytes,
          host_pkt_per_sec: cfg.hostPktPerSec,
          member_pkt_per_sec: cfg.memberPktPerSec,
          host_bytes_per_sec: cfg.hostBytesPerSec,
          member_bytes_per_sec: cfg.memberBytesPerSec,
        },
        ping_every_ms: cfg.pingEveryMs,
        idle_timeout_ms: cfg.idleTimeoutMs,
        flush_ms: cfg.flushMs,
      });
      r.broadcastText({ t: 'peer.joined', seat: c.seat }, c.seat);
      // R3 자동 입장(판) / 연결 확인(필드). 실패해도 연결은 유지한다(하트비트가 따라잡는다)
      dir()
        .peerConnected(c.k, c.rid, { accountId: acct.id, characterId: ch.id, characterUuid: c.cid, seat: c.seat })
        .catch((e: unknown) => logger.error({ err: e }, 'relay peerConnected failed'));
    }

    ws.on('message', (data: RawData, isBinary: boolean) => {
      const now = Date.now();
      const len = Array.isArray(data) ? data.reduce((a, b) => a + b.length, 0) : data instanceof ArrayBuffer ? data.byteLength : data.length;
      if (session) {
        session.lastRecvAt = now;
        session.bytesIn += len;
      }
      relayMetrics.bytesIn.add(len);
      relayMetrics.framesIn.add(1);
      if (isBinary) {
        if (!session || !room) {
          err('NOT_AUTHENTICATED', '먼저 hello로 인증해야 합니다.');
          if (preBad()) fail(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false);
          return;
        }
        const buf = Buffer.isBuffer(data) ? data : Buffer.concat(data as Buffer[]);
        const d = decodeFrame(buf, cfg.packetMaxBytes);
        if (d.kind === 'ping') {
          // PING도 속도 한도와 역압을 받는다(PING 폭주로 서버 송신을 늘리는 경로를 닫는다)
          if (!session.allow(1)) room.strike(session, 'rate');
          else if (session.ws.bufferedAmount <= cfg.backpressureBytes) session.sendBinary(encodePong(d.nonce));
        } else if (d.kind === 'batch') {
          room.route(session, d.packets);
        } else if (d.kind === 'pong') {
          // 클라이언트가 서버 PING에 답한 것(서버는 PING을 보내지 않으므로 무시)
        } else if (session.badFrame()) {
          session.bye(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false);
        } else {
          err('BAD_FRAME', '데이터 프레임 형식이 올바르지 않습니다.');
        }
        return;
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(data.toString());
      } catch {
        err('BAD_FRAME', 'JSON을 읽을 수 없습니다.');
        if (session ? session.badFrame() : preBad()) (session ? session.bye(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false) : fail(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false));
        return;
      }
      const r = controlFrame.safeParse(parsed);
      if (!r.success) {
        const t = (parsed as { t?: unknown } | null)?.t;
        err('BAD_FRAME', '프레임 형식이 올바르지 않습니다.', typeof t === 'string' ? t : undefined);
        if (session ? session.badFrame() : preBad()) (session ? session.bye(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false) : fail(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false));
        return;
      }
      const f = r.data;
      if (f.t === 'leave') {
        if (!session && preBad()) fail(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false);
        if (session) {
          session.closed = true;
          session.cause = 'closed';
          ws.close(1000, 'LEAVE');
        }
        return;
      }
      if (session || helloing) {
        err('BAD_FRAME', '이미 인증되었습니다.', 'hello');
        if (session ? session.badFrame() : preBad()) (session ? session.bye(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false) : fail(CLOSE_CODE.BAD_PROTOCOL, 'BAD_PROTOCOL', false));
        return;
      }
      helloing = true;
      doHello(f)
        .catch((e: unknown) => {
          logger.error({ err: e }, 'relay hello failed');
          fail(CLOSE_CODE.SERVER_BUSY, 'SERVER_BUSY', true, randomInt(3000, 8001));
        })
        .finally(() => {
          helloing = false;
        });
    });

    ws.on('pong', () => {
      if (session && room && session.pingSentAt > 0) {
        room.noteRtt(Date.now() - session.pingSentAt);
        session.pingSentAt = 0;
      }
    });
    ws.on('error', () => undefined);
    ws.on('close', (code) => {
      ended = true;
      clearTimeout(helloTimer);
      total--;
      uncount();
      const s = session;
      const r = room;
      if (!s || !r) return;
      bump(authByIp, s.ip, -1);
      s.closed = true;
      const finalCode = s.closeCode === 1006 ? code : s.closeCode;
      relayMetrics.close(finalCode);
      r.noteClose(finalCode);
      if (r.conns.get(s.seat) !== s) return; // 새 연결이 이미 대체했다
      r.conns.delete(s.seat);
      r.absorb(s);
      r.broadcastText({ t: 'peer.left', seat: s.seat, reason: s.cause });
      logger.info({ room: r.key, seat: s.seat, code, cause: s.cause }, 'relay.conn_closed');
      if (r.conns.size === 0) r.emptyAt = Date.now();
      if (s.cause === 'removed' || s.cause === 'replaced') return;
      const m = { accountId: s.accountId, characterId: s.characterId, characterUuid: s.characterUuid, seat: s.seat };
      dir()
        .peerLost(r.kind, r.uuid, m)
        .catch((e: unknown) => logger.error({ err: e }, 'relay peerLost failed'));
      if (r.hostSeat === s.seat && !r.closed) {
        r.hostLostAt = Date.now();
        if (r.hostGraceTimer) clearTimeout(r.hostGraceTimer);
        r.hostGraceTimer = setTimeout(() => {
          r.hostGraceTimer = null;
          if (r.closed || r.hostSeat !== s.seat || r.conns.has(s.seat)) return;
          dir()
            .hostLost(r.kind, r.uuid, s.seat, r.hostEpoch, r.connectedSeats())
            .then((res) => {
              if (res && !r.closed) r.setHost(res.hostSeat, res.hostEpoch);
            })
            .catch((e: unknown) => logger.error({ err: e }, 'relay hostLost failed'));
        }, cfg.hostGraceMs);
        r.hostGraceTimer.unref();
      }
    });
  }

  // ---------- 틱: 플러시(RELAY_FLUSH_MS), 유휴·핑·빈 방 정리(1초) ----------

  let lastFlush = Date.now();
  const flushTimer = setInterval(() => {
    const now = Date.now();
    relayMetrics.recordFlushLag(Math.max(0, now - lastFlush - cfg.flushMs));
    lastFlush = now;
    for (const room of rooms.values()) room.flush(now);
  }, cfg.flushMs);
  flushTimer.unref();

  const houseTimer = setInterval(() => {
    const now = Date.now();
    for (const room of [...rooms.values()]) {
      for (const s of [...room.conns.values()]) {
        if (s.closed) continue;
        if (now - s.lastRecvAt > cfg.idleTimeoutMs) {
          s.cause = 'timeout';
          s.bye(CLOSE_CODE.PEER_TIMEOUT, 'PEER_TIMEOUT', true);
          continue;
        }
        if (now - s.lastPingAt >= 10_000 && s.ws.readyState === s.ws.OPEN) {
          s.lastPingAt = now;
          s.pingSentAt = now;
          s.ws.ping();
        }
      }
      if (room.conns.size === 0 && room.emptyAt !== 0 && now - room.emptyAt > cfg.roomEmptyGraceSeconds * 1000) {
        void destroyRoom(room);
      }
    }
  }, 1000);
  houseTimer.unref();

  return {
    roomCount: () => rooms.size,
    connCount: () => total,
    async close(): Promise<void> {
      closing = true;
      setRelayState('closing');
      clearInterval(flushTimer);
      clearInterval(houseTimer);
      const maint = maintPhase() === 'active';
      for (const room of rooms.values()) {
        for (const s of room.conns.values()) {
          s.cause = 'closed';
          if (maint) s.bye(1001, 'MAINTENANCE', true, maintRetryAfterMs());
          else s.bye(1001, 'GOING_AWAY', true, randomInt(3000, 8001));
        }
      }
      await Promise.all([...rooms.values()].map((r) => r.finish()));
      rooms.clear();
      setRelayHub(null);
      await new Promise<void>((resolve) => {
        const kill = setTimeout(() => {
          for (const c of wss.clients) c.terminate();
        }, 500);
        wss.close(() => {
          clearTimeout(kill);
          resolve();
        });
        for (const c of wss.clients) if (c.readyState !== c.OPEN) c.terminate();
        setTimeout(() => {
          for (const c of wss.clients) c.terminate();
        }, 600).unref();
      });
      setRelayState('unused');
    },
  };
}

export type { RoomKind };
