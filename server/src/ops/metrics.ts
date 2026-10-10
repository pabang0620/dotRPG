// 프로세스 안 메모리 지표(재시작하면 0). OPS_SNAPSHOT_SECONDS마다 ops.snapshot 로그로 내보내고 관리자 OP1이 읽는다.
import { monitorEventLoopDelay } from 'node:perf_hooks';

interface ReqSample {
  at: number;
  status: number;
  ms: number;
  code?: string;
  route?: string;
}

const UUID_SEG = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** 경로 집계용 이름(15단계 G5): uuid·숫자 경로 조각을 :id로 바꾼다 */
export function routeKey(method: string, path: string): string {
  const norm = path
    .split('/')
    .map((seg) => (UUID_SEG.test(seg) || /^\d+$/.test(seg) ? ':id' : seg))
    .join('/');
  return `${method} ${norm}`;
}

const WINDOW_MS = 5 * 60_000;

function pct(sorted: number[], p: number): number {
  if (sorted.length === 0) return 0;
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] as number;
}

class Metrics {
  private samples: ReqSample[] = [];
  private loop = monitorEventLoopDelay({ resolution: 20 });
  readonly wsClose = new Map<string, number>();
  wsHandshakeRejected = 0;
  loginFailures: number[] = [];
  /** 틱·작업의 마지막 실행 */
  readonly ticks = new Map<string, { lastAt: number; ms: number }>();

  constructor() {
    this.loop.enable();
  }

  recordRequest(status: number, ms: number, code?: string, route?: string): void {
    const now = Date.now();
    this.samples.push({ at: now, status, ms, ...(code ? { code } : {}), ...(route ? { route } : {}) });
    if (this.samples.length > 20_000) this.samples = this.samples.filter((s) => now - s.at < WINDOW_MS);
  }

  recordWsClose(reason: string): void {
    this.wsClose.set(reason, (this.wsClose.get(reason) ?? 0) + 1);
  }

  recordLoginFailure(): void {
    const now = Date.now();
    this.loginFailures.push(now);
    this.loginFailures = this.loginFailures.filter((t) => now - t < WINDOW_MS);
  }

  recordTick(name: string, ms: number): void {
    this.ticks.set(name, { lastAt: Date.now(), ms });
  }

  /** 지금 실행 중인 틱 수(관리자 드레인 판단) */
  readonly running = new Map<string, number>();
  auctionLagSeconds = 0;
  /** 9단계: 기기 한도 초과 진입 수, 프레즌스 없는 처치 보고 수(프로세스 시작 이후 누계) */
  presenceDeviceLimitHits = 0;
  presenceRequired = 0;
  /** 10단계: 클리어권 주간 구매 한도에 걸린 요청 수(프로세스 시작 이후 누계) */
  sweepBuyLimitHits = 0;
  /** 탈퇴: 탈퇴 요청 수(withdrawal_requested_total, 프로세스 시작 이후 누계) */
  withdrawalRequested = 0;

  /** 틱 한 번을 감싼다: 실행 중 수와 마지막 실행 시각·소요를 기록한다 */
  async track<T>(name: string, fn: () => Promise<T>): Promise<T> {
    this.running.set(name, (this.running.get(name) ?? 0) + 1);
    const t0 = Date.now();
    try {
      return await fn();
    } finally {
      this.running.set(name, (this.running.get(name) ?? 1) - 1);
      this.recordTick(name, Date.now() - t0);
    }
  }

  runningTotal(): number {
    let n = 0;
    for (const v of this.running.values()) n += v;
    return n;
  }

  /** 최근 windowMs 안의 요청 통계 */
  http(windowMs = 60_000): {
    requests: number;
    status_2xx: number;
    status_4xx: number;
    status_5xx: number;
    p50_ms: number;
    p95_ms: number;
    p99_ms: number;
    top_errors: { code: string; count: number }[];
  } {
    const now = Date.now();
    const list = this.samples.filter((s) => now - s.at < windowMs);
    const ms = list.map((s) => s.ms).sort((a, b) => a - b);
    const codes = new Map<string, number>();
    for (const s of list) if (s.code) codes.set(s.code, (codes.get(s.code) ?? 0) + 1);
    return {
      requests: list.length,
      status_2xx: list.filter((s) => s.status >= 200 && s.status < 300).length,
      status_4xx: list.filter((s) => s.status >= 400 && s.status < 500).length,
      status_5xx: list.filter((s) => s.status >= 500).length,
      p50_ms: pct(ms, 50),
      p95_ms: pct(ms, 95),
      p99_ms: pct(ms, 99),
      top_errors: [...codes.entries()]
        .sort((a, b) => b[1] - a[1])
        .slice(0, 5)
        .map(([code, count]) => ({ code, count })),
    };
  }

  /** 15단계 G5: 최근 windowMs 안의 경로별 건수·p95(건수 많은 순 limit개). 잦은 경로의 요청 로그를 debug로 내린 대신 여기서 본다 */
  routes(windowMs = 5 * 60_000, limit = 12): { route: string; count: number; p95_ms: number; errors: number }[] {
    const now = Date.now();
    const by = new Map<string, { ms: number[]; errors: number }>();
    for (const s of this.samples) {
      if (!s.route || now - s.at >= windowMs) continue;
      const e = by.get(s.route) ?? { ms: [], errors: 0 };
      e.ms.push(s.ms);
      if (s.status >= 400) e.errors++;
      by.set(s.route, e);
    }
    return [...by.entries()]
      .map(([route, e]) => ({ route, count: e.ms.length, p95_ms: pct(e.ms.sort((a, b) => a - b), 95), errors: e.errors }))
      .sort((a, b) => b.count - a.count)
      .slice(0, limit);
  }

  eventLoopP99Ms(): number {
    return Math.round(this.loop.percentile(99) / 1e6);
  }

  resetEventLoop(): void {
    this.loop.reset();
  }

  /** 테스트 전용 */
  clear(): void {
    this.samples = [];
    this.wsClose.clear();
    this.loginFailures = [];
    this.ticks.clear();
    this.wsHandshakeRejected = 0;
    this.presenceDeviceLimitHits = 0;
    this.presenceRequired = 0;
    this.sweepBuyLimitHits = 0;
    this.withdrawalRequested = 0;
  }
}

export const metrics = new Metrics();
