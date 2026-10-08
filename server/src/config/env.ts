import path from 'node:path';
import { z } from 'zod';
import { logger } from '../utils/logger';
import { antiAbuseShape, buildAntiAbuse, type AntiAbuseConfig } from './antiAbuseEnv';
import { buildSweep, sweepShape, type SweepConfig } from './sweepEnv';
import { buildPay, payShape, type PayConfig } from './payEnv';
import { buildWithdraw, withdrawShape, type WithdrawConfig } from './withdrawEnv';
import { buildPhase8, phase8Shape, type FieldConfig, type Phase8Config, type RelayConfig, type TransportConfig } from './phase8Env';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);
const nonNegInt = (def: number) => z.coerce.number().int().min(0).default(def);

const envSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().min(1).max(65535).default(3000),
  DATABASE_URL: z.string().min(1),
  JWT_SECRET: z.string().min(32, 'JWT_SECRET은 32자 이상이어야 합니다'),
  MIN_CLIENT_VERSION: z.string().regex(/^\d+\.\d+\.\d+$/),
  AUTH_DEV_ENABLED: boolStr.optional(),
  REQUEST_LOG_TTL_DAYS: posInt(7),
  GAME_DATA_DIR: z.string().optional(),
  TRUST_PROXY: z.coerce.number().int().min(0).default(0),
  LOG_LEVEL: z.string().default('info'),
  RATE_REGISTER_IP_MAX: posInt(5),
  RATE_LOGIN_IP_MAX: posInt(20),
  RATE_LOGIN_FAIL_MAX: posInt(5),
  RATE_REFRESH_ACCOUNT_MAX: posInt(10),
  RATE_CHAR_CREATE_MAX: posInt(10),
  RATE_STATE_SAVE_MAX: posInt(1),
  RATE_GENERAL_IP_MAX: posInt(120),
  // 3단계(경제) 속도 제한. 캐릭터별 한도는 창 길이가 코드 고정(초 단위 또는 분 단위)
  RATE_ECONOMY_IP_MAX: posInt(600),
  RATE_KILL_PER_SEC: posInt(6),
  RATE_KILL_PER_MIN: posInt(90),
  RATE_CLAIM_PER_SEC: posInt(5),
  RATE_GATHER_PER_SEC: posInt(3),
  RATE_NODES_PER_SEC: posInt(1),
  RATE_SHOP_PER_SEC: posInt(5),
  RATE_ENHANCE_PER_SEC: posInt(2),
  RATE_USE_PER_SEC: posInt(3),
  RATE_SLOW_PER_SEC: posInt(1),
  // 3단계 서버 정책 상수(게임 데이터가 아니라 방어·운영 값)
  DROP_TTL_SECONDS: posInt(300),
  DROP_OPEN_PER_CHARACTER: posInt(300),
  KILL_BURST_FIELD: posInt(4),
  KILL_BURST_DUNGEON: posInt(6),
  KILL_SUPPLY_MARGIN: z.coerce.number().min(1).default(1.1),
  POWER_PASSIVE_PER_LEVEL: z.coerce.number().min(0).default(0.11),
  POWER_SKILL_FACTOR: z.coerce.number().min(1).default(2),
  POWER_AOE_CAP: posInt(5),
  ANOMALY_WINDOW_MINUTES: posInt(10),
  ANOMALY_BLOCK_COUNT: posInt(20),
  GATHER_GRACE_SECONDS: z.coerce.number().int().min(0).default(3),
  GATHER_PER_MINUTE: posInt(40),
  DUNGEON_TIME_OVERHEAD_SECONDS: z.coerce.number().int().min(0).default(30),
  ITEM_USE_MIN_GAP_MS: z.coerce.number().int().min(0).default(500),
  RUN_STALE_SECONDS: posInt(3600),
  DUNGEON_ROOM_CLEAR_RATIO: z.coerce.number().gt(0).max(1).default(0.8),
  DUNGEON_CARD_TTL_HOURS: posInt(24),
  DUNGEON_CARD_AUTO_PICK_MINUTES: posInt(10),
  // 4단계(파티) 속도 제한
  RATE_PARTY_LIST_PER_SEC: posInt(1),
  RATE_PARTY_POLL_PER_SEC: posInt(2),
  RATE_PARTY_CREATE_PER_3SEC: posInt(1),
  RATE_PARTY_ACTION_PER_SEC: posInt(2),
  RATE_PARTY_RUN_PER_SEC: posInt(1),
  RATE_HEARTBEAT_PER_2SEC: posInt(1),
  RATE_STEAM_IP_MAX: posInt(20),
  RATE_STEAM_LINK_MAX: posInt(5),
  // 4단계 정책 상수
  PARTY_LISTING_MINUTES: posInt(10),
  PARTY_APPLY_SECONDS: posInt(120), // a person needs time to notice and accept
  PARTY_IDLE_MINUTES: posInt(30),
  MATCH_QUEUE_SECONDS: posInt(60),
  MATCH_POWER_RATIO: z.coerce.number().gt(0).default(0.3),
  MATCH_START_SECONDS: posInt(60),
  MATCH_TICKET_STALE_SECONDS: posInt(15),
  PARTY_GATHER_SECONDS: posInt(90),
  PARTY_MIN_LEVEL_SLACK: z.coerce.number().int().min(0).default(5),
  HOST_STALE_SECONDS: posInt(12),
  PARTY_REJOIN_SECONDS: posInt(60),
  PARTY_RESULT_WAIT_SECONDS: posInt(90),
  PARTY_ELAPSED_TOLERANCE_MS: posInt(5000),
  PARTY_KILL_SLACK_RATIO: z.coerce.number().min(0).max(1).default(0.2),
  PARTY_POWER_SLACK: z.coerce.number().min(1).default(1.15),
  PARTY_DAMAGE_MIN_RATIO: z.coerce.number().min(0).max(1).default(0.8),
  RAID_REWARD_MIN_HUMANS: posInt(2),
  RAID_PRACTICE_PAYS_KILLS: boolStr.default(false),
  // Steam 인증(값이 틀리면 기동하지 않는다)
  STEAM_AUTH_MODE: z.enum(['off', 'mock', 'web_api']).default('off'),
  STEAM_APP_ID: z.coerce.number().int().positive().optional(),
  STEAM_WEB_API_KEY: z.string().min(1).optional(),
  STEAM_IDENTITY: z.string().min(1).default('dotrpg-server'),
  // 폐기(8단계): COMBAT_TRANSPORT_ORDER 가 대체한다. 값이 있으면 옛 설정으로 보고 순서를 파생한다
  PARTY_TRANSPORT: z.enum(['dev', 'steam']).optional(),
  // 5단계(채팅·친구) 속도 제한과 정책 상수. 값은 phase5_api.md 11절
  RATE_SOCIAL_IP_MAX: posInt(600),
  RATE_SOCIAL_LIST_PER_SEC: posInt(1),
  RATE_SOCIAL_REQUEST_PER_MIN: posInt(10),
  RATE_SOCIAL_ACTION_PER_SEC: posInt(2),
  RATE_SOCIAL_BLOCK_PER_MIN: posInt(20),
  CHAT_INTERVAL_TOLERANCE: z.coerce.number().gt(0).max(1).default(0.8),
  CHAT_PER_MINUTE: posInt(30),
  CHAT_WHISPER_TARGETS_PER_MIN: posInt(10),
  CHAT_SHARD_SIZE: posInt(150),
  CHAT_BACKLOG_GENERAL: posInt(20),
  CHAT_BACKLOG_MINUTES: posInt(10),
  CHAT_BACKLOG_PARTY: posInt(50),
  CHAT_BACKLOG_WHISPER: posInt(50),
  CHAT_BACKLOG_WHISPER_MINUTES: posInt(30),
  CHAT_RESEND_SECONDS: posInt(30),
  CHAT_FILTER_WINDOW_MINUTES: posInt(10),
  CHAT_FILTER_STRIKES: posInt(5),
  CHAT_REPEAT_MUTE_STRIKES: posInt(5),
  CHAT_AUTO_MUTE_MINUTES: posInt(10),
  CHAT_AUTO_ESCALATE_COUNT: posInt(3),
  CHAT_AUTO_MUTE_ESCALATED_MINUTES: posInt(1440),
  CHAT_RETENTION_DAYS: posInt(7),
  WS_HELLO_TIMEOUT_MS: posInt(5000),
  WS_PING_EVERY_MS: posInt(15000),
  WS_IDLE_TIMEOUT_MS: posInt(45000),
  WS_TOKEN_GRACE_SECONDS: posInt(30),
  WS_REVALIDATE_SECONDS: posInt(60),
  WS_MAX_PAYLOAD_BYTES: posInt(4096),
  WS_FRAMES_PER_SEC: posInt(10),
  WS_SEND_QUEUE_MAX: posInt(200),
  WS_HANDSHAKE_PER_MIN_IP: posInt(60),
  WS_UNAUTH_PER_IP: posInt(20),
  WS_MAX_CONNECTIONS: posInt(500),
  PARTY_POLL_WS_SECONDS: posInt(15),
  FRIEND_MAX: posInt(50),
  FRIEND_PENDING_OUT_MAX: posInt(20),
  FRIEND_PENDING_IN_MAX: posInt(50),
  FRIEND_REQUEST_DAYS: posInt(14),
  FRIEND_REREQUEST_HOURS: posInt(24),
  BLOCK_MAX: posInt(100),
  REPORT_PER_HOUR: posInt(5),
  REPORT_PER_DAY: posInt(20),
  REPORT_CONTEXT_HOURS: posInt(24),
  REPORT_LOOKBACK_MINUTES: posInt(30),
  REPORT_TARGET_LINES: posInt(10),
  REPORT_RETENTION_DAYS: posInt(180),
  PARTY_INVITE_SECONDS: posInt(30),
  INVITE_PER_MIN: posInt(10),
  INVITE_PER_TARGET_SECONDS: posInt(10),
  // 6단계(경매·우편) 속도 제한과 정책 상수. 값은 phase6_api.md 3절
  RATE_AUCTION_SEARCH_PER_SEC: posInt(2),
  RATE_AUCTION_PRICE_PER_SEC: posInt(5),
  RATE_AUCTION_SELLABLE_PER_SEC: posInt(1),
  RATE_AUCTION_LIST_PER_SEC: posInt(1),
  RATE_AUCTION_LIST_PER_MIN: posInt(10),
  RATE_AUCTION_ACTION_PER_SEC: posInt(2),
  RATE_AUCTION_MINE_PER_SEC: posInt(1),
  RATE_MAIL_CLAIM_PER_SEC: posInt(5),
  RATE_MAIL_CLAIM_ALL_PER_SEC: posInt(1),
  RATE_MAIL_SUMMARY_PER_5SEC: posInt(1),
  AUCTION_TICK_SECONDS: posInt(60),
  AUCTION_TICK_BATCH: posInt(100),
  AUCTION_MIN_LEVEL: nonNegInt(10),
  AUCTION_MIN_ACCOUNT_AGE_DAYS: nonNegInt(7),
  AUCTION_MAX_PRICE: posInt(1_000_000_000),
  GOLD_CLIENT_MAX: posInt(2_147_483_647),
  AUCTION_REF_WINDOW_DAYS: posInt(7),
  AUCTION_REF_MIN_TRADES: posInt(5),
  AUCTION_REF_MIN_BUYERS: posInt(3),
  AUCTION_REF_PAIR_MAX: posInt(2),
  AUCTION_REF_DAILY_CAP_BPS: posInt(15000),
  AUCTION_COLD_FLOOR_MULT: posInt(1),
  AUCTION_COLD_CEIL_MULT: posInt(100),
  AUCTION_PAIR_DAILY_TRADES: posInt(3),
  // 9단계 결정 5: 쌍별 일일 골드 한도 1억 -> 1천만(부계정 이전 통로의 규모를 직접 줄인다)
  AUCTION_PAIR_DAILY_GOLD: posInt(10_000_000),
  MAIL_CLAIM_ALL_MAX: posInt(50),
  AUCTION_TICK_ENABLED: boolStr.default(true),
  // 7단계(운영): 관리자, 가입 스위치, 점검, 종료, 정리, 감시. 값의 뜻은 phase7_ops.md 3.3
  AUTH_DEV_REGISTER_ENABLED: boolStr.optional(),
  ALLOW_DEV_AUTH_IN_PRODUCTION: boolStr.default(false),
  ADMIN_ENABLED: boolStr.optional(),
  ADMIN_BIND: z.string().min(1).default('127.0.0.1'),
  ADMIN_PORT: z.coerce.number().int().min(1).max(65535).default(3001),
  ADMIN_ALLOWED_CIDRS: z.string().default('127.0.0.1/32,::1/128'),
  ADMIN_SECRET_KEY: z.string().optional(),
  ADMIN_SESSION_IDLE_MINUTES: posInt(30),
  ADMIN_SESSION_MAX_HOURS: posInt(8),
  ADMIN_LOGIN_FAIL_MAX: posInt(5),
  ADMIN_LOCK_MINUTES: posInt(15),
  ADMIN_RATE_PER_MIN: posInt(120),
  ADMIN_GRANT_MAX_GOLD: posInt(1_000_000),
  ADMIN_GRANT_MAX_ITEM_COUNT: posInt(99),
  ADMIN_GRANT_DAILY_GOLD: posInt(5_000_000),
  ADMIN_OPERATOR_BAN_MAX_DAYS: posInt(30),
  WATCHLIST_MIN_SCORE: posInt(10),
  WATCHLIST_WINDOW_HOURS: posInt(24),
  MAINT_PRE_BLOCK_MINUTES: posInt(10),
  MAINT_ANNOUNCE_MINUTES: z.string().default('30,10,5,1'),
  MAINT_POLL_SECONDS: posInt(5),
  MAINT_BYE_DELAY_SECONDS: nonNegInt(3),
  SHUTDOWN_GRACE_SECONDS: posInt(30),
  DB_POOL_MAX: posInt(10),
  DB_STATEMENT_TIMEOUT_MS: posInt(15000),
  KILL_LOG_RETENTION_DAYS: posInt(7),
  DROP_RETENTION_DAYS: posInt(1),
  ANOMALY_RETENTION_DAYS: posInt(30),
  ANOMALY_SEVERE_RETENTION_DAYS: posInt(180),
  REFRESH_TOKEN_PURGE_DAYS: posInt(30),
  MAIL_CLAIMED_RETENTION_DAYS: posInt(180),
  PARTY_RECORD_RETENTION_DAYS: posInt(30),
  JOB_RUN_RETENTION_DAYS: posInt(90),
  ADMIN_SESSION_PURGE_DAYS: posInt(30),
  PURGE_BATCH: posInt(5000),
  PURGE_BATCH_SLEEP_MS: nonNegInt(100),
  JOB_MAX_SECONDS: posInt(300),
  JOBS_ENABLED: boolStr.optional(),
  OPS_SNAPSHOT_SECONDS: posInt(60),
  ALERT_WEBHOOK_URL: z.url().optional(),
  ALERT_WEBHOOK_FORMAT: z.enum(['discord', 'slack', 'json']).default('discord'),
  ALERT_MIN_INTERVAL_MINUTES: posInt(30),
  OPS_HEARTBEAT_URL: z.url().optional(),
  SERVER_NAME: z.string().min(1).default('dotrpg'),
  IMAGE_VERSION: z.string().default('dev'),
  ...phase8Shape,
  ...antiAbuseShape,
  ...sweepShape,
  ...payShape,
  ...withdrawShape,
});

export interface AppConfig {
  nodeEnv: 'development' | 'test' | 'production';
  port: number;
  databaseUrl: string;
  jwtSecret: string;
  minClientVersion: string;
  authDevEnabled: boolean;
  /** /auth/dev/register 라우트 등록 여부(authDevEnabled와 둘 다 참일 때만) */
  authDevRegisterEnabled: boolean;
  allowDevAuthInProduction: boolean;
  shutdownGraceSeconds: number;
  dbPoolMax: number;
  dbStatementTimeoutMs: number;
  admin: {
    enabled: boolean;
    bind: string;
    port: number;
    allowedCidrs: string[];
    secretKey: Buffer | null;
    sessionIdleMinutes: number;
    sessionMaxHours: number;
    loginFailMax: number;
    lockMinutes: number;
    ratePerMin: number;
    grantMaxGold: number;
    grantMaxItemCount: number;
    grantDailyGold: number;
    operatorBanMaxDays: number;
    watchlistMinScore: number;
    watchlistWindowHours: number;
  };
  maint: { preBlockMinutes: number; announceMinutes: number[]; pollSeconds: number; byeDelaySeconds: number };
  purge: {
    killLogDays: number;
    dropDays: number;
    anomalyDays: number;
    anomalySevereDays: number;
    refreshTokenDays: number;
    mailClaimedDays: number;
    partyRecordDays: number;
    jobRunDays: number;
    adminSessionDays: number;
    batch: number;
    batchSleepMs: number;
    jobMaxSeconds: number;
  };
  ops: {
    jobsEnabled: boolean;
    snapshotSeconds: number;
    alertWebhookUrl: string | null;
    alertWebhookFormat: 'discord' | 'slack' | 'json';
    alertMinIntervalMinutes: number;
    heartbeatUrl: string | null;
    serverName: string;
    imageVersion: string;
  };
  requestLogTtlDays: number;
  gameDataDir: string;
  trustProxy: number;
  logLevel: string;
  rate: {
    registerIp: number;
    loginIp: number;
    loginFail: number;
    refreshAccount: number;
    charCreate: number;
    stateSave: number;
    generalIp: number;
    economyIp: number;
    killPerSec: number;
    killPerMin: number;
    claimPerSec: number;
    gatherPerSec: number;
    nodesPerSec: number;
    shopPerSec: number;
    enhancePerSec: number;
    usePerSec: number;
    slowPerSec: number;
    partyListPerSec: number;
    partyPollPerSec: number;
    partyCreatePer3Sec: number;
    partyActionPerSec: number;
    partyRunPerSec: number;
    heartbeatPer2Sec: number;
    steamIp: number;
    steamLink: number;
    socialIp: number;
    socialListPerSec: number;
    socialRequestPerMin: number;
    socialActionPerSec: number;
    socialBlockPerMin: number;
    p8: Phase8Config['rate'];
    auctionSearchPerSec: number;
    auctionPricePerSec: number;
    auctionSellablePerSec: number;
    auctionListPerSec: number;
    auctionListPerMin: number;
    auctionActionPerSec: number;
    auctionMinePerSec: number;
    mailClaimPerSec: number;
    mailClaimAllPerSec: number;
    mailSummaryPer5Sec: number;
  };
  /** 6단계: 경매·우편 정책 상수(게임 데이터가 아니라 운영 값) */
  auction: {
    tickSeconds: number;
    tickBatch: number;
    tickEnabled: boolean;
    minLevel: number;
    minAccountAgeDays: number;
    maxPrice: number;
    goldClientMax: number;
    refWindowDays: number;
    refMinTrades: number;
    refMinBuyers: number;
    refPairMax: number;
    refDailyCapBps: number;
    coldFloorMult: number;
    coldCeilMult: number;
    pairDailyTrades: number;
    pairDailyGold: number;
    mailClaimAllMax: number;
  };
  /** 5단계: 채팅·WebSocket·친구·차단·신고·초대 정책 상수(게임 데이터가 아니라 운영 값) */
  social: {
    chatIntervalTolerance: number;
    chatPerMinute: number;
    chatWhisperTargetsPerMin: number;
    chatShardSize: number;
    chatBacklogGeneral: number;
    chatBacklogMinutes: number;
    chatBacklogParty: number;
    chatBacklogWhisper: number;
    chatBacklogWhisperMinutes: number;
    chatResendSeconds: number;
    chatFilterWindowMinutes: number;
    chatFilterStrikes: number;
    chatRepeatMuteStrikes: number;
    chatAutoMuteMinutes: number;
    chatAutoEscalateCount: number;
    chatAutoMuteEscalatedMinutes: number;
    chatRetentionDays: number;
    wsHelloTimeoutMs: number;
    wsPingEveryMs: number;
    wsIdleTimeoutMs: number;
    wsTokenGraceSeconds: number;
    wsRevalidateSeconds: number;
    wsMaxPayloadBytes: number;
    wsFramesPerSec: number;
    wsSendQueueMax: number;
    wsHandshakePerMinIp: number;
    wsUnauthPerIp: number;
    wsMaxConnections: number;
    partyPollWsSeconds: number;
    friendMax: number;
    friendPendingOutMax: number;
    friendPendingInMax: number;
    friendRequestDays: number;
    friendRerequestHours: number;
    blockMax: number;
    reportPerHour: number;
    reportPerDay: number;
    reportContextHours: number;
    reportLookbackMinutes: number;
    reportTargetLines: number;
    reportRetentionDays: number;
    partyInviteSeconds: number;
    invitePerMin: number;
    invitePerTargetSeconds: number;
  };
  steam: {
    mode: 'off' | 'mock' | 'web_api';
    appId: number | null;
    webApiKey: string | null;
    identity: string;
    webApiBase: string;
    retries: number;
    breakerFailures: number;
    breakerOpenSeconds: number;
  };
  /** 폐기된 PARTY_TRANSPORT(옛 설정 호환용, 없으면 'dev') */
  partyTransport: 'dev' | 'steam';
  /** 8단계 */
  deployStage: 'test' | 'live';
  relay: RelayConfig;
  field: FieldConfig;
  transport: TransportConfig;
  /** 9단계: 부정 행위 방지 */
  aa: AntiAbuseConfig;
  /** 10단계: 던전 클리어권(소탕)과 운영 우편 캠페인 */
  sweep: SweepConfig;
  /** 11단계: 별조각 Steam 결제와 결제 보호(기본 꺼짐) */
  pay: PayConfig;
  /** 회원 탈퇴(유예, 보관, 파기 스위치). 값은 법무 확인 전 기본값이며 환경변수로 바꾼다 */
  withdraw: WithdrawConfig;
  policy: {
    dropTtlSeconds: number;
    dropOpenPerCharacter: number;
    killBurstField: number;
    killBurstDungeon: number;
    killSupplyMargin: number;
    powerPassivePerLevel: number;
    powerSkillFactor: number;
    powerAoeCap: number;
    anomalyWindowMinutes: number;
    anomalyBlockCount: number;
    gatherGraceSeconds: number;
    gatherPerMinute: number;
    dungeonTimeOverheadSeconds: number;
    itemUseMinGapMs: number;
    runStaleSeconds: number;
    dungeonRoomClearRatio: number;
    dungeonCardTtlHours: number;
    /** 클리어 뒤 이만큼 지나도록 카드를 고르지 않으면 서버가 한 장을 골라 지급한다 */
    dungeonCardAutoPickMinutes: number;
    partyListingMinutes: number;
    partyApplySeconds: number;
    partyIdleMinutes: number;
    matchQueueSeconds: number;
    matchPowerRatio: number;
    matchStartSeconds: number;
    matchTicketStaleSeconds: number;
    partyGatherSeconds: number;
    partyMinLevelSlack: number;
    hostStaleSeconds: number;
    partyRejoinSeconds: number;
    partyResultWaitSeconds: number;
    partyElapsedToleranceMs: number;
    partyKillSlackRatio: number;
    partyPowerSlack: number;
    partyDamageMinRatio: number;
    raidRewardMinHumans: number;
    raidPracticePaysKills: boolean;
  };
}

// 빈 문자열은 "설정 안 함"으로 본다 (.env.example의 빈 칸 대응)
function clean(raw: NodeJS.ProcessEnv): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [k, v] of Object.entries(raw)) {
    if (v !== undefined && v !== '') out[k] = v;
  }
  return out;
}

/** 자리표시 값(changeme 등)이나 한 글자 반복이면 약한 비밀로 본다 */
function isWeakSecret(v: string): boolean {
  if (/^(.)\1+$/.test(v)) return true;
  return /(changeme|change-me|replace|example|placeholder|secret-secret|your[-_]?secret|password)/i.test(v);
}

export function loadConfig(raw: NodeJS.ProcessEnv = process.env): AppConfig {
  const parsed = envSchema.safeParse(clean(raw));
  if (!parsed.success) {
    const detail = parsed.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
    throw new Error(`환경변수 검증 실패: ${detail}`);
  }
  const e = parsed.data;
  if (e.STEAM_AUTH_MODE === 'web_api' && (!e.STEAM_APP_ID || !e.STEAM_WEB_API_KEY)) {
    throw new Error('환경변수 검증 실패: STEAM_AUTH_MODE=web_api 에는 STEAM_APP_ID 와 STEAM_WEB_API_KEY 가 필요합니다');
  }
  if (e.NODE_ENV === 'production' && e.STEAM_AUTH_MODE === 'mock') {
    throw new Error('환경변수 검증 실패: 운영에서는 STEAM_AUTH_MODE=mock 과 PARTY_TRANSPORT=dev 를 함께 쓸 수 없습니다');
  }
  if (e.NODE_ENV === 'production' && (e.AUCTION_MIN_LEVEL === 0 || e.AUCTION_MIN_ACCOUNT_AGE_DAYS === 0)) {
    throw new Error('환경변수 검증 실패: 운영에서는 AUCTION_MIN_LEVEL 과 AUCTION_MIN_ACCOUNT_AGE_DAYS 를 0으로 둘 수 없습니다');
  }
  // ---- 7단계 운영 가드(G1~G7): 틀리면 기동하지 않는다 ----
  const prod = e.NODE_ENV === 'production';
  const devAuth = e.AUTH_DEV_ENABLED ?? !prod;
  const adminEnabled = e.ADMIN_ENABLED ?? (prod ? true : e.ADMIN_SECRET_KEY !== undefined);
  let adminKey: Buffer | null = null;
  if (e.ADMIN_SECRET_KEY !== undefined) {
    adminKey = Buffer.from(e.ADMIN_SECRET_KEY, 'base64');
    if (adminKey.length !== 32) throw new Error('환경변수 검증 실패: ADMIN_SECRET_KEY 는 32바이트(base64)여야 합니다');
  }
  if (adminEnabled && !adminKey) throw new Error('환경변수 검증 실패: ADMIN_ENABLED=true 에는 ADMIN_SECRET_KEY 가 필요합니다');
  // 시험 서버(DEPLOY_STAGE=test)는 NODE_ENV=production 이어도 시험 구성(개발 로그인, Steam 연동 전)을 허용한다(G9)
  const mockFree = e.ALLOW_DEV_AUTH_IN_PRODUCTION || e.DEPLOY_STAGE === 'test';
  if (prod) {
    if (e.STEAM_AUTH_MODE === 'mock') {
      throw new Error('환경변수 검증 실패: 운영에서는 STEAM_AUTH_MODE=mock 을 쓸 수 없습니다(누구의 Steam ID로든 로그인됩니다)');
    }
    if (e.STEAM_AUTH_MODE === 'off' && !mockFree) {
      throw new Error('환경변수 검증 실패: 운영에서는 STEAM_AUTH_MODE=web_api 여야 합니다(Steam 연동 전 시험은 ALLOW_DEV_AUTH_IN_PRODUCTION=true)');
    }
    if (e.PARTY_TRANSPORT === 'dev' && e.COMBAT_TRANSPORT_ORDER === undefined && !mockFree) {
      throw new Error('환경변수 검증 실패: 운영에서는 PARTY_TRANSPORT=steam 이어야 합니다(Steam 연동 전 시험은 ALLOW_DEV_AUTH_IN_PRODUCTION=true)');
    }
    if (devAuth && !mockFree) {
      throw new Error('환경변수 검증 실패: 운영에서 AUTH_DEV_ENABLED=true 는 ALLOW_DEV_AUTH_IN_PRODUCTION=true 가 함께 있을 때만 허용됩니다');
    }
    if (e.STEAM_APP_ID === 480 && e.DEPLOY_STAGE === 'live') {
      throw new Error('환경변수 검증 실패: 운영(DEPLOY_STAGE=live)에서는 STEAM_APP_ID=480(Valve 시험용 앱)을 쓸 수 없습니다');
    }
    if (e.TRUST_PROXY < 1) {
      throw new Error('환경변수 검증 실패: 운영(프록시 뒤)에서는 TRUST_PROXY 가 1 이상이어야 합니다(0이면 모든 접속자가 프록시 IP 하나로 보입니다)');
    }
    if (isWeakSecret(e.JWT_SECRET)) {
      throw new Error('환경변수 검증 실패: JWT_SECRET 이 자리표시 값이거나 반복 문자입니다');
    }
    if (adminEnabled && (e.ADMIN_BIND === '0.0.0.0' || e.ADMIN_BIND === '::')) {
      throw new Error('환경변수 검증 실패: ADMIN_BIND 를 모든 인터페이스(0.0.0.0)로 둘 수 없습니다');
    }
  }
  const p8 = buildPhase8(e, { prod, jwtSecret: e.JWT_SECRET, port: e.PORT, legacyTransport: e.PARTY_TRANSPORT, wsMaxConnections: e.WS_MAX_CONNECTIONS });
  // 9단계: 운영에서 DEPLOY_STAGE 가 환경에 명시되지 않으면 기동 실패(조용히 live 로 보이는 상태를 없앤다)
  if (prod && clean(raw).DEPLOY_STAGE === undefined) {
    throw new Error('환경변수 검증 실패: 운영(NODE_ENV=production)에서는 DEPLOY_STAGE(test 또는 live)를 명시해야 합니다');
  }
  const aa = buildAntiAbuse(e, {
    prod,
    jwtSecret: e.JWT_SECRET,
    gameDataDir: e.GAME_DATA_DIR ?? path.resolve(__dirname, '..', '..', 'data'),
    fieldCarry: { slack: e.FIELD_CARRY_SLACK, hardGap: e.FIELD_CARRY_HARD_GAP },
    warn: (m) => logger.warn(m),
  });
  const pay = buildPay(e, { prod, jwtSecret: e.JWT_SECRET, steamAuthMode: e.STEAM_AUTH_MODE, deployStage: p8.deployStage, warn: (m) => logger.warn(m) });
  const withdraw = buildWithdraw(e, { prod, jwtSecret: e.JWT_SECRET });
  const announce = e.MAINT_ANNOUNCE_MINUTES.split(',')
    .map((x) => Number(x.trim()))
    .filter((n) => Number.isInteger(n) && n > 0)
    .sort((a, b) => b - a);
  return {
    nodeEnv: e.NODE_ENV,
    port: e.PORT,
    databaseUrl: e.DATABASE_URL,
    jwtSecret: e.JWT_SECRET,
    minClientVersion: e.MIN_CLIENT_VERSION,
    authDevEnabled: devAuth,
    authDevRegisterEnabled: devAuth && (e.AUTH_DEV_REGISTER_ENABLED ?? !prod),
    allowDevAuthInProduction: mockFree,
    shutdownGraceSeconds: e.SHUTDOWN_GRACE_SECONDS,
    dbPoolMax: e.DB_POOL_MAX,
    dbStatementTimeoutMs: e.DB_STATEMENT_TIMEOUT_MS,
    admin: {
      enabled: adminEnabled,
      bind: e.ADMIN_BIND,
      port: e.ADMIN_PORT,
      allowedCidrs: e.ADMIN_ALLOWED_CIDRS.split(',').map((x) => x.trim()).filter(Boolean),
      secretKey: adminKey,
      sessionIdleMinutes: e.ADMIN_SESSION_IDLE_MINUTES,
      sessionMaxHours: e.ADMIN_SESSION_MAX_HOURS,
      loginFailMax: e.ADMIN_LOGIN_FAIL_MAX,
      lockMinutes: e.ADMIN_LOCK_MINUTES,
      ratePerMin: e.ADMIN_RATE_PER_MIN,
      grantMaxGold: e.ADMIN_GRANT_MAX_GOLD,
      grantMaxItemCount: e.ADMIN_GRANT_MAX_ITEM_COUNT,
      grantDailyGold: e.ADMIN_GRANT_DAILY_GOLD,
      operatorBanMaxDays: e.ADMIN_OPERATOR_BAN_MAX_DAYS,
      watchlistMinScore: e.WATCHLIST_MIN_SCORE,
      watchlistWindowHours: e.WATCHLIST_WINDOW_HOURS,
    },
    maint: {
      preBlockMinutes: e.MAINT_PRE_BLOCK_MINUTES,
      announceMinutes: announce,
      pollSeconds: e.MAINT_POLL_SECONDS,
      byeDelaySeconds: e.MAINT_BYE_DELAY_SECONDS,
    },
    purge: {
      killLogDays: e.KILL_LOG_RETENTION_DAYS,
      dropDays: e.DROP_RETENTION_DAYS,
      anomalyDays: e.ANOMALY_RETENTION_DAYS,
      anomalySevereDays: e.ANOMALY_SEVERE_RETENTION_DAYS,
      refreshTokenDays: e.REFRESH_TOKEN_PURGE_DAYS,
      mailClaimedDays: e.MAIL_CLAIMED_RETENTION_DAYS,
      partyRecordDays: e.PARTY_RECORD_RETENTION_DAYS,
      jobRunDays: e.JOB_RUN_RETENTION_DAYS,
      adminSessionDays: e.ADMIN_SESSION_PURGE_DAYS,
      batch: e.PURGE_BATCH,
      batchSleepMs: e.PURGE_BATCH_SLEEP_MS,
      jobMaxSeconds: e.JOB_MAX_SECONDS,
    },
    ops: {
      jobsEnabled: e.JOBS_ENABLED ?? true,
      snapshotSeconds: e.OPS_SNAPSHOT_SECONDS,
      alertWebhookUrl: e.ALERT_WEBHOOK_URL ?? null,
      alertWebhookFormat: e.ALERT_WEBHOOK_FORMAT,
      alertMinIntervalMinutes: e.ALERT_MIN_INTERVAL_MINUTES,
      heartbeatUrl: e.OPS_HEARTBEAT_URL ?? null,
      serverName: e.SERVER_NAME,
      imageVersion: e.IMAGE_VERSION,
    },
    requestLogTtlDays: e.REQUEST_LOG_TTL_DAYS,
    gameDataDir: e.GAME_DATA_DIR ?? path.resolve(__dirname, '..', '..', 'data'),
    trustProxy: e.TRUST_PROXY,
    logLevel: e.LOG_LEVEL,
    rate: {
      registerIp: e.RATE_REGISTER_IP_MAX,
      loginIp: e.RATE_LOGIN_IP_MAX,
      loginFail: e.RATE_LOGIN_FAIL_MAX,
      refreshAccount: e.RATE_REFRESH_ACCOUNT_MAX,
      charCreate: e.RATE_CHAR_CREATE_MAX,
      stateSave: e.RATE_STATE_SAVE_MAX,
      generalIp: e.RATE_GENERAL_IP_MAX,
      economyIp: e.RATE_ECONOMY_IP_MAX,
      killPerSec: e.RATE_KILL_PER_SEC,
      killPerMin: e.RATE_KILL_PER_MIN,
      claimPerSec: e.RATE_CLAIM_PER_SEC,
      gatherPerSec: e.RATE_GATHER_PER_SEC,
      nodesPerSec: e.RATE_NODES_PER_SEC,
      shopPerSec: e.RATE_SHOP_PER_SEC,
      enhancePerSec: e.RATE_ENHANCE_PER_SEC,
      usePerSec: e.RATE_USE_PER_SEC,
      slowPerSec: e.RATE_SLOW_PER_SEC,
      partyListPerSec: e.RATE_PARTY_LIST_PER_SEC,
      partyPollPerSec: e.RATE_PARTY_POLL_PER_SEC,
      partyCreatePer3Sec: e.RATE_PARTY_CREATE_PER_3SEC,
      partyActionPerSec: e.RATE_PARTY_ACTION_PER_SEC,
      partyRunPerSec: e.RATE_PARTY_RUN_PER_SEC,
      heartbeatPer2Sec: e.RATE_HEARTBEAT_PER_2SEC,
      steamIp: e.RATE_STEAM_IP_MAX,
      steamLink: e.RATE_STEAM_LINK_MAX,
      socialIp: e.RATE_SOCIAL_IP_MAX,
      socialListPerSec: e.RATE_SOCIAL_LIST_PER_SEC,
      socialRequestPerMin: e.RATE_SOCIAL_REQUEST_PER_MIN,
      socialActionPerSec: e.RATE_SOCIAL_ACTION_PER_SEC,
      socialBlockPerMin: e.RATE_SOCIAL_BLOCK_PER_MIN,
      auctionSearchPerSec: e.RATE_AUCTION_SEARCH_PER_SEC,
      auctionPricePerSec: e.RATE_AUCTION_PRICE_PER_SEC,
      auctionSellablePerSec: e.RATE_AUCTION_SELLABLE_PER_SEC,
      auctionListPerSec: e.RATE_AUCTION_LIST_PER_SEC,
      auctionListPerMin: e.RATE_AUCTION_LIST_PER_MIN,
      auctionActionPerSec: e.RATE_AUCTION_ACTION_PER_SEC,
      auctionMinePerSec: e.RATE_AUCTION_MINE_PER_SEC,
      mailClaimPerSec: e.RATE_MAIL_CLAIM_PER_SEC,
      mailClaimAllPerSec: e.RATE_MAIL_CLAIM_ALL_PER_SEC,
      mailSummaryPer5Sec: e.RATE_MAIL_SUMMARY_PER_5SEC,
      p8: p8.rate,
    },
    auction: {
      tickSeconds: e.AUCTION_TICK_SECONDS,
      tickBatch: e.AUCTION_TICK_BATCH,
      tickEnabled: e.AUCTION_TICK_ENABLED,
      minLevel: e.AUCTION_MIN_LEVEL,
      minAccountAgeDays: e.AUCTION_MIN_ACCOUNT_AGE_DAYS,
      maxPrice: e.AUCTION_MAX_PRICE,
      goldClientMax: e.GOLD_CLIENT_MAX,
      refWindowDays: e.AUCTION_REF_WINDOW_DAYS,
      refMinTrades: e.AUCTION_REF_MIN_TRADES,
      refMinBuyers: e.AUCTION_REF_MIN_BUYERS,
      refPairMax: e.AUCTION_REF_PAIR_MAX,
      refDailyCapBps: e.AUCTION_REF_DAILY_CAP_BPS,
      coldFloorMult: e.AUCTION_COLD_FLOOR_MULT,
      coldCeilMult: e.AUCTION_COLD_CEIL_MULT,
      pairDailyTrades: e.AUCTION_PAIR_DAILY_TRADES,
      pairDailyGold: e.AUCTION_PAIR_DAILY_GOLD,
      mailClaimAllMax: e.MAIL_CLAIM_ALL_MAX,
    },
    social: {
      chatIntervalTolerance: e.CHAT_INTERVAL_TOLERANCE,
      chatPerMinute: e.CHAT_PER_MINUTE,
      chatWhisperTargetsPerMin: e.CHAT_WHISPER_TARGETS_PER_MIN,
      chatShardSize: e.CHAT_SHARD_SIZE,
      chatBacklogGeneral: e.CHAT_BACKLOG_GENERAL,
      chatBacklogMinutes: e.CHAT_BACKLOG_MINUTES,
      chatBacklogParty: e.CHAT_BACKLOG_PARTY,
      chatBacklogWhisper: e.CHAT_BACKLOG_WHISPER,
      chatBacklogWhisperMinutes: e.CHAT_BACKLOG_WHISPER_MINUTES,
      chatResendSeconds: e.CHAT_RESEND_SECONDS,
      chatFilterWindowMinutes: e.CHAT_FILTER_WINDOW_MINUTES,
      chatFilterStrikes: e.CHAT_FILTER_STRIKES,
      chatRepeatMuteStrikes: e.CHAT_REPEAT_MUTE_STRIKES,
      chatAutoMuteMinutes: e.CHAT_AUTO_MUTE_MINUTES,
      chatAutoEscalateCount: e.CHAT_AUTO_ESCALATE_COUNT,
      chatAutoMuteEscalatedMinutes: e.CHAT_AUTO_MUTE_ESCALATED_MINUTES,
      chatRetentionDays: e.CHAT_RETENTION_DAYS,
      wsHelloTimeoutMs: e.WS_HELLO_TIMEOUT_MS,
      wsPingEveryMs: e.WS_PING_EVERY_MS,
      wsIdleTimeoutMs: e.WS_IDLE_TIMEOUT_MS,
      wsTokenGraceSeconds: e.WS_TOKEN_GRACE_SECONDS,
      wsRevalidateSeconds: e.WS_REVALIDATE_SECONDS,
      wsMaxPayloadBytes: e.WS_MAX_PAYLOAD_BYTES,
      wsFramesPerSec: e.WS_FRAMES_PER_SEC,
      wsSendQueueMax: e.WS_SEND_QUEUE_MAX,
      wsHandshakePerMinIp: e.WS_HANDSHAKE_PER_MIN_IP,
      wsUnauthPerIp: e.WS_UNAUTH_PER_IP,
      wsMaxConnections: e.WS_MAX_CONNECTIONS,
      partyPollWsSeconds: e.PARTY_POLL_WS_SECONDS,
      friendMax: e.FRIEND_MAX,
      friendPendingOutMax: e.FRIEND_PENDING_OUT_MAX,
      friendPendingInMax: e.FRIEND_PENDING_IN_MAX,
      friendRequestDays: e.FRIEND_REQUEST_DAYS,
      friendRerequestHours: e.FRIEND_REREQUEST_HOURS,
      blockMax: e.BLOCK_MAX,
      reportPerHour: e.REPORT_PER_HOUR,
      reportPerDay: e.REPORT_PER_DAY,
      reportContextHours: e.REPORT_CONTEXT_HOURS,
      reportLookbackMinutes: e.REPORT_LOOKBACK_MINUTES,
      reportTargetLines: e.REPORT_TARGET_LINES,
      reportRetentionDays: e.REPORT_RETENTION_DAYS,
      partyInviteSeconds: e.PARTY_INVITE_SECONDS,
      invitePerMin: e.INVITE_PER_MIN,
      invitePerTargetSeconds: e.INVITE_PER_TARGET_SECONDS,
    },
    steam: {
      mode: e.STEAM_AUTH_MODE,
      appId: e.STEAM_APP_ID ?? null,
      webApiKey: e.STEAM_WEB_API_KEY ?? null,
      identity: e.STEAM_IDENTITY,
      ...p8.steamExtra,
    },
    partyTransport: e.PARTY_TRANSPORT ?? 'dev',
    deployStage: p8.deployStage,
    relay: p8.relay,
    field: p8.field,
    transport: p8.transport,
    aa,
    sweep: buildSweep(e, { prod, adminGrantMaxGold: e.ADMIN_GRANT_MAX_GOLD }),
    pay,
    withdraw,
    policy: {
      dropTtlSeconds: e.DROP_TTL_SECONDS,
      dropOpenPerCharacter: e.DROP_OPEN_PER_CHARACTER,
      killBurstField: e.KILL_BURST_FIELD,
      killBurstDungeon: e.KILL_BURST_DUNGEON,
      killSupplyMargin: e.KILL_SUPPLY_MARGIN,
      powerPassivePerLevel: e.POWER_PASSIVE_PER_LEVEL,
      powerSkillFactor: e.POWER_SKILL_FACTOR,
      powerAoeCap: e.POWER_AOE_CAP,
      anomalyWindowMinutes: e.ANOMALY_WINDOW_MINUTES,
      anomalyBlockCount: e.ANOMALY_BLOCK_COUNT,
      gatherGraceSeconds: e.GATHER_GRACE_SECONDS,
      gatherPerMinute: e.GATHER_PER_MINUTE,
      dungeonTimeOverheadSeconds: e.DUNGEON_TIME_OVERHEAD_SECONDS,
      itemUseMinGapMs: e.ITEM_USE_MIN_GAP_MS,
      runStaleSeconds: e.RUN_STALE_SECONDS,
      dungeonRoomClearRatio: e.DUNGEON_ROOM_CLEAR_RATIO,
      dungeonCardTtlHours: e.DUNGEON_CARD_TTL_HOURS,
      dungeonCardAutoPickMinutes: e.DUNGEON_CARD_AUTO_PICK_MINUTES,
      partyListingMinutes: e.PARTY_LISTING_MINUTES,
      partyApplySeconds: e.PARTY_APPLY_SECONDS,
      partyIdleMinutes: e.PARTY_IDLE_MINUTES,
      matchQueueSeconds: e.MATCH_QUEUE_SECONDS,
      matchPowerRatio: e.MATCH_POWER_RATIO,
      matchStartSeconds: e.MATCH_START_SECONDS,
      matchTicketStaleSeconds: e.MATCH_TICKET_STALE_SECONDS,
      partyGatherSeconds: e.PARTY_GATHER_SECONDS,
      partyMinLevelSlack: e.PARTY_MIN_LEVEL_SLACK,
      hostStaleSeconds: e.HOST_STALE_SECONDS,
      partyRejoinSeconds: e.PARTY_REJOIN_SECONDS,
      partyResultWaitSeconds: e.PARTY_RESULT_WAIT_SECONDS,
      partyElapsedToleranceMs: e.PARTY_ELAPSED_TOLERANCE_MS,
      partyKillSlackRatio: e.PARTY_KILL_SLACK_RATIO,
      partyPowerSlack: e.PARTY_POWER_SLACK,
      partyDamageMinRatio: e.PARTY_DAMAGE_MIN_RATIO,
      raidRewardMinHumans: e.RAID_REWARD_MIN_HUMANS,
      raidPracticePaysKills: e.RAID_PRACTICE_PAYS_KILLS,
    },
  };
}

let current: AppConfig | null = null;

/** 서버 기동(또는 테스트 setup)에서 한 번 호출한다. 이후 어디서든 getConfig()로 읽는다. */
export function initConfig(raw: NodeJS.ProcessEnv = process.env): AppConfig {
  current = loadConfig(raw);
  return current;
}

export function getConfig(): AppConfig {
  if (!current) current = loadConfig(process.env);
  return current;
}
