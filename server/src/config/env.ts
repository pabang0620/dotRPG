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
