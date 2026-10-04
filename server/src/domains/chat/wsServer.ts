// WebSocket 서버(/ws): 같은 HTTP 서버의 upgrade 이벤트에 붙는다(별도 포트·프로세스 없음).
import type { IncomingMessage, Server } from 'node:http';
import type { Duplex } from 'node:stream';
import { randomInt } from 'node:crypto';
import { WebSocketServer, type RawData, type WebSocket } from 'ws';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { notifyPartyMatesOfMove } from './partyNotify';
import { verifyAccessToken } from '../../middleware/authMiddleware';
import { isShuttingDown } from '../../ops/lifecycle';
import { maintPhase, maintRetryAfterMs } from '../../ops/maintenanceState';
import { metrics } from '../../ops/metrics';
import { getRateLimitStore } from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { compareSemver } from '../../utils/semver';
import { buildBacklog } from './backlogService';
import { handleChatSend, loadMute } from './chatService';
import { ChatSession } from './chatSession';
import { getChatWriter } from './chatWriter';
import { getLimiterStore } from './limiterStore';
import { pushMyPresence, sendAllPresence } from './presenceService';
import { registry } from './realtimeNotifier';
import { checkBan, refreshAccount, startSanctionListener, stopSanctionListener } from './sanctionService';
import { CLOSE, PROTOCOL_VERSION, clientFrame, type ClientFrame, type Frame } from './wsProtocol';
import { incomingInvites } from '../partyinvites/partyInvitesService';

export interface RealtimeHandle {
  close(): Promise<void>;
}

/** Express의 trust proxy 규칙과 같게 클라이언트 IP를 구한다(프록시 뒤에서 모두 같은 IP로 보이는 사고 방지) */
export function clientIp(req: IncomingMessage): string {
  const hops = getConfig().trustProxy;
  const remote = req.socket.remoteAddress ?? 'unknown';
  if (hops <= 0) return remote;
  const xff = req.headers['x-forwarded-for'];
  const list = (Array.isArray(xff) ? xff.join(',') : (xff ?? '')).split(',').map((x) => x.trim()).filter(Boolean);
  const chain = [...list, remote];
  return chain[Math.max(0, chain.length - 1 - hops)] ?? remote;
}

function reject(socket: Duplex, status: string): void {
  socket.write(`HTTP/1.1 ${status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
  socket.destroy();
}

function rawSend(ws: WebSocket, frame: Frame): void {
  if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(frame));
}

function rawBye(ws: WebSocket, code: number, reason: string, reconnect: boolean): void {
  rawSend(ws, { t: 'bye', code, reason, reconnect });
  ws.close(code, reason);
}

export async function attachRealtime(server: Server): Promise<RealtimeHandle> {
  const cfg = getConfig();
  const sc = cfg.social;
  const wss = new WebSocketServer({ noServer: true, maxPayload: sc.wsMaxPayloadBytes, perMessageDeflate: false });
  const unauthByIp = new Map<string, number>();
  let closing = false;
  let total = 0;

  server.on('upgrade', (req, socket, head) => {
    const path = new URL(req.url ?? '/', 'http://localhost').pathname;
    // /relay 는 같은 서버의 다른 리스너(domains/relay)가 처리한다
    if (path === '/relay') return;
    if (path !== '/ws') return reject(socket, '404 Not Found');
    // 종료 중이거나 점검 중(active)이면 새 연결을 받지 않는다(phase7_ops.md 4.2, 4.5)
    if (closing || isShuttingDown() || maintPhase() === 'active') {
      metrics.wsHandshakeRejected++;
      return reject(socket, '503 Service Unavailable');
    }
    const ip = clientIp(req);
    const store = getRateLimitStore();
    const key = `ws-handshake:${ip}`;
    if (store.count(key, 60_000).count >= sc.wsHandshakePerMinIp) return reject(socket, '429 Too Many Requests');
    store.add(key, 60_000);
    if (total >= sc.wsMaxConnections) return reject(socket, '503 Service Unavailable');
    if ((unauthByIp.get(ip) ?? 0) >= sc.wsUnauthPerIp) return reject(socket, '429 Too Many Requests');
    wss.handleUpgrade(req, socket, head, (ws) => onConnection(ws, ip));
  });

  function onConnection(ws: WebSocket, ip: string): void {
    total++;
    unauthByIp.set(ip, (unauthByIp.get(ip) ?? 0) + 1);
    let unauthCounted = true;
    const uncount = (): void => {
      if (!unauthCounted) return;
      unauthCounted = false;
      const n = (unauthByIp.get(ip) ?? 1) - 1;
      if (n <= 0) unauthByIp.delete(ip);
      else unauthByIp.set(ip, n);
    };
    let session: ChatSession | null = null;
    const stamps: number[] = [];
    let bad: number[] = [];
    let chain: Promise<void> = Promise.resolve();

    const helloTimer = setTimeout(() => {
      if (!session) rawBye(ws, CLOSE.HELLO_TIMEOUT, 'HELLO_TIMEOUT', true);
    }, sc.wsHelloTimeoutMs);

    const strike = (): boolean => {
      const now = Date.now();
      bad = bad.filter((t) => now - t < 10_000);
      bad.push(now);
      if (bad.length >= 3) {
        if (session) session.close(CLOSE.BAD_PROTOCOL, 'BAD_PROTOCOL', false);
        else rawBye(ws, CLOSE.BAD_PROTOCOL, 'BAD_PROTOCOL', false);
        return true;
      }
      return false;
    };
    const err = (ref: string | undefined, code: string, message: string): void => {
      const f: Frame = { t: 'error', code, message };
      if (ref) f.ref = ref;
      if (session) session.send(f);
      else rawSend(ws, f);
    };

    const closeWith = (code: number, reason: string, reconnect: boolean, extra?: Frame): void => {
      if (extra) rawSend(ws, extra);
      if (session) session.close(code, reason, reconnect);
      else rawBye(ws, code, reason, reconnect);
    };

    async function hello(f: Extract<ClientFrame, { t: 'hello' }>): Promise<void> {
      if (compareSemver(f.client_version, cfg.minClientVersion) < 0) {
        return closeWith(CLOSE.CLIENT_OUTDATED, 'CLIENT_OUTDATED', false, {
          t: 'error', code: 'CLIENT_OUTDATED', message: '게임을 업데이트해 주세요.',
        });
      }
      let verified;
      try {
        verified = await verifyAccessToken(f.token);
      } catch (e) {
        if (e instanceof AppError && e.code === 'ACCOUNT_BANNED') {
          return closeWith(CLOSE.BANNED, 'BANNED', false, {
            t: 'sanction', kind: 'ban', ends_at: e.extra?.banned_until, reason_code: 'other', message: '이용이 정지된 계정입니다.',
          });
        }
        if (e instanceof AppError && e.code === 'TOKEN_EXPIRED') return closeWith(CLOSE.TOKEN_EXPIRED, 'TOKEN_EXPIRED', true);
        if (e instanceof AppError) return closeWith(CLOSE.TOKEN_INVALID, 'TOKEN_INVALID', false);
        throw e;
      }
      const account = verified.account;
      const ch = await getPool().query<{ id: string; uuid: string; name: string }>(
        'SELECT id, uuid, name FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL',
        [f.character_id, account.id],
      );
      const row = ch.rows[0];
      if (!row) return closeWith(CLOSE.CHARACTER_INVALID, 'CHARACTER_INVALID', false);

      const s = new ChatSession(ws, account.id, account.uuid, Number(row.id), row.uuid, row.name, verified.expiresAtSec, sc.wsSendQueueMax);
      const blocks = await getPool().query<{ a: string }>(
        'SELECT blocked_account_id AS a FROM blocks WHERE blocker_account_id = $1 AND deleted_at IS NULL',
        [account.id],
      );
      s.blocks = new Set(blocks.rows.map((x) => Number(x.a)));
      await loadMute(s);
      if (f.caps?.steam_p2p === true) {
        const st = await getPool().query("SELECT 1 FROM auth_identities WHERE provider = 'steam' AND account_id = $1", [account.id]);
        s.caps.steamP2p = st.rows.length > 0;
      }
      await getPool().query('UPDATE accounts SET last_character_id = $2 WHERE id = $1', [account.id, s.characterId]);
      if (ws.readyState !== ws.OPEN) return;
      // 먼저 수신자 목록에 등록(이후 전달분은 세션 큐에 쌓인다), 그다음 backlog를 읽는다
      // 같은 계정의 hello가 동시에 둘 오면 위의 await 사이에 서로를 못 본다: 등록 직전에(await 없이) 다시 확인한다
      const old = registry.ofAccount(account.id);
      if (old) {
        registry.remove(old);
        old.close(CLOSE.REPLACED, 'REPLACED', false);
      }
      s.shard = registry.pickShard();
      session = s;
      clearTimeout(helloTimer);
      uncount();
      registry.add(s);
      const nowMs = Date.now();
      const st = getLimiterStore().get(account.id);
      const { lines, gap, cursor } = await buildBacklog(s, f.since ?? null);
      const mute =
        s.sanctionMuteUntil > nowMs
          ? { ends_at: new Date(s.sanctionMuteUntil).toISOString(), source: 'sanction' }
          : st.repeatMutedUntil > nowMs
            ? { ends_at: new Date(st.repeatMutedUntil).toISOString(), source: 'repeat' }
            : null;
      s.sendNow({
        t: 'ready',
        v: PROTOCOL_VERSION,
        server_time: new Date().toISOString(),
        account_id: account.uuid,
        character: { id: s.characterUuid, name: s.characterName },
        shard: s.shard,
        token_expires_at: new Date(s.tokenExpSec * 1000).toISOString(),
        ping_every_ms: sc.wsPingEveryMs,
        idle_timeout_ms: sc.wsIdleTimeoutMs,
        limits: {
          max_length: getGameData().chat.maxLength,
          min_interval_ms: getGameData().chat.minIntervalSeconds * 1000,
          resend_seconds: sc.chatResendSeconds,
          party_poll_seconds: sc.partyPollWsSeconds,
        },
        mute,
      });
      s.sendNow({ t: 'backlog', lines, gap, cursor });
      await sendAllPresence(s);
      for (const inv of await incomingInvites(s.characterId, new Date())) s.sendNow({ t: 'party.invite', ...inv });
      s.flush(cursor);
      await refreshAccount(account.id);
      pushMyPresence(account.id).catch((e: unknown) => logger.error({ err: e }, 'presence push failed'));
    }

    async function handle(f: ClientFrame): Promise<void> {
      if (!session) {
        if (f.t !== 'hello') {
          err(f.t, 'NOT_AUTHENTICATED', '먼저 hello로 인증해야 합니다.');
          strike();
          return;
        }
        return hello(f);
      }
      const s = session;
      switch (f.t) {
        case 'hello':
          err('hello', 'BAD_FRAME', '이미 인증되었습니다.');
          strike();
          return;
        case 'ping':
          s.send({ t: 'pong', ...(f.n !== undefined ? { n: f.n } : {}), server_time: new Date().toISOString() });
          return;
        case 'auth': {
          try {
            const v = await verifyAccessToken(f.token);
            if (v.account.id !== s.accountId) return s.close(CLOSE.TOKEN_INVALID, 'TOKEN_INVALID', false);
            s.tokenExpSec = v.expiresAtSec;
            s.tokenWarned = false;
            s.send({ t: 'auth.ok', token_expires_at: new Date(v.expiresAtSec * 1000).toISOString() });
          } catch (e) {
            if (e instanceof AppError && e.code === 'TOKEN_EXPIRED') s.close(CLOSE.TOKEN_EXPIRED, 'TOKEN_EXPIRED', true);
            else if (e instanceof AppError && e.code === 'ACCOUNT_BANNED') await checkBan(s);
            else s.close(CLOSE.TOKEN_INVALID, 'TOKEN_INVALID', false);
          }
          return;
        }
        case 'chat.send':
          await handleChatSend(s, f);
          return;
        case 'presence.set':
          if (!getGameData().maps.has(f.map_id)) {
            err('presence.set', 'BAD_FRAME', '알 수 없는 맵입니다.');
            return;
          }
          if (s.mapId !== f.map_id) {
            s.mapId = f.map_id;
            await pushMyPresence(s.accountId);
            await notifyPartyMatesOfMove(s.characterId);
          }
          return;
      }
    }

    ws.on('message', (data: RawData, isBinary: boolean) => {
      const now = Date.now();
      if (session) session.lastFrameAt = now;
      while (stamps.length > 0 && now - (stamps[0] as number) >= 1000) stamps.shift();
      stamps.push(now);
      if (stamps.length > sc.wsFramesPerSec) return closeWith(CLOSE.FLOOD, 'FLOOD', true);
      if (isBinary) {
        err(undefined, 'BAD_FRAME', '텍스트(JSON) 프레임만 받습니다.');
        strike();
        return;
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(data.toString());
      } catch {
        err(undefined, 'BAD_FRAME', 'JSON을 읽을 수 없습니다.');
        strike();
        return;
      }
      const r = clientFrame.safeParse(parsed);
      if (!r.success) {
        const t = (parsed as { t?: unknown } | null)?.t;
        err(typeof t === 'string' ? t : undefined, 'BAD_FRAME', '프레임 형식이 올바르지 않습니다.');
        strike();
        return;
      }
      // 한 연결의 프레임은 도착 순서대로 처리한다(채팅 순서 보장)
      chain = chain.then(() => handle(r.data)).catch((e: unknown) => {
        logger.error({ err: e }, 'ws frame failed');
        err(r.data.t, 'INTERNAL', '처리 중 오류가 발생했습니다.');
      });
    });

    ws.on('error', () => undefined);
    ws.on('close', () => {
      clearTimeout(helloTimer);
      total--;
      uncount();
      const s = session;
      if (s) {
        s.closed = true;
        if (registry.remove(s)) pushMyPresence(s.accountId).catch(() => undefined);
      }
    });
  }

  // 유휴 정리, 토큰 만료 안내, 정지 재확인
  const tick = setInterval(() => {
    const now = Date.now();
    for (const s of registry.all()) {
      if (now - s.lastFrameAt > sc.wsIdleTimeoutMs) {
        s.close(CLOSE.IDLE, 'IDLE', true);
        continue;
      }
      const nowSec = now / 1000;
      if (!s.tokenWarned && nowSec >= s.tokenExpSec - 60) {
        s.tokenWarned = true;
        s.send({ t: 'token.expiring', expires_at: new Date(s.tokenExpSec * 1000).toISOString() });
      }
      if (nowSec >= s.tokenExpSec + sc.wsTokenGraceSeconds) {
        s.close(CLOSE.TOKEN_EXPIRED, 'TOKEN_EXPIRED', true);
        continue;
      }
      if (now - s.lastRevalidateAt >= sc.wsRevalidateSeconds * 1000) {
        s.lastRevalidateAt = now;
        refreshAccount(s.accountId).catch((e: unknown) => logger.error({ err: e }, 'ws revalidate failed'));
      }
    }
  }, 1000);
  tick.unref();

  await startSanctionListener();

  return {
    async close(): Promise<void> {
      closing = true;
      clearInterval(tick);
      // 점검 창이 active면 reason=MAINTENANCE와 긴 retry_after_ms, 아니면 기존 GOING_AWAY(3~8초 무작위)
      const maint = maintPhase() === 'active';
      for (const s of registry.all()) {
        if (maint) s.close(CLOSE.GOING_AWAY, 'MAINTENANCE', true, maintRetryAfterMs());
        else s.close(CLOSE.GOING_AWAY, 'GOING_AWAY', true, randomInt(3000, 8001));
      }
      await getChatWriter().drain();
      await stopSanctionListener();
      await new Promise<void>((resolve) => {
        const kill = setTimeout(() => {
          for (const c of wss.clients) c.terminate();
        }, 500);
        wss.close(() => {
          clearTimeout(kill);
          resolve();
        });
        // 인증 전 소켓과 닫기 응답이 없는 소켓이 서버 종료를 막지 않게 한다
        for (const c of wss.clients) if (c.readyState !== c.OPEN) c.terminate();
        setTimeout(() => {
          for (const c of wss.clients) c.terminate();
        }, 600).unref();
      });
    },
  };
}
