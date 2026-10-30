// WebSocket 프로토콜(phase5_api.md 3절): 클라이언트 프레임 스키마, close 코드, 채팅 오류 코드
import { z } from 'zod';

export const PROTOCOL_VERSION = 1;

export const CLOSE = {
  GOING_AWAY: 1001,
  TOO_BIG: 1009,
  REPLACED: 4001,
  TOKEN_EXPIRED: 4002,
  BANNED: 4003,
  TOKEN_INVALID: 4004,
  BAD_PROTOCOL: 4005,
  FLOOD: 4006,
  HELLO_TIMEOUT: 4007,
  SLOW_CONSUMER: 4008,
  IDLE: 4009,
  CHARACTER_INVALID: 4010,
  KICKED: 4011,
  /** 탈퇴 요청으로 끊김(재연결하지 않는다) */
  WITHDRAWN: 4012,
  CLIENT_OUTDATED: 4426,
} as const;

export type ChatErrCode =
  | 'TEXT_EMPTY'
  | 'TEXT_TOO_LONG'
  | 'RATE_LIMITED'
  | 'MUTED_REPEAT'
  | 'MUTED_SANCTION'
  | 'MESSAGE_BLOCKED'
  | 'CHANNEL_INVALID'
  | 'NOT_IN_PARTY'
  | 'NOT_IN_GUILD'
  | 'TARGET_REQUIRED'
  | 'TARGET_NOT_FOUND'
  | 'TARGET_OFFLINE'
  | 'TARGET_BLOCKED'
  | 'SELF_WHISPER'
  | 'WHISPER_TARGETS_LIMIT'
  | 'CHAT_UNAVAILABLE';

export interface Frame {
  t: string;
  [key: string]: unknown;
}

const uuid = z.uuid();

// [NET 2026-10-30] 프레임 스키마는 모르는 필드를 지우고 받는다(z.object). 새 클라이언트가 선택 필드를 하나 더 보내도
// BAD_FRAME 3번(10초) = BAD_PROTOCOL로 채팅·마을 연결이 영구히 끊기지 않게 한다. 아는 필드의 값 검사는 그대로다.
/** 서버가 아는 프레임 종류. 모르는 종류(더 새 클라이언트)는 오류만 알리고 위반으로 세지 않는다 */
export const KNOWN_FRAME_TYPES: ReadonlySet<string> = new Set(['hello', 'auth', 'ping', 'chat.send', 'presence.set', 'town.pos']);

export const helloFrame = z.object({
  t: z.literal('hello'),
  v: z.literal(PROTOCOL_VERSION),
  token: z.string().min(1).max(2048),
  client_version: z.string().regex(/^\d+\.\d+\.\d+$/),
  character_id: uuid,
  since: z.number().int().min(0).nullable().optional(),
  /** 8단계: 클라이언트 능력(모르는 서버는 무시한다) */
  caps: z.object({ steam_p2p: z.boolean().optional() }).optional(),
});

/** 마을에서 내 모습과 위치(같은 마을·같은 채널 사람에게 중계). 전투·보상과 무관한 표시용 */
export const townPosFrame = z.object({
  t: z.literal('town.pos'),
  map_id: z.string().min(1).max(40),
  x: z.number().finite().min(-50).max(500),
  y: z.number().finite().min(-50).max(500),
  f: z.number().int().min(0).max(7),
  m: z.boolean(),
  cls: z.number().int().min(0).max(15),
  career: z.number().int().min(0).max(31).optional(),
  skin: z.string().max(40).optional(),
  /** 착용 오라(고리·발자국 표시용). 소유한 것만 중계한다 */
  aura: z.string().max(40).optional(),
  weapon: z.string().max(60).optional(),
  level: z.number().int().min(1).max(999).optional(),
  /** 공격·스킬 횟수(16비트로 돈다). 바뀌면 다른 사람 화면에서 그 동작을 한 번 재생한다. 표시용 */
  a: z.number().int().min(0).max(65535).optional(),
  /** 마지막 동작: -1 기본 공격, 0 이상 스킬 슬롯 */
  k: z.number().int().min(-1).max(15).optional(),
});

export const chatSendFrame = z.object({
  t: z.literal('chat.send'),
  cid: uuid,
  channel: z.string().max(20),
  text: z.string().min(1).max(400),
  to: uuid.optional(),
  to_name: z.string().min(1).max(16).optional(),
});

export const clientFrame = z.discriminatedUnion('t', [
  helloFrame,
  z.object({ t: z.literal('auth'), token: z.string().min(1).max(2048) }),
  z.object({ t: z.literal('ping'), n: z.number().int().optional() }),
  chatSendFrame,
  z.object({ t: z.literal('presence.set'), map_id: z.string().min(1).max(40) }),
  townPosFrame,
]);

export type ClientFrame = z.infer<typeof clientFrame>;
export type ChatSendFrame = z.infer<typeof chatSendFrame>;
export type TownPosFrame = z.infer<typeof townPosFrame>;
