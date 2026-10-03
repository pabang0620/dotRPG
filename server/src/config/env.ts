import path from 'node:path';
import { z } from 'zod';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);

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
});

export interface AppConfig {
  nodeEnv: 'development' | 'test' | 'production';
  port: number;
  databaseUrl: string;
  jwtSecret: string;
  minClientVersion: string;
  authDevEnabled: boolean;
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
  };
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

export function loadConfig(raw: NodeJS.ProcessEnv = process.env): AppConfig {
  const parsed = envSchema.safeParse(clean(raw));
  if (!parsed.success) {
    const detail = parsed.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
    throw new Error(`환경변수 검증 실패: ${detail}`);
  }
  const e = parsed.data;
  return {
    nodeEnv: e.NODE_ENV,
    port: e.PORT,
    databaseUrl: e.DATABASE_URL,
    jwtSecret: e.JWT_SECRET,
    minClientVersion: e.MIN_CLIENT_VERSION,
    authDevEnabled: e.AUTH_DEV_ENABLED ?? e.NODE_ENV !== 'production',
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
    },
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
