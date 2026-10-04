// 중계 데이터 프레임 코덱(순수 함수, phase8_api.md 4.4). 정수는 리틀 엔디언(C# BinaryWriter와 같다).
// BATCH: [0x04][count u8] 뒤에 count개의 [channel u8][addr u8][len u16 LE][payload]. PING [0x02][nonce u32], PONG [0x03][nonce u32].
export const FRAME = { PING: 0x02, PONG: 0x03, BATCH: 0x04 } as const;

/** NetChannel(Transport.cs)과 같은 번호 */
export const CH = { INPUT: 1, CHAT: 2, PRESENCE: 3, SNAPSHOT: 4, EVENT: 5, CONTROL: 6, MEMBER_STATE: 7 } as const;

/** 방 전체(호스트만 쓸 수 있는 주소) */
export const ADDR_ALL = 0xff;

export interface Packet {
  channel: number;
  /** C->S 에서는 받을 좌석(또는 0xFF), S->C 에서는 보낸 좌석(서버가 도장) */
  addr: number;
  payload: Buffer;
}

export type Decoded =
  | { kind: 'ping'; nonce: number }
  | { kind: 'pong'; nonce: number }
  | { kind: 'batch'; packets: Packet[] }
  | { kind: 'error'; reason: 'empty' | 'type' | 'length' | 'packet_too_big' | 'count' };

export function decodeFrame(buf: Buffer, packetMax: number): Decoded {
  if (buf.length === 0) return { kind: 'error', reason: 'empty' };
  const type = buf[0];
  if (type === FRAME.PING || type === FRAME.PONG) {
    if (buf.length !== 5) return { kind: 'error', reason: 'length' };
    return { kind: type === FRAME.PING ? 'ping' : 'pong', nonce: buf.readUInt32LE(1) };
  }
  if (type !== FRAME.BATCH) return { kind: 'error', reason: 'type' };
  if (buf.length < 2) return { kind: 'error', reason: 'length' };
  const count = buf[1] as number;
  if (count === 0) return { kind: 'error', reason: 'count' };
  const packets: Packet[] = [];
  let off = 2;
  for (let i = 0; i < count; i++) {
    if (off + 4 > buf.length) return { kind: 'error', reason: 'length' };
    const channel = buf[off] as number;
    const addr = buf[off + 1] as number;
    const len = buf.readUInt16LE(off + 2);
    off += 4;
    if (len > packetMax) return { kind: 'error', reason: 'packet_too_big' };
    if (off + len > buf.length) return { kind: 'error', reason: 'length' };
    packets.push({ channel, addr, payload: buf.subarray(off, off + len) });
    off += len;
  }
  if (off !== buf.length) return { kind: 'error', reason: 'length' };
  return { kind: 'batch', packets };
}

export const PACKET_HEADER_BYTES = 4;

/** 패킷 목록을 BATCH 하나로(호출 쪽이 count <= 255, 총 길이 한도를 지킨다) */
export function encodeBatch(packets: Packet[]): Buffer {
  let size = 2;
  for (const p of packets) size += PACKET_HEADER_BYTES + p.payload.length;
  const out = Buffer.allocUnsafe(size);
  out[0] = FRAME.BATCH;
  out[1] = packets.length;
  let off = 2;
  for (const p of packets) {
    out[off] = p.channel;
    out[off + 1] = p.addr;
    out.writeUInt16LE(p.payload.length, off + 2);
    p.payload.copy(out, off + 4);
    off += 4 + p.payload.length;
  }
  return out;
}

export function encodePong(nonce: number): Buffer {
  const b = Buffer.allocUnsafe(5);
  b[0] = FRAME.PONG;
  b.writeUInt32LE(nonce >>> 0, 1);
  return b;
}

export function encodePing(nonce: number): Buffer {
  const b = encodePong(nonce);
  b[0] = FRAME.PING;
  return b;
}

/** 패킷들을 한도 안에서 여러 BATCH 프레임으로 나눈다 */
export function splitBatches(packets: Packet[], frameMax: number): Buffer[] {
  const out: Buffer[] = [];
  let cur: Packet[] = [];
  let size = 2;
  for (const p of packets) {
    const add = PACKET_HEADER_BYTES + p.payload.length;
    if (cur.length > 0 && (size + add > frameMax || cur.length >= 255)) {
      out.push(encodeBatch(cur));
      cur = [];
      size = 2;
    }
    cur.push(p);
    size += add;
  }
  if (cur.length > 0) out.push(encodeBatch(cur));
  return out;
}

/** 채널별 신뢰 등급: Input, Event, Control은 신뢰(순서 유지), Snapshot, MemberState는 비신뢰(최신 우선) */
export const isReliableChannel = (ch: number): boolean => ch === CH.INPUT || ch === CH.EVENT || ch === CH.CONTROL;

export type RouteResult =
  | { to: number[] }
  | { drop: 'no_host' | 'target_absent' | 'stale_host' }
  | { violation: 'channel' | 'addr' | 'host_only' | 'member_to_member' | 'broadcast' };

export interface RouteCtx {
  hostSeat: number | null;
  /** 직전 호스트 좌석(인계 직후의 정상 지연을 위반으로 세지 않는다) */
  staleHostSeat: number | null;
  connected: number[];
}

/** 라우팅 규칙(헤더만 본다, 4.5절). 별 모양 강제와 호스트 전용 채널 */
export function routePacket(from: number, p: Packet, c: RouteCtx): RouteResult {
  const ch = p.channel;
  if (ch !== CH.INPUT && ch !== CH.SNAPSHOT && ch !== CH.EVENT && ch !== CH.CONTROL && ch !== CH.MEMBER_STATE) {
    return { violation: 'channel' };
  }
  const isHostChannel = ch === CH.SNAPSHOT;
  const memberToHostOnly = ch === CH.INPUT || ch === CH.MEMBER_STATE;
  if (c.hostSeat !== null && from === c.hostSeat) {
    if (memberToHostOnly) return { violation: 'host_only' };
    if (p.addr === ADDR_ALL) {
      const to = c.connected.filter((s) => s !== from);
      return { to };
    }
    if (p.addr > 3) return { violation: 'addr' };
    if (p.addr === from) return { violation: 'addr' };
    return c.connected.includes(p.addr) ? { to: [p.addr] } : { drop: 'target_absent' };
  }
  // 멤버(또는 호스트가 없는 방의 좌석)
  if (c.staleHostSeat !== null && from === c.staleHostSeat && (isHostChannel || ch === CH.EVENT || ch === CH.CONTROL) && p.addr !== c.hostSeat) {
    return { drop: 'stale_host' };
  }
  if (isHostChannel) {
    return from === c.staleHostSeat ? { drop: 'stale_host' } : { violation: 'host_only' };
  }
  if (p.addr === ADDR_ALL) return { violation: 'broadcast' };
  if (p.addr > 3) return { violation: 'addr' };
  if (c.hostSeat === null) return { drop: 'no_host' };
  if (p.addr !== c.hostSeat) {
    if (c.staleHostSeat !== null && p.addr === c.staleHostSeat) return { drop: 'stale_host' };
    return { violation: 'member_to_member' };
  }
  return c.connected.includes(c.hostSeat) ? { to: [c.hostSeat] } : { drop: 'target_absent' };
}
