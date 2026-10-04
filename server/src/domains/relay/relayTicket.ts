// 중계 입장 티켓(phase8_api.md 4.3): HS256, 방·좌석·전송 세대에 묶이고 단일 사용.
import { randomUUID } from 'node:crypto';
import jwt from 'jsonwebtoken';
import { getConfig } from '../../config/env';

export type RoomKind = 'run' | 'field';
export const TICKET_AUD = 'dotrpg-relay';

export interface TicketClaims {
  jti: string;
  /** 계정 uuid */
  sub: string;
  /** 캐릭터 uuid */
  cid: string;
  k: RoomKind;
  /** 방 uuid */
  rid: string;
  seat: number;
  /** 전송 세대 */
  ep: number;
  iat: number;
  exp: number;
}

export function signTicket(c: { accountUuid: string; characterUuid: string; kind: RoomKind; roomUuid: string; seat: number; epoch: number }): string {
  const cfg = getConfig().relay;
  return jwt.sign({ v: 1, cid: c.characterUuid, k: c.kind, rid: c.roomUuid, seat: c.seat, ep: c.epoch }, cfg.ticketSecret, {
    algorithm: 'HS256',
    audience: TICKET_AUD,
    subject: c.accountUuid,
    jwtid: randomUUID(),
    expiresIn: cfg.ticketTtlSeconds,
  });
}

export type TicketError = 'expired' | 'invalid';

export function verifyTicket(token: string): { ok: true; claims: TicketClaims } | { ok: false; reason: TicketError } {
  try {
    const p = jwt.verify(token, getConfig().relay.ticketSecret, { algorithms: ['HS256'], audience: TICKET_AUD }) as Partial<TicketClaims> & { v?: number };
    if (
      p.v !== 1 || typeof p.jti !== 'string' || typeof p.sub !== 'string' || typeof p.cid !== 'string' ||
      (p.k !== 'run' && p.k !== 'field') || typeof p.rid !== 'string' || typeof p.seat !== 'number' || typeof p.ep !== 'number'
    ) {
      return { ok: false, reason: 'invalid' };
    }
    return { ok: true, claims: p as TicketClaims };
  } catch (err) {
    return { ok: false, reason: err instanceof jwt.TokenExpiredError ? 'expired' : 'invalid' };
  }
}

/** 단일 사용 저장소(메모리 2분). 나중에 분리된 중계 프로세스는 같은 인터페이스를 쓴다 */
export interface RelayTicketStore {
  /** 처음 보는 jti면 기록하고 true */
  consume(jti: string, nowMs: number): boolean;
}


export class MemoryTicketStore implements RelayTicketStore {
  private seen = new Map<string, number>();
  consume(jti: string, nowMs: number): boolean {
    // 보관은 티켓 수명 + 여유(수명이 끝난 티켓은 서명 검증에서 어차피 거절된다)
    const keep = getConfig().relay.ticketTtlSeconds * 1000 + 60_000;
    for (const [k, at] of this.seen) {
      if (nowMs - at > keep) this.seen.delete(k);
      else break; // Map은 삽입 순서라 앞이 가장 오래됐다
    }
    if (this.seen.has(jti)) return false;
    this.seen.set(jti, nowMs);
    return true;
  }
}

let store: RelayTicketStore = new MemoryTicketStore();
export const getTicketStore = (): RelayTicketStore => store;
export const setTicketStore = (s: RelayTicketStore): void => {
  store = s;
};
