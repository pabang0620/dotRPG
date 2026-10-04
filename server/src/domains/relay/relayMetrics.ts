// 중계 지표(프로세스 메모리, 재시작하면 0). ops.snapshot의 relay 그룹과 알림 규칙이 읽는다(phase8_api.md 4.12).
interface Entry {
  t: number;
  n: number;
}

class Rolling {
  private list: Entry[] = [];
  constructor(private readonly keepMs = 24 * 3_600_000) {}
  add(n = 1, now = Date.now()): void {
    const sec = Math.floor(now / 1000) * 1000;
    const last = this.list[this.list.length - 1];
    if (last && last.t === sec) last.n += n;
    else this.list.push({ t: sec, n });
    if (this.list.length > 90_000) this.prune(now);
  }
  private prune(now: number): void {
    const i = this.list.findIndex((e) => now - e.t <= this.keepMs);
    this.list = i < 0 ? [] : this.list.slice(i);
  }
  sum(windowMs: number, now = Date.now()): number {
    let s = 0;
    for (let i = this.list.length - 1; i >= 0; i--) {
      const e = this.list[i] as Entry;
      if (now - e.t > windowMs) break;
      s += e.n;
    }
    return s;
  }
  clear(): void {
    this.list = [];
  }
}

const pct = (sorted: number[], p: number): number => (sorted.length === 0 ? 0 : (sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] as number));

class RelayMetrics {
  readonly bytesIn = new Rolling();
  readonly bytesOut = new Rolling();
  readonly framesIn = new Rolling();
  readonly framesOut = new Rolling();
  readonly droppedUnreliable = new Rolling();
  readonly reconnects = new Rolling();
  readonly abuse = new Rolling();
  readonly fallbacks = new Rolling();
  readonly steamToRelay = new Rolling();
  readonly hostChanges = new Rolling();
  readonly closes = new Map<number, Rolling>();
  readonly ticketRejects = new Map<string, Rolling>();
  /** 방 RTT 표본(ms, 시각) */
  private rtt: { t: number; ms: number }[] = [];
  private flushLag: { t: number; ms: number }[] = [];
  /** RELAY_UNAVAILABLE 응답이 이어진 시작 시각(0이면 없음) */
  unavailableSince = 0;
  private lastUnavailableAt = 0;
  private rttHighSince = 0;

  close(code: number): void {
    const r = this.closes.get(code) ?? new Rolling();
    r.add();
    this.closes.set(code, r);
  }

  ticketReject(reason: string): void {
    const r = this.ticketRejects.get(reason) ?? new Rolling();
    r.add();
    this.ticketRejects.set(reason, r);
  }

  recordRtt(ms: number): void {
    const now = Date.now();
    this.rtt.push({ t: now, ms });
    if (this.rtt.length > 2000) this.rtt = this.rtt.filter((x) => now - x.t < 120_000);
  }

  recordFlushLag(ms: number): void {
    const now = Date.now();
    this.flushLag.push({ t: now, ms });
    if (this.flushLag.length > 3000) this.flushLag = this.flushLag.filter((x) => now - x.t < 120_000);
  }

  /** 티켓 발급이 503으로 거절됐다/성공했다(연속 구간 추적) */
  unavailable(flag: boolean): void {
    const now = Date.now();
    if (flag) {
      if (this.unavailableSince === 0 || now - this.lastUnavailableAt > 60_000) this.unavailableSince = now;
      this.lastUnavailableAt = now;
    } else {
      this.unavailableSince = 0;
    }
  }

  rttPercentiles(windowMs = 60_000): { p50: number; p95: number; samples: number } {
    const now = Date.now();
    const v = this.rtt.filter((x) => now - x.t < windowMs).map((x) => x.ms).sort((a, b) => a - b);
    const p95 = pct(v, 95);
    if (p95 > 250) {
      if (this.rttHighSince === 0) this.rttHighSince = now;
    } else if (v.length > 0) this.rttHighSince = 0;
    return { p50: pct(v, 50), p95, samples: v.length };
  }

  rttHighForMs(): number {
    return this.rttHighSince === 0 ? 0 : Date.now() - this.rttHighSince;
  }

  flushLagP99(windowMs = 60_000): number {
    const now = Date.now();
    return pct(this.flushLag.filter((x) => now - x.t < windowMs).map((x) => x.ms).sort((a, b) => a - b), 99);
  }

  closesSince(windowMs: number): Record<string, number> {
    const out: Record<string, number> = {};
    for (const [code, r] of this.closes) {
      const n = r.sum(windowMs);
      if (n > 0) out[String(code)] = n;
    }
    return out;
  }

  rejectsSince(windowMs: number): Record<string, number> {
    const out: Record<string, number> = {};
    for (const [k, r] of this.ticketRejects) {
      const n = r.sum(windowMs);
      if (n > 0) out[k] = n;
    }
    return out;
  }

  /** 테스트 전용 */
  clear(): void {
    for (const r of [this.bytesIn, this.bytesOut, this.framesIn, this.framesOut, this.droppedUnreliable, this.reconnects, this.abuse, this.fallbacks, this.steamToRelay, this.hostChanges]) r.clear();
    this.closes.clear();
    this.ticketRejects.clear();
    this.rtt = [];
    this.flushLag = [];
    this.unavailableSince = 0;
    this.rttHighSince = 0;
  }
}

export const relayMetrics = new RelayMetrics();
