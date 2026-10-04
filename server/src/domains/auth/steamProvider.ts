// Steam 티켓 검증(A1, A2). 인증 공급자 인터페이스 뒤에 두어 mock(개발) / web_api(실제)를 바꾼다.
// 티켓 원문은 로그에 남기지 않는다(앞 8자 해시만).
import { createHash } from 'node:crypto';
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';

export interface SteamIdentity {
  steamId: string;
  ownerSteamId: string | null;
  publisherBanned: boolean;
}

export interface SteamVerifier {
  verify(ticket: string): Promise<SteamIdentity>;
}

const MOCK_RE = /^mock:(\d{17}):[A-Za-z0-9]{8,32}$/;
const invalid = () => new AppError(401, 'Steam 티켓이 올바르지 않습니다.', 'STEAM_TICKET_INVALID');

/** 개발용: 네트워크 호출 없이 SteamID를 믿는다. 운영에서는 기동 검사가 거부한다 */
export const mockVerifier: SteamVerifier = {
  async verify(ticket) {
    const m = MOCK_RE.exec(ticket);
    if (!m) throw invalid();
    return { steamId: m[1] as string, ownerSteamId: null, publisherBanned: false };
  },
};

interface WebApiResponse {
  response?: {
    params?: { result?: string; steamid?: string; ownersteamid?: string; publisherbanned?: boolean };
    error?: { errorcode?: number; errordesc?: string };
  };
}

// ---------- 지표와 회로 차단(phase8_api.md 5.5 S5, S7) ----------

const stats = {
  ok: 0,
  fail: 0,
  invalid: 0,
  latencies: [] as number[],
  consecutiveFails: 0,
  breakerOpenUntil: 0,
  /** 키·앱 ID가 거절된 마지막 시각(우리 설정 오류, 긴급 알림) */
  misconfiguredAt: 0,
};

export function steamStats(): {
  ok: number;
  fail: number;
  invalid: number;
  latency_p95_ms: number;
  breaker_open: boolean;
  misconfigured_recent: boolean;
} {
  const sorted = [...stats.latencies].sort((a, b) => a - b);
  const now = Date.now();
  return {
    ok: stats.ok,
    fail: stats.fail,
    invalid: stats.invalid,
    latency_p95_ms: sorted.length === 0 ? 0 : (sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))] as number),
    breaker_open: now < stats.breakerOpenUntil,
    misconfigured_recent: stats.misconfiguredAt > 0 && now - stats.misconfiguredAt < 10 * 60_000,
  };
}

/** 테스트 전용 */
export function resetSteamStats(): void {
  stats.ok = stats.fail = stats.invalid = 0;
  stats.latencies = [];
  stats.consecutiveFails = 0;
  stats.breakerOpenUntil = 0;
  stats.misconfiguredAt = 0;
}

const unavailable = (retryAfterSec?: number) =>
  new AppError(503, 'Steam 서버에 연결할 수 없습니다.', 'STEAM_UNAVAILABLE', retryAfterSec !== undefined ? { retry_after_sec: retryAfterSec } : undefined);

function recordFailure(): void {
  stats.fail++;
  stats.consecutiveFails++;
  const { breakerFailures, breakerOpenSeconds } = getConfig().steam;
  if (stats.consecutiveFails >= breakerFailures) {
    stats.breakerOpenUntil = Date.now() + breakerOpenSeconds * 1000;
    stats.consecutiveFails = 0;
    logger.error({ open_seconds: breakerOpenSeconds }, 'steam.breaker_open');
  }
}

/** Steam이 응답했다(성공이든 무효 티켓이든 Steam 쪽은 살아 있다) */
function recordReachable(ms: number): void {
  stats.consecutiveFails = 0;
  stats.latencies.push(ms);
  if (stats.latencies.length > 200) stats.latencies.shift();
}

export type SteamCall =
  | { kind: 'ok'; identity: SteamIdentity }
  | { kind: 'invalid'; detail: string }
  | { kind: 'rejected'; status: number }
  | { kind: 'unavailable' };

/** AuthenticateUserTicket 호출 한 번(재시도 포함). 키가 담긴 URL은 로그에 남기지 않는다 */
async function callSteam(ticket: string): Promise<SteamCall> {
  const { steam } = getConfig();
  const url = new URL('/ISteamUserAuth/AuthenticateUserTicket/v1/', steam.webApiBase);
  url.searchParams.set('key', steam.webApiKey ?? '');
  url.searchParams.set('appid', String(steam.appId ?? ''));
  url.searchParams.set('ticket', ticket);
  url.searchParams.set('identity', steam.identity);
  const deadline = Date.now() + 8000;
  for (let attempt = 0; ; attempt++) {
    const t0 = Date.now();
    try {
      const res = await fetch(url, { signal: AbortSignal.timeout(Math.max(500, Math.min(5000, deadline - t0))) });
      // 400은 티켓 형식·내용 문제(사용자 입력)라 무효 티켓으로 본다. 401/403만 우리 키·앱 ID 설정 오류다
      if (res.status === 400) {
        recordReachable(Date.now() - t0);
        stats.invalid++;
        return { kind: 'invalid', detail: 'http_400' };
      }
      if (res.status === 401 || res.status === 403) {
        // 키 만료·앱 ID 오설정은 사용자 탓이 아니다: 운영자가 즉시 알게 한다
        stats.misconfiguredAt = Date.now();
        logger.error({ status: res.status }, 'steam.key_rejected');
        recordFailure();
        return { kind: 'rejected', status: res.status };
      }
      if (res.status >= 500 || !res.ok) throw new Error(`status ${res.status}`);
      const body = (await res.json()) as WebApiResponse;
      recordReachable(Date.now() - t0);
      const p = body.response?.params;
      if (body.response?.error || !p || p.result !== 'OK' || !p.steamid || !/^\d{17}$/.test(p.steamid)) {
        stats.invalid++;
        return { kind: 'invalid', detail: body.response?.error?.errordesc ?? 'not_ok' };
      }
      stats.ok++;
      return { kind: 'ok', identity: { steamId: p.steamid, ownerSteamId: p.ownersteamid ?? null, publisherBanned: p.publisherbanned === true } };
    } catch (err) {
      logger.warn({ err: err instanceof Error ? err.message : String(err), attempt }, 'steam web api failed');
      // 네트워크 오류·5xx에 한해 재시도한다(총 8초 이내)
      if (attempt < steam.retries && Date.now() < deadline - 500) continue;
      recordFailure();
      return { kind: 'unavailable' };
    }
  }
}

export const webApiVerifier: SteamVerifier = {
  async verify(ticket) {
    if (ticket.startsWith('mock:')) throw invalid();
    const now = Date.now();
    if (now < stats.breakerOpenUntil) throw unavailable(Math.max(1, Math.ceil((stats.breakerOpenUntil - now) / 1000)));
    const r = await callSteam(ticket);
    if (r.kind === 'ok') return r.identity;
    if (r.kind === 'invalid') throw invalid();
    throw unavailable();
  },
};

/** 기동 자가 점검(S6): 일부러 잘못된 티켓으로 한 번 불러 키·앱 ID가 정상인지 로그에 남긴다. 실패해도 서버는 뜬다 */
export async function steamSelfCheck(): Promise<SteamCall['kind']> {
  const r = await callSteam('00');
  if (r.kind === 'invalid') logger.info({ result: 'ok_invalid_ticket', detail: r.detail }, 'steam.selfcheck');
  else if (r.kind === 'rejected') logger.error({ result: 'key_rejected', status: r.status }, 'steam.selfcheck');
  else if (r.kind === 'unavailable') logger.warn({ result: 'unavailable' }, 'steam.selfcheck');
  else logger.warn({ result: 'unexpected_ok' }, 'steam.selfcheck');
  return r.kind;
}

let override: SteamVerifier | null = null;
export function setSteamVerifier(v: SteamVerifier | null): void {
  override = v;
}
export function getSteamVerifier(): SteamVerifier {
  if (override) return override;
  return getConfig().steam.mode === 'web_api' ? webApiVerifier : mockVerifier;
}

// ---------- 티켓 재사용 방지(메모리, 10분). 나중에 Redis로 ----------

export interface TicketReplayStore {
  /** 처음 보는 티켓이면 기록하고 true, 이미 있으면 false */
  consume(hash: string, nowMs: number): boolean;
}

const TTL_MS = 10 * 60_000;

export class MemoryTicketReplayStore implements TicketReplayStore {
  private seen = new Map<string, number>();
  consume(hash: string, nowMs: number): boolean {
    // Map은 삽입 순서(시각 오름차순)라 첫 만료 안 된 항목에서 멈춘다
    for (const [h, at] of this.seen) {
      if (nowMs - at > TTL_MS) this.seen.delete(h);
      else break;
    }
    if (this.seen.has(hash)) return false;
    this.seen.set(hash, nowMs);
    return true;
  }
}

let replayStore: TicketReplayStore = new MemoryTicketReplayStore();
export function getTicketReplayStore(): TicketReplayStore {
  return replayStore;
}
export function setTicketReplayStore(s: TicketReplayStore): void {
  replayStore = s;
}

export const ticketHash = (ticket: string): string => createHash('sha256').update(ticket).digest('hex');

/** 검증 + 재사용 방지. 로그인과 연결이 같이 쓴다 */
export async function verifyTicket(ticket: string): Promise<SteamIdentity> {
  const id = await getSteamVerifier().verify(ticket);
  if (id.publisherBanned) throw new AppError(403, 'Steam 계정이 제한되었습니다.', 'STEAM_BANNED');
  if (!getTicketReplayStore().consume(ticketHash(ticket), Date.now())) {
    throw new AppError(401, '이미 사용한 Steam 티켓입니다.', 'TICKET_REPLAYED');
  }
  if (id.ownerSteamId && id.ownerSteamId !== id.steamId) {
    // 패밀리 공유: 기록만 하고 로그인은 허용한다(정책은 운영에서)
    logger.info({ steam: id.steamId.slice(0, 6) }, 'steam family sharing login');
  }
  return id;
}
