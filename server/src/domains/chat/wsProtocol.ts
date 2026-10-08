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

export const helloFrame = z.strictObject({
  t: z.literal('hello'),
  v: z.literal(PROTOCOL_VERSION),
  token: z.string().min(1).max(2048),
  client_version: z.string().regex(/^\d+\.\d+\.\d+$/),
  character_id: uuid,
  since: z.number().int().min(0).nullable().optional(),
  /** 8단계: 클라이언트 능력(모르는 서버는 무시한다) */
  caps: z.strictObject({ steam_p2p: z.boolean().optional() }).optional(),
});

/** 마을에서 내 모습과 위치(같은 마을·같은 채널 사람에게 중계). 전투·보상과 무관한 표시용 */
export const townPosFrame = z.strictObject({
  t: z.literal('town.pos'),
  map_id: z.string().min(1).max(40),
  x: z.number().finite().min(-50).max(500),
  y: z.number().finite().min(-50).max(500),
  f: z.number().int().min(0).max(7),
  m: z.boolean(),
  cls: z.number().int().min(0).max(15),
  career: z.number().int().min(0).max(31).optional(),
  skin: z.string().max(40).optional(),
  weapon: z.string().max(60).optional(),
  level: z.number().int().min(1).max(999).optional(),
});

export const chatSendFrame = z.strictObject({
  t: z.literal('chat.send'),
  cid: uuid,
  channel: z.string().max(20),
  text: z.string().min(1).max(400),
  to: uuid.optional(),
  to_name: z.string().min(1).max(16).optional(),
});

export const clientFrame = z.discriminatedUnion('t', [
  helloFrame,
  z.strictObject({ t: z.literal('auth'), token: z.string().min(1).max(2048) }),
  z.strictObject({ t: z.literal('ping'), n: z.number().int().optional() }),
  chatSendFrame,
  z.strictObject({ t: z.literal('presence.set'), map_id: z.string().min(1).max(40) }),
  townPosFrame,
]);

export type ClientFrame = z.infer<typeof clientFrame>;
export type ChatSendFrame = z.infer<typeof chatSendFrame>;
export type TownPosFrame = z.infer<typeof townPosFrame>;
