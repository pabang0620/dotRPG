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
  response?: { params?: { result?: string; steamid?: string; ownersteamid?: string; publisherbanned?: boolean } };
}

export const webApiVerifier: SteamVerifier = {
  async verify(ticket) {
    const { steam } = getConfig();
    if (ticket.startsWith('mock:')) throw invalid();
    const url = new URL('https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/');
    url.searchParams.set('key', steam.webApiKey ?? '');
    url.searchParams.set('appid', String(steam.appId ?? ''));
    url.searchParams.set('ticket', ticket);
    url.searchParams.set('identity', steam.identity);
    let body: WebApiResponse;
    try {
      const res = await fetch(url, { signal: AbortSignal.timeout(5000) });
      if (!res.ok) throw new Error(`status ${res.status}`);
      body = (await res.json()) as WebApiResponse;
    } catch (err) {
      // 키가 담긴 URL은 남기지 않는다
      logger.warn({ err: err instanceof Error ? err.message : String(err) }, 'steam web api failed');
      throw new AppError(503, 'Steam 서버에 연결할 수 없습니다.', 'STEAM_UNAVAILABLE');
    }
    const p = body.response?.params;
    if (!p || p.result !== 'OK' || !p.steamid || !/^\d{17}$/.test(p.steamid)) throw invalid();
    return { steamId: p.steamid, ownerSteamId: p.ownersteamid ?? null, publisherBanned: p.publisherbanned === true };
  },
};

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
    for (const [h, at] of this.seen) if (nowMs - at > TTL_MS) this.seen.delete(h);
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
