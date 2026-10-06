// Steam 결제 API(ISteamMicroTxn) 어댑터 인터페이스와 공통 보호막(Docs/server/phase11_payments.md 7절).
// 실제 호출(steamPartnerHttp.ts)과 가짜(steamPartnerMock.ts)는 이 인터페이스 뒤에 있고, 서비스는 getSteamPartner()만 부른다.
// 보호막(GuardedPartner): 호출별 재시도 규칙(InitTxn·FinalizeTxn은 맹목적으로 재시도하지 않는다), 결제 전용 회로 차단(인증용과 분리),
// 키 거절(401/403) 기록, 작업의 분당 호출 상한. 키·URL·응답 원문은 오류 메시지와 로그에 남기지 않는다(7.6).
//
// TODO [확인] 7.8 항목(Steamworks 문서·샌드박스 실측으로 확정할 것): 퍼블리셔 API 호스트와 샌드박스 사용법, InitTxn/QueryTxn/FinalizeTxn의
// 파라미터명·버전·usersession·ipaddress·amount 단위와 세금, 상태 문자열 전체 철자, FinalizeTxn 재호출 응답, GetUserInfo/GetReport 형식,
// 환불·차지백 정책과 통지 지연, MicroTxnAuthorizationResponse 필드, 키의 IP 제한과 교체, 주문 번호 유일성 범위.
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';

export type PartnerErrorKind = 'unavailable' | 'rejected' | 'breaker_open';

/** 키·URL·원문이 없는 오류(7.6). unavailable = 닿지 못함·시간 초과·5xx(결과를 모른다), rejected = 401/403(키 문제) */
export class PartnerCallError extends Error {
  constructor(
    readonly kind: PartnerErrorKind,
    readonly httpStatus: number | null = null,
    readonly retryAfterSec?: number,
  ) {
    super(`steam partner call failed: ${kind}${httpStatus ? ` (${httpStatus})` : ''}`);
    this.name = 'PartnerCallError';
  }
}

export interface UserInfo {
  /** ISO 3166-1 alpha-2 */
  country: string;
  /** ISO 4217 */
  currency: string;
}

export interface InitRequest {
  /** 서버가 발급한 주문 번호(uint64를 문자열로) */
  orderId: string;
  steamId: string;
  appId: number;
  itemId: number;
  itemName: string;
  amountMinor: number;
  currency: string;
  language: string;
}

export type InitResult = { ok: true; transId: string | null } | { ok: false; errorCode: string };

export interface TxnItem {
  itemId: number;
  quantity: number;
  amountMinor: number;
}

export interface TxnView {
  orderId: string;
  transId: string | null;
  steamId: string;
  appId: number;
  /** Steam 상태 문자열(원문 그대로, 40자 이하). 의미 해석은 orderMachine */
  status: string;
  country: string | null;
  currency: string;
  items: TxnItem[];
}

export type QueryResult = { found: false } | { found: true; txn: TxnView };
export type FinalizeResult = { ok: true } | { ok: false; errorCode: string };

export interface ReportEntry {
  orderId: string;
  steamId: string;
  status: string;
  amountMinor: number;
  currency: string;
}

export interface SteamPartner {
  getUserInfo(steamId: string): Promise<UserInfo>;
  /** 비멱등: 재시도하지 않는다. 시간 초과·5xx는 PartnerCallError('unavailable')로 던진다(결과를 모름) */
  initTxn(req: InitRequest): Promise<InitResult>;
  queryTxn(orderId: string): Promise<QueryResult>;
  /** 비멱등: 재시도하지 않는다 */
  finalizeTxn(orderId: string): Promise<FinalizeResult>;
  getReport(from: Date, to: Date): Promise<ReportEntry[]>;
}

// ---------------- 회로 차단·지표(인증용 steamProvider와 카운터를 분리) ----------------

const stats = {
  consecutiveFails: 0,
  breakerOpenUntil: 0,
  keyRejectedAt: 0,
  ok: 0,
  fail: 0,
  jobCalls: [] as number[],
};

export function payBreakerRemainingSec(): number {
  const left = stats.breakerOpenUntil - Date.now();
  return left > 0 ? Math.max(1, Math.ceil(left / 1000)) : 0;
}

export function payPartnerStats(): { breaker_open: boolean; key_rejected_recent: boolean; ok: number; fail: number } {
  return {
    breaker_open: payBreakerRemainingSec() > 0,
    key_rejected_recent: stats.keyRejectedAt > 0 && Date.now() - stats.keyRejectedAt < 10 * 60_000,
    ok: stats.ok,
    fail: stats.fail,
  };
}

/** 테스트 전용 */
export function resetPayPartnerStats(): void {
  stats.consecutiveFails = 0;
  stats.breakerOpenUntil = 0;
  stats.keyRejectedAt = 0;
  stats.ok = 0;
  stats.fail = 0;
  stats.jobCalls = [];
}

function recordFailure(): void {
  stats.fail++;
  stats.consecutiveFails++;
  const { breakerFailures, breakerOpenSeconds } = getConfig().steam;
  if (stats.consecutiveFails >= breakerFailures) {
    stats.breakerOpenUntil = Date.now() + breakerOpenSeconds * 1000;
    stats.consecutiveFails = 0;
    logger.error({ open_seconds: breakerOpenSeconds }, 'payment.steam_breaker_open');
  }
}

/** 작업(대사·감시·리포트)이 Steam을 부를 수 있는 분당 상한(PAY_STEAM_MAX_CALLS_PER_MIN). 호출 전에 부르고 false면 이번 틱은 건너뛴다 */
export function takeJobCall(): boolean {
  const now = Date.now();
  stats.jobCalls = stats.jobCalls.filter((t) => now - t < 60_000);
  if (stats.jobCalls.length >= getConfig().pay.steamMaxCallsPerMin) return false;
  stats.jobCalls.push(now);
  return true;
}

class GuardedPartner implements SteamPartner {
  constructor(private readonly raw: SteamPartner) {}

  /** retries: 추가 시도 횟수(GET 성격은 2, 비멱등은 0) */
  private async call<T>(name: string, retries: number, fn: () => Promise<T>): Promise<T> {
    const left = payBreakerRemainingSec();
    if (left > 0) throw new PartnerCallError('breaker_open', null, left);
    for (let attempt = 0; ; attempt++) {
      try {
        const r = await fn();
        stats.consecutiveFails = 0;
        stats.ok++;
        return r;
      } catch (err) {
        if (err instanceof PartnerCallError && err.kind === 'rejected') {
          // 키 거절은 우리 설정 문제라 재시도하지 않고 즉시 알린다(critical 경보 payment_key_rejected)
          stats.keyRejectedAt = Date.now();
          logger.error({ call: name, status: err.httpStatus }, 'payment.key_rejected');
          recordFailure();
          throw new PartnerCallError('rejected', err.httpStatus);
        }
        if (attempt < retries) continue;
        recordFailure();
        // 원문 오류는 버리고 키가 없는 오류로 다시 던진다
        logger.warn({ call: name, attempt }, 'payment.steam_call_failed');
        throw new PartnerCallError('unavailable', err instanceof PartnerCallError ? err.httpStatus : null);
      }
    }
  }

  getUserInfo(steamId: string): Promise<UserInfo> {
    return this.call('GetUserInfo', 2, () => this.raw.getUserInfo(steamId));
  }
  initTxn(req: InitRequest): Promise<InitResult> {
    return this.call('InitTxn', 0, () => this.raw.initTxn(req));
  }
  queryTxn(orderId: string): Promise<QueryResult> {
    return this.call('QueryTxn', 2, () => this.raw.queryTxn(orderId));
  }
  finalizeTxn(orderId: string): Promise<FinalizeResult> {
    return this.call('FinalizeTxn', 0, () => this.raw.finalizeTxn(orderId));
  }
  getReport(from: Date, to: Date): Promise<ReportEntry[]> {
    return this.call('GetReport', 2, () => this.raw.getReport(from, to));
  }
}

let override: SteamPartner | null = null;
let httpFactory: (() => SteamPartner) | null = null;
let mockFactory: (() => SteamPartner) | null = null;
let cached: { mode: string; partner: GuardedPartner } | null = null;

/** 테스트 전용: 가짜 어댑터를 끼운다(실제 Steam 호출 금지). null이면 설정(PAYMENTS_STEAM_MODE)을 따른다 */
export function setSteamPartner(p: SteamPartner | null): void {
  if (process.env.NODE_ENV === 'production') throw new Error('운영에서는 결제 어댑터를 바꿀 수 없습니다');
  override = p;
  cached = null;
}

/** 모드별 구현 등록(순환 import를 피하려고 구현 파일이 스스로 등록한다) */
export function registerPartnerFactories(f: { http: () => SteamPartner; mock: () => SteamPartner }): void {
  httpFactory = f.http;
  mockFactory = f.mock;
}

export function getSteamPartner(): SteamPartner {
  const mode = getConfig().pay.mode;
  if (override) {
    if (!cached || cached.mode !== 'override') cached = { mode: 'override', partner: new GuardedPartner(override) };
    return cached.partner;
  }
  if (mode === 'off') throw new AppError(503, 'Steam 결제 연동이 꺼져 있습니다.', 'STEAM_UNAVAILABLE');
  if (!cached || cached.mode !== mode) {
    const f = mode === 'mock' ? mockFactory : httpFactory;
    if (!f) throw new Error('결제 어댑터가 등록되지 않았습니다');
    cached = { mode, partner: new GuardedPartner(f()) };
  }
  return cached.partner;
}
