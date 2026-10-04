// 중계 방 하나(최대 4좌석): 라우팅(헤더만 본다), 호스트 상태, 방 송신 한도, 사용 요약. 페이로드는 해석하지 않는다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { logger } from '../../utils/logger';
import { insertAnomaly } from '../economy/economyRepository';
import { routePacket, type Packet } from './relayFrames';
import { relayMetrics } from './relayMetrics';
import type { RoomSnapshot } from './relayRepository';
import { insertRoomStats } from './relayRepository';
import { TokenBucket, type RelaySession } from './relaySession';
import type { RoomKind } from './relayTicket';

export const CLOSE_CODE = {
  REPLACED: 4001,
  BANNED: 4003,
  TOKEN_INVALID: 4004,
  BAD_PROTOCOL: 4005,
  FLOOD: 4006,
  HELLO_TIMEOUT: 4007,
  SLOW_CONSUMER: 4008,
  MEMBER_REMOVED: 4011,
  ROOM_CLOSED: 4012,
  PEER_TIMEOUT: 4013,
  TRANSPORT_CHANGED: 4014,
  WIRE_MISMATCH: 4015,
  SERVER_BUSY: 4016,
  CLIENT_OUTDATED: 4426,
} as const;

/** 인계 직후 옛 호스트의 프레임을 위반으로 세지 않는 시간 */
const STALE_HOST_MS = 10_000;

export class RelayRoom {
  readonly conns = new Map<number, RelaySession>();
  hostSeat: number | null;
  hostEpoch: number;
  readonly transportEpoch: number;
  wire: number | null = null;
  staleHostSeat: number | null = null;
  private staleUntil = 0;
  readonly startedAt = new Date();
  peak = 0;
  hostGraceTimer: NodeJS.Timeout | null = null;
  /** 연결이 모두 사라진 시각(0이면 연결이 있다) */
  emptyAt = 0;
  closed = false;
  private egress: TokenBucket;
  private egressOverSince = 0;
  private stats = { bytesIn: 0, bytesOut: 0, framesIn: 0, framesOut: 0, dropped: 0, reconnects: 0 };
  private closeCodes: Record<string, number> = {};
  private rtt: number[] = [];

  constructor(
    readonly kind: RoomKind,
    readonly uuid: string,
    snap: RoomSnapshot,
  ) {
    this.hostSeat = snap.hostSeat;
    this.hostEpoch = snap.hostEpoch;
    this.transportEpoch = snap.transportEpoch;
    const bps = getConfig().relay.roomEgressBps;
    this.egress = new TokenBucket(bps, bps * 2);
  }

  get key(): string {
    return `${this.kind}:${this.uuid}`;
  }

  connectedSeats(): number[] {
    return [...this.conns.keys()];
  }

  broadcastText(frame: Record<string, unknown>, exceptSeat: number | null = null): void {
    for (const [seat, s] of this.conns) if (seat !== exceptSeat) s.sendText(frame);
  }

  noteClose(code: number): void {
    this.closeCodes[String(code)] = (this.closeCodes[String(code)] ?? 0) + 1;
  }

  noteReconnect(): void {
    this.stats.reconnects++;
    relayMetrics.reconnects.add();
  }

  /** 연결이 사라질 때 그 연결의 카운터를 방 요약에 더한다 */
  absorb(s: RelaySession): void {
    this.stats.bytesIn += s.bytesIn;
    this.stats.bytesOut += s.bytesOut;
    this.stats.framesOut += s.framesOut;
    this.stats.dropped += s.dropped;
    s.bytesIn = s.bytesOut = s.dropped = s.framesOut = 0;
  }

  noteRtt(ms: number): void {
    this.rtt.push(ms);
    if (this.rtt.length > 200) this.rtt.shift();
    relayMetrics.recordRtt(ms);
  }

  add(s: RelaySession): void {
    this.conns.set(s.seat, s);
    if (s.seat === this.hostSeat) this.hostLostAt = 0;
    this.peak = Math.max(this.peak, this.conns.size);
    this.emptyAt = 0;
    if (s.seat === this.hostSeat && this.hostGraceTimer) {
      clearTimeout(this.hostGraceTimer);
      this.hostGraceTimer = null;
    }
  }

  /** 호스트를 바꾼다. 세대가 커졌을 때만. 모든 연결에 host.changed를 보낸다 */
  setHost(seat: number | null, epoch: number): boolean {
    if (epoch <= this.hostEpoch) return false;
    if (this.hostSeat !== null && this.hostSeat !== seat) {
      this.staleHostSeat = this.hostSeat;
      this.staleUntil = Date.now() + STALE_HOST_MS;
    }
    this.hostSeat = seat;
    this.hostEpoch = epoch;
    if (this.hostGraceTimer) {
      clearTimeout(this.hostGraceTimer);
      this.hostGraceTimer = null;
    }
    for (const [s, c] of this.conns) c.setRole(s === seat);
    this.broadcastText({ t: 'host.changed', seat, epoch });
    relayMetrics.hostChanges.add();
    return true;
  }

  hostPresence(): { connected: boolean; absentMs: number } {
    if (this.hostSeat !== null && this.conns.has(this.hostSeat)) return { connected: true, absentMs: 0 };
    return { connected: false, absentMs: Date.now() - (this.hostLostAt || this.startedAt.getTime()) };
  }

  /** 호스트 연결이 사라진 시각(0이면 아직 연결이 없던 적이 없거나 연결 중) */
  hostLostAt = 0;

  /** 받은 BATCH의 패킷들을 라우팅한다. 위반이면 연결을 끊을 수 있다 */
  route(from: RelaySession, packets: Packet[]): void {
    const staleHost = Date.now() < this.staleUntil ? this.staleHostSeat : null;
    for (const p of packets) {
      this.stats.framesIn++;
      if (!from.allow(p.payload.length)) {
        if (this.strike(from, 'rate')) return;
        continue;
      }
      const r = routePacket(from.seat, p, { hostSeat: this.hostSeat, staleHostSeat: staleHost, connected: this.connectedSeats() });
      if ('violation' in r) {
        if (this.strike(from, r.violation)) return;
        continue;
      }
      if ('drop' in r) continue;
      for (const to of r.to) {
        const target = this.conns.get(to);
        if (!target) continue;
        // 서버가 보낸 사람을 도장 찍는다(addr = 보낸 좌석)
        if (!target.enqueue({ channel: p.channel, addr: from.seat, payload: p.payload })) {
          target.bye(CLOSE_CODE.SLOW_CONSUMER, 'SLOW_CONSUMER', true);
        }
      }
    }
  }

  /** 규칙 위반 한 번. 분당 한도를 넘으면 FLOOD로 끊고 true */
  strike(s: RelaySession, why: string): boolean {
    if (!s.strike(getConfig().relay.strikesPerMin)) return false;
    relayMetrics.abuse.add();
    insertAnomaly(getPool(), s.accountId, s.characterId, 'relay_abuse', 2, { room_kind: this.kind, room: this.uuid, seat: s.seat, why }).catch((err: unknown) =>
      logger.error({ err }, 'relay_abuse anomaly insert failed'),
    );
    s.bye(CLOSE_CODE.FLOOD, 'FLOOD', false);
    return true;
  }

  /** 플러시 틱: 연결마다 큐를 BATCH로 보낸다. 방 송신 한도를 넘으면 비신뢰 줄부터 보류하고 지속되면 닫는다 */
  flush(now: number): void {
    const cfg = getConfig().relay;
    for (const s of this.conns.values()) {
      if (s.closed || !s.hasPending()) continue;
      if (s.reliableTooOld(now)) {
        s.bye(CLOSE_CODE.SLOW_CONSUMER, 'SLOW_CONSUMER', true);
        continue;
      }
      const ok = this.egress.available(now) > 0;
      const sent = s.flush(ok);
      if (sent > 0) {
        const left = this.egress.force(sent, now);
        if (left < 0) {
          if (this.egressOverSince === 0) this.egressOverSince = now;
        } else this.egressOverSince = 0;
      }
    }
    // 방 전체 송신이 3초 넘게 한도 밖이면 버그·남용으로 보고 닫는다
    if (this.egressOverSince !== 0 && now - this.egressOverSince > 3000 && this.egress.available(now) < -cfg.roomEgressBps) {
      relayMetrics.abuse.add();
      for (const s of this.conns.values()) s.bye(CLOSE_CODE.FLOOD, 'FLOOD', false);
    }
  }

  /** 방을 닫고(연결에는 호출 쪽이 bye를 보냈다) 사용 요약을 쓴다 */
  async finish(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    if (this.hostGraceTimer) clearTimeout(this.hostGraceTimer);
    for (const s of this.conns.values()) this.absorb(s);
    if (this.peak === 0) return;
    const sorted = [...this.rtt].sort((a, b) => a - b);
    const at = (p: number): number | null => (sorted.length === 0 ? null : (sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] as number));
    try {
      await insertRoomStats(getPool(), {
        kind: this.kind,
        ref: this.uuid,
        startedAt: this.startedAt,
        endedAt: new Date(),
        peakPeers: this.peak,
        bytesIn: this.stats.bytesIn,
        bytesOut: this.stats.bytesOut,
        framesIn: this.stats.framesIn,
        framesOut: this.stats.framesOut,
        droppedUnreliable: this.stats.dropped,
        reconnects: this.stats.reconnects,
        rttP50: at(50),
        rttP95: at(95),
        closeCodes: this.closeCodes,
      });
    } catch (err) {
      logger.error({ err }, 'relay_room_stats insert failed');
    }
    logger.info({ room: this.key, peak: this.peak, close_codes: this.closeCodes, bytes_out: this.stats.bytesOut, frames_out: this.stats.framesOut }, 'relay.room_closed');
  }
}
