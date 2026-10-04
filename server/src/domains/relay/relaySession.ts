// 중계 연결 한 건(좌석 하나): 토큰 버킷, 두 줄 송신 큐(신뢰 FIFO / 비신뢰 최신 하나), 규칙 위반 카운터.
import type { WebSocket } from 'ws';
import { getConfig } from '../../config/env';
import { splitBatches, isReliableChannel, type Packet } from './relayFrames';
import { relayMetrics } from './relayMetrics';
import type { RoomKind } from './relayTicket';

export class TokenBucket {
  private tokens: number;
  private at: number;
  constructor(
    private rate: number,
    private burst: number,
    now = Date.now(),
  ) {
    this.tokens = burst;
    this.at = now;
  }
  reconfigure(rate: number, burst: number): void {
    this.rate = rate;
    this.burst = burst;
    this.tokens = Math.min(this.tokens, burst);
  }
  /** n개를 쓸 수 있으면 쓰고 true */
  take(n: number, now = Date.now()): boolean {
    this.tokens = Math.min(this.burst, this.tokens + ((now - this.at) / 1000) * this.rate);
    this.at = now;
    if (this.tokens < n) return false;
    this.tokens -= n;
    return true;
  }
  /** 모자라도 빼고(음수 허용) 남은 양을 돌려준다(방 송신 한도처럼 신뢰 줄은 보내야 하는 경우) */
  force(n: number, now = Date.now()): number {
    this.tokens = Math.min(this.burst, this.tokens + ((now - this.at) / 1000) * this.rate);
    this.at = now;
    this.tokens -= n;
    return this.tokens;
  }
  available(now = Date.now()): number {
    return Math.min(this.burst, this.tokens + ((now - this.at) / 1000) * this.rate);
  }
}

interface Queued {
  pkt: Packet;
  bytes: number;
  at: number;
}

export class RelaySession {
  closed = false;
  lastRecvAt = Date.now();
  lastPingAt = 0;
  pingSentAt = 0;
  readonly connectedAt = Date.now();
  bytesIn = 0;
  bytesOut = 0;
  framesIn = 0;
  framesOut = 0;
  dropped = 0;
  private reliable: Queued[] = [];
  private reliableBytes = 0;
  private latest = new Map<string, Packet>();
  private pkt: TokenBucket;
  private bytes: TokenBucket;
  private strikes: number[] = [];
  private bad: number[] = [];
  /** 서버가 끊은 사유(close 이벤트에서 peer.left 이유를 정한다) */
  cause: 'closed' | 'timeout' | 'replaced' | 'removed' = 'closed';
  closeCode = 1006;

  constructor(
    readonly ws: WebSocket,
    readonly ip: string,
    readonly accountId: number,
    readonly accountUuid: string,
    readonly characterId: number,
    readonly characterUuid: string,
    readonly kind: RoomKind,
    readonly roomUuid: string,
    readonly seat: number,
    readonly wire: number,
    isHost: boolean,
  ) {
    const c = getConfig().relay;
    this.pkt = new TokenBucket(isHost ? c.hostPktPerSec : c.memberPktPerSec, (isHost ? c.hostPktPerSec : c.memberPktPerSec) * 2);
    this.bytes = new TokenBucket(isHost ? c.hostBytesPerSec : c.memberBytesPerSec, (isHost ? c.hostBytesPerSec : c.memberBytesPerSec) * 4);
  }

  /** 호스트 인계로 역할이 바뀌었다 */
  setRole(isHost: boolean): void {
    const c = getConfig().relay;
    const p = isHost ? c.hostPktPerSec : c.memberPktPerSec;
    const b = isHost ? c.hostBytesPerSec : c.memberBytesPerSec;
    this.pkt.reconfigure(p, p * 2);
    this.bytes.reconfigure(b, b * 4);
  }

  /** 받은 패킷 하나가 속도 한도 안인가 */
  allow(payloadBytes: number): boolean {
    const now = Date.now();
    return this.pkt.take(1, now) && this.bytes.take(payloadBytes + 4, now);
  }

  /** 규칙 위반 한 번. 분당 한도를 넘으면 true */
  strike(limitPerMin: number): boolean {
    const now = Date.now();
    this.strikes = this.strikes.filter((t) => now - t < 60_000);
    this.strikes.push(now);
    return this.strikes.length > limitPerMin;
  }

  /** 형식이 틀린 프레임 한 번. 10초 안에 3번이면 true */
  badFrame(): boolean {
    const now = Date.now();
    this.bad = this.bad.filter((t) => now - t < 10_000);
    this.bad.push(now);
    return this.bad.length >= 3;
  }

  sendText(frame: Record<string, unknown>): void {
    if (this.closed || this.ws.readyState !== this.ws.OPEN) return;
    this.ws.send(JSON.stringify(frame));
  }

  sendBinary(buf: Buffer): void {
    if (this.closed || this.ws.readyState !== this.ws.OPEN) return;
    this.bytesOut += buf.length;
    this.framesOut++;
    relayMetrics.bytesOut.add(buf.length);
    relayMetrics.framesOut.add(1);
    this.ws.send(buf, { binary: true });
  }

  /** 서버가 끊는다: bye를 먼저 보낸다 */
  bye(code: number, reason: string, reconnect: boolean, retryAfterMs?: number): void {
    if (this.closed) return;
    this.closed = true;
    this.closeCode = code;
    try {
      if (this.ws.readyState === this.ws.OPEN) {
        const f: Record<string, unknown> = { t: 'bye', code, reason, reconnect };
        if (retryAfterMs !== undefined) f.retry_after_ms = retryAfterMs;
        this.ws.send(JSON.stringify(f));
        this.ws.close(code, reason.slice(0, 100));
      } else this.ws.terminate();
    } catch {
      this.ws.terminate();
    }
  }

  /** 큐에 넣는다. 신뢰 줄이 한도를 넘으면 false(느린 소비자) */
  enqueue(p: Packet): boolean {
    const cfg = getConfig().relay;
    if (isReliableChannel(p.channel)) {
      const bytes = p.payload.length + 4;
      this.reliable.push({ pkt: p, bytes, at: Date.now() });
      this.reliableBytes += bytes;
      return this.reliableBytes <= cfg.reliableQueueBytes;
    }
    const key = `${p.addr}:${p.channel}`;
    if (this.latest.has(key)) {
      this.dropped++;
      relayMetrics.droppedUnreliable.add(1);
    }
    this.latest.set(key, p);
    return true;
  }

  /** 가장 오래된 신뢰 항목이 한도보다 오래됐는가 */
  reliableTooOld(now: number): boolean {
    const first = this.reliable[0];
    return first !== undefined && now - first.at > getConfig().relay.reliableQueueAgeMs;
  }

  hasPending(): boolean {
    return this.reliable.length > 0 || this.latest.size > 0;
  }

  /** 틱마다: 신뢰 줄을 먼저, 이어서(여유가 있으면) 비신뢰 줄의 최신 값을 BATCH로 보낸다. 보낸 바이트를 돌려준다 */
  flush(allowUnreliable: boolean): number {
    const cfg = getConfig().relay;
    const packets: Packet[] = this.reliable.map((q) => q.pkt);
    this.reliable = [];
    this.reliableBytes = 0;
    // 느린 연결·방 송신 한도 초과: 비신뢰 줄은 보류한다(다음 틱에 더 새로운 값이 덮어쓴다)
    if (this.latest.size > 0 && allowUnreliable && this.ws.bufferedAmount <= cfg.backpressureBytes) {
      packets.push(...this.latest.values());
      this.latest.clear();
    }
    let sent = 0;
    for (const buf of splitBatches(packets, cfg.frameMaxBytes)) {
      this.sendBinary(buf);
      sent += buf.length;
    }
    return sent;
  }
}
