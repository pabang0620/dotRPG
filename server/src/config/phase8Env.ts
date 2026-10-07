// 8단계(전투 중계 + 필드 파티 사냥 + Steam) 환경변수. 값의 뜻은 Docs/server/phase8_api.md 11절.
import { createHash } from 'node:crypto';
import { z } from 'zod';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);

export const TRANSPORTS = ['relay', 'steam', 'dev'] as const;
export type TransportKind = (typeof TRANSPORTS)[number];

export const phase8Shape = {
  RELAY_ENABLED: boolStr.default(true),
  RELAY_MODE: z.enum(['embedded', 'standalone']).default('embedded'),
  RELAY_TICKET_SECRET: z.string().optional(),
  RELAY_PUBLIC_URL: z.string().optional(),
  RELAY_TICKET_TTL_SECONDS: z.coerce.number().int().min(1).max(120).default(60),
  RELAY_MAX_CONNECTIONS: posInt(400),
  RELAY_MAX_ROOMS: posInt(120),
  RELAY_ADMISSION_RATIO: z.coerce.number().gt(0).max(1).default(0.9),
  RELAY_HANDSHAKE_PER_MIN_IP: posInt(30),
  RELAY_UNAUTH_PER_IP: posInt(10),
  RELAY_CONN_PER_IP: posInt(8),
  RELAY_HELLO_TIMEOUT_MS: posInt(5000),
  RELAY_PING_EVERY_MS: posInt(2000),
  RELAY_IDLE_TIMEOUT_MS: posInt(6000),
  RELAY_FLUSH_MS: posInt(20),
  RELAY_FRAME_MAX_BYTES: posInt(8192),
  RELAY_PACKET_MAX_BYTES: posInt(6144),
  RELAY_HOST_PKT_PER_SEC: posInt(300),
  RELAY_MEMBER_PKT_PER_SEC: posInt(120),
  RELAY_HOST_BYTES_PER_SEC: posInt(131072),
  RELAY_MEMBER_BYTES_PER_SEC: posInt(32768),
  RELAY_ROOM_EGRESS_BPS: posInt(262144),
  RELAY_RELIABLE_QUEUE_BYTES: posInt(262144),
  RELAY_RELIABLE_QUEUE_AGE_MS: posInt(5000),
  RELAY_BACKPRESSURE_BYTES: posInt(32768),
  RELAY_STRIKES_PER_MIN: posInt(20),
  RELAY_HOST_GRACE_MS: z.coerce.number().int().min(0).default(3000),
  RELAY_ROOM_EMPTY_GRACE_SECONDS: posInt(30),
  RELAY_EGRESS_DAILY_BUDGET_GB: z.coerce.number().positive().optional(),
  COMBAT_TRANSPORT_ORDER: z.string().optional(),
  STEAM_P2P_ENABLED: boolStr.default(true),
  TRANSPORT_SWITCH_COOLDOWN_SECONDS: z.coerce.number().int().min(0).default(10),
  STEAM_WEB_API_BASE: z.string().url().default('https://api.steampowered.com'),
  STEAM_WEB_API_RETRIES: z.coerce.number().int().min(0).max(3).default(1),
  STEAM_BREAKER_FAILURES: posInt(5),
  STEAM_BREAKER_OPEN_SECONDS: posInt(30),
  DEPLOY_STAGE: z.enum(['test', 'live']).default('live'),
  FIELD_ELECTION_WINDOW_SECONDS: z.coerce.number().int().min(0).default(5),
  FIELD_STALE_SECONDS: posInt(300),
  FIELD_OBSERVE_GRACE_SECONDS: posInt(30),
  FIELD_UNCREDITED_MAX: posInt(24),
  FIELD_CREDIT_SURPLUS: z.coerce.number().int().min(0).default(8),
  FIELD_POWER_SLACK: z.coerce.number().min(1).default(1.15),
  KILL_BURST_FIELD_PER_EXTRA: z.coerce.number().int().min(0).default(2),
  FIELD_CARRY_SLACK: z.coerce.number().int().min(0).default(5),
  // 9단계: 기본값 0.12/0.2 -> 0.15/0.02, 하드 격차(경험치 1)와 골드 감쇠를 더한다(phase9_anti_abuse.md 6절)
  FIELD_CARRY_STEP: z.coerce.number().min(0).default(0.15),
  FIELD_CARRY_MIN: z.coerce.number().gt(0).max(1).default(0.02),
  FIELD_CARRY_HARD_GAP: posInt(15),
  FIELD_CARRY_HARD_DROP_MUL: z.coerce.number().min(0).max(1).default(0),
  FIELD_CARRY_GOLD_SCALE: boolStr.default(true),
  FIELD_PARTY_XP_FACTOR: z.coerce.number().gt(0).max(1).default(1),
  FIELD_PARTY_DROP_FACTOR: z.coerce.number().gt(0).max(1).default(1),
  FIELD_RECORD_RETENTION_DAYS: posInt(30),
  /** 멤버 개인 화력 상한 여유 알파: 개인 상한 = 자기 attack_cap x (1 + alpha) */
  FIELD_MEMBER_POWER_ALPHA: z.coerce.number().min(0).default(1),
  /** 다인 세션에서 호스트 관찰이 이 시간(분) 넘게 없으면 경험치 배율을 낮춘다 */
  FIELD_OBSERVE_LAPSE_MINUTES: posInt(3),
  FIELD_OBSERVE_LAPSE_XP_FACTOR: z.coerce.number().gt(0).max(1).default(0.5),
  /** 같은 (파티, 맵, 캐릭터)의 기여 부채를 이 시간(분) 안에 다시 열린 세션으로 이어 준다 */
  FIELD_DEBT_CARRY_MINUTES: posInt(10),
  RELAY_STATS_RETENTION_DAYS: posInt(90),
  // 속도 제한(캐릭터당, 창 길이는 코드 고정)
  RATE_RELAY_TICKET_PER_10S: posInt(3),
  RATE_RELAY_TICKET_PER_MIN: posInt(10),
  RATE_TRANSPORT_SWITCH_PER_10S: posInt(1),
  RATE_FIELD_ENTER_PER_SEC: posInt(1),
  RATE_FIELD_ENTER_PER_MIN: posInt(12),
  RATE_FIELD_GET_PER_SEC: posInt(2),
  RATE_FIELD_LEAVE_PER_SEC: posInt(1),
  RATE_FIELD_CLAIM_PER_SEC: posInt(1),
  RATE_FIELD_OBSERVE_PER_5SEC: posInt(1),
  RATE_FIELD_ME_PER_SEC: posInt(1),
};

export type Phase8Raw = z.output<z.ZodObject<typeof phase8Shape>>;

export interface RelayConfig {
  enabled: boolean;
  mode: 'embedded' | 'standalone';
  ticketSecret: string;
  publicUrl: string;
  ticketTtlSeconds: number;
  maxConnections: number;
  maxRooms: number;
  admissionRatio: number;
  handshakePerMinIp: number;
  unauthPerIp: number;
  connPerIp: number;
  helloTimeoutMs: number;
  pingEveryMs: number;
  idleTimeoutMs: number;
  flushMs: number;
  frameMaxBytes: number;
  packetMaxBytes: number;
  hostPktPerSec: number;
  memberPktPerSec: number;
  hostBytesPerSec: number;
  memberBytesPerSec: number;
  roomEgressBps: number;
  reliableQueueBytes: number;
  reliableQueueAgeMs: number;
  backpressureBytes: number;
  strikesPerMin: number;
  hostGraceMs: number;
  roomEmptyGraceSeconds: number;
  egressDailyBudgetGb: number | null;
  retentionDays: number;
}

export interface FieldConfig {
  electionWindowSeconds: number;
  staleSeconds: number;
  observeGraceSeconds: number;
  uncreditedMax: number;
  creditSurplus: number;
  powerSlack: number;
  killBurstPerExtra: number;
  carrySlack: number;
  carryStep: number;
  carryMin: number;
  carryHardGap: number;
  carryHardDropMul: number;
  carryGoldScale: boolean;
  partyXpFactor: number;
  partyDropFactor: number;
  retentionDays: number;
  memberPowerAlpha: number;
  observeLapseMinutes: number;
  observeLapseXpFactor: number;
  debtCarryMinutes: number;
}

export interface TransportConfig {
  order: TransportKind[];
  steamP2pEnabled: boolean;
  switchCooldownSeconds: number;
}

export interface Phase8Config {
  deployStage: 'test' | 'live';
  relay: RelayConfig;
  field: FieldConfig;
  transport: TransportConfig;
  steamExtra: { webApiBase: string; retries: number; breakerFailures: number; breakerOpenSeconds: number };
  rate: {
    relayTicket10s: number;
    relayTicketMin: number;
    transportSwitch10s: number;
    fieldEnterSec: number;
    fieldEnterMin: number;
    fieldGetSec: number;
    fieldLeaveSec: number;
    fieldClaimSec: number;
    fieldObserve5s: number;
    fieldMeSec: number;
  };
}

function isWeak(v: string): boolean {
  if (/^(.)\1+$/.test(v)) return true;
  return /(changeme|change-me|replace|example|placeholder|secret-secret|your[-_]?secret|password)/i.test(v);
}

const fail = (msg: string): never => {
  throw new Error(`환경변수 검증 실패: ${msg}`);
};

function parseOrder(raw: string): TransportKind[] {
  const out: TransportKind[] = [];
  for (const part of raw.split(',').map((x) => x.trim()).filter(Boolean)) {
    if (!(TRANSPORTS as readonly string[]).includes(part)) fail(`COMBAT_TRANSPORT_ORDER 에 알 수 없는 전송이 있습니다: ${part}`);
    if (!out.includes(part as TransportKind)) out.push(part as TransportKind);
  }
  if (out.length === 0) fail('COMBAT_TRANSPORT_ORDER 가 비어 있습니다');
  return out;
}

/** 8단계 설정 조립과 기동 가드(G2, G8, G10). legacyTransport는 폐기된 PARTY_TRANSPORT 값(없으면 undefined) */
export function buildPhase8(
  e: Phase8Raw,
  ctx: { prod: boolean; jwtSecret: string; port: number; legacyTransport: 'dev' | 'steam' | undefined; wsMaxConnections: number },
): Phase8Config {
  const stage = e.DEPLOY_STAGE;
  let order: TransportKind[];
  if (e.COMBAT_TRANSPORT_ORDER !== undefined) {
    order = parseOrder(e.COMBAT_TRANSPORT_ORDER);
    if (ctx.prod && order.includes('dev')) fail('운영에서는 COMBAT_TRANSPORT_ORDER 에 dev 를 둘 수 없습니다');
  } else if (ctx.legacyTransport === 'steam') {
    order = ['steam', 'relay'];
  } else if (ctx.legacyTransport === 'dev') {
    order = ctx.prod ? ['relay'] : ['relay', 'dev'];
  } else {
    order = stage === 'test' ? ['steam', 'relay'] : ['relay', 'steam'];
  }
  if (!e.RELAY_ENABLED) order = order.filter((t) => t !== 'relay');

  let secret = e.RELAY_TICKET_SECRET ?? '';
  if (e.RELAY_ENABLED) {
    if (!e.RELAY_TICKET_SECRET) {
      // 개발·시험에서만 JWT 비밀에서 파생한다(운영은 G8로 필수)
      if (ctx.prod) fail('RELAY_ENABLED=true 에는 RELAY_TICKET_SECRET 이 필요합니다');
      secret = createHash('sha256').update(`relay-ticket:${ctx.jwtSecret}`).digest('base64url');
    } else {
      if (secret.length < 32) fail('RELAY_TICKET_SECRET 은 32자 이상이어야 합니다');
      if (secret === ctx.jwtSecret) fail('RELAY_TICKET_SECRET 은 JWT_SECRET 과 달라야 합니다');
      if (isWeak(secret)) fail('RELAY_TICKET_SECRET 이 자리표시 값이거나 반복 문자입니다');
    }
    if (ctx.prod && !e.RELAY_PUBLIC_URL) fail('운영에서 RELAY_ENABLED=true 에는 RELAY_PUBLIC_URL(wss://...)이 필요합니다');
    if (ctx.prod && e.RELAY_PUBLIC_URL && !e.RELAY_PUBLIC_URL.startsWith('wss://')) fail('운영 RELAY_PUBLIC_URL 은 wss:// 여야 합니다');
  }
  if (ctx.prod && order.length === 0) fail('운영에 쓸 수 있는 전투 전송이 없습니다');
  if (ctx.prod && e.RELAY_MAX_CONNECTIONS + ctx.wsMaxConnections > 20000) {
    fail('RELAY_MAX_CONNECTIONS + WS_MAX_CONNECTIONS 가 너무 큽니다(부하 시험 후 조정)');
  }
  return {
    deployStage: stage,
    relay: {
      enabled: e.RELAY_ENABLED,
      mode: e.RELAY_MODE,
      ticketSecret: secret,
      publicUrl: e.RELAY_PUBLIC_URL ?? `ws://localhost:${ctx.port}/relay`,
      ticketTtlSeconds: e.RELAY_TICKET_TTL_SECONDS,
      maxConnections: e.RELAY_MAX_CONNECTIONS,
      maxRooms: e.RELAY_MAX_ROOMS,
      admissionRatio: e.RELAY_ADMISSION_RATIO,
      handshakePerMinIp: e.RELAY_HANDSHAKE_PER_MIN_IP,
      unauthPerIp: e.RELAY_UNAUTH_PER_IP,
      connPerIp: e.RELAY_CONN_PER_IP,
      helloTimeoutMs: e.RELAY_HELLO_TIMEOUT_MS,
      pingEveryMs: e.RELAY_PING_EVERY_MS,
      idleTimeoutMs: e.RELAY_IDLE_TIMEOUT_MS,
      flushMs: e.RELAY_FLUSH_MS,
      frameMaxBytes: e.RELAY_FRAME_MAX_BYTES,
      packetMaxBytes: e.RELAY_PACKET_MAX_BYTES,
      hostPktPerSec: e.RELAY_HOST_PKT_PER_SEC,
      memberPktPerSec: e.RELAY_MEMBER_PKT_PER_SEC,
      hostBytesPerSec: e.RELAY_HOST_BYTES_PER_SEC,
      memberBytesPerSec: e.RELAY_MEMBER_BYTES_PER_SEC,
      roomEgressBps: e.RELAY_ROOM_EGRESS_BPS,
      reliableQueueBytes: e.RELAY_RELIABLE_QUEUE_BYTES,
      reliableQueueAgeMs: e.RELAY_RELIABLE_QUEUE_AGE_MS,
      backpressureBytes: e.RELAY_BACKPRESSURE_BYTES,
      strikesPerMin: e.RELAY_STRIKES_PER_MIN,
      hostGraceMs: e.RELAY_HOST_GRACE_MS,
      roomEmptyGraceSeconds: e.RELAY_ROOM_EMPTY_GRACE_SECONDS,
      egressDailyBudgetGb: e.RELAY_EGRESS_DAILY_BUDGET_GB ?? null,
      retentionDays: e.RELAY_STATS_RETENTION_DAYS,
    },
    field: {
      electionWindowSeconds: e.FIELD_ELECTION_WINDOW_SECONDS,
      staleSeconds: e.FIELD_STALE_SECONDS,
      observeGraceSeconds: e.FIELD_OBSERVE_GRACE_SECONDS,
      uncreditedMax: e.FIELD_UNCREDITED_MAX,
      creditSurplus: e.FIELD_CREDIT_SURPLUS,
      powerSlack: e.FIELD_POWER_SLACK,
      killBurstPerExtra: e.KILL_BURST_FIELD_PER_EXTRA,
      carrySlack: e.FIELD_CARRY_SLACK,
      carryStep: e.FIELD_CARRY_STEP,
      carryMin: e.FIELD_CARRY_MIN,
      carryHardGap: e.FIELD_CARRY_HARD_GAP,
      carryHardDropMul: e.FIELD_CARRY_HARD_DROP_MUL,
      carryGoldScale: e.FIELD_CARRY_GOLD_SCALE,
      partyXpFactor: e.FIELD_PARTY_XP_FACTOR,
      partyDropFactor: e.FIELD_PARTY_DROP_FACTOR,
      retentionDays: e.FIELD_RECORD_RETENTION_DAYS,
      memberPowerAlpha: e.FIELD_MEMBER_POWER_ALPHA,
      observeLapseMinutes: e.FIELD_OBSERVE_LAPSE_MINUTES,
      observeLapseXpFactor: e.FIELD_OBSERVE_LAPSE_XP_FACTOR,
      debtCarryMinutes: e.FIELD_DEBT_CARRY_MINUTES,
    },
    transport: { order, steamP2pEnabled: e.STEAM_P2P_ENABLED, switchCooldownSeconds: e.TRANSPORT_SWITCH_COOLDOWN_SECONDS },
    steamExtra: {
      webApiBase: e.STEAM_WEB_API_BASE,
      retries: e.STEAM_WEB_API_RETRIES,
      breakerFailures: e.STEAM_BREAKER_FAILURES,
      breakerOpenSeconds: e.STEAM_BREAKER_OPEN_SECONDS,
    },
    rate: {
      relayTicket10s: e.RATE_RELAY_TICKET_PER_10S,
      relayTicketMin: e.RATE_RELAY_TICKET_PER_MIN,
      transportSwitch10s: e.RATE_TRANSPORT_SWITCH_PER_10S,
      fieldEnterSec: e.RATE_FIELD_ENTER_PER_SEC,
      fieldEnterMin: e.RATE_FIELD_ENTER_PER_MIN,
      fieldGetSec: e.RATE_FIELD_GET_PER_SEC,
      fieldLeaveSec: e.RATE_FIELD_LEAVE_PER_SEC,
      fieldClaimSec: e.RATE_FIELD_CLAIM_PER_SEC,
      fieldObserve5s: e.RATE_FIELD_OBSERVE_PER_5SEC,
      fieldMeSec: e.RATE_FIELD_ME_PER_SEC,
    },
  };
}
