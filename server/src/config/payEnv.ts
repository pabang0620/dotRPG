// 11단계(별조각 Steam 결제) 환경변수. 값의 뜻은 Docs/server/phase11_payments.md 16절.
// [운영 전 확정] 한도·가격·묶음·속도 제한 숫자는 모두 시작값이다(문서 10.2, 16절). 상품 가격표가 정해지면 환경변수로 조정한다.
// 결제 기능 플래그(PAYMENTS_ENABLED)는 기본 꺼짐이다.
import { z } from 'zod';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);
const nonNegInt = (def: number) => z.coerce.number().int().min(0).default(def);

export const payShape = {
  PAYMENTS_ENABLED: boolStr.default(false),
  PAYMENTS_STEAM_MODE: z.enum(['off', 'mock', 'sandbox', 'live']).default('off'),
  /** 결제 API 퍼블리셔 키(비밀). 인증용 STEAM_WEB_API_KEY와 분리한다. sandbox|live에서 필수 */
  STEAM_PUBLISHER_API_KEY: z.string().min(1).optional(),
  /** TODO [확인] Steamworks 퍼블리셔 API 호스트(7.8-1). 기본값은 문서에서 확인한 값으로 교체한다 */
  STEAM_PARTNER_API_BASE: z.url().default('https://partner.steam-api.com'),
  PAYMENT_QUOTE_SECRET: z.string().optional(),
  PAYMENT_QUOTE_TTL_SECONDS: posInt(600),
  PAY_ORDER_EXPIRE_MINUTES: posInt(15),
  PAY_LANGUAGE: z.string().min(2).max(10).default('ko'),
  PAY_INIT_LOST_MINUTES: posInt(10),
  PAY_FINALIZE_MAX_ATTEMPTS: posInt(5),
  PAY_SYNC_MIN_INTERVAL_SECONDS: nonNegInt(2),
  PAY_STEAM_MAX_CALLS_PER_MIN: posInt(60),
  PAY_WATCH_DAYS: posInt(180),
  PAY_REPORT_DAYS: posInt(7),
  PAY_LIMIT_DAILY_STARS: posInt(10_000),
  PAY_LIMIT_MONTHLY_STARS: posInt(50_000),
  PAY_LIMIT_NEW_DAILY_STARS: posInt(3_000),
  PAY_LIMIT_NEW_MONTHLY_STARS: posInt(10_000),
  PAY_LIMIT_RESTRICTED_DAILY_STARS: posInt(1_000),
  PAY_LIMIT_RESTRICTED_MONTHLY_STARS: posInt(3_000),
  PAY_NEW_ACCOUNT_DAYS: nonNegInt(14),
  PAY_MAX_ORDERS_PER_HOUR: posInt(3),
  PAY_MIN_ORDER_GAP_SECONDS: nonNegInt(60),
  PAY_FAIL_COOLDOWN_THRESHOLD: posInt(5),
  PAY_SHARED_DEVICE_ACCOUNTS: posInt(3),
  PAY_COUNTRY_CHANGE_MAX: nonNegInt(1),
  PAY_REFUND_SPEND_RATIO: z.coerce.number().gt(0).max(1).default(0.5),
  PAY_REFUND_RESTRICT_COUNT: posInt(2),
  PAY_REFUND_BLOCK_COUNT: posInt(3),
  PAY_BURN_MINUTES: posInt(10),
  PAY_IP_RETENTION_DAYS: posInt(180),
  STAR_SPEND_DAILY_CAP: nonNegInt(30_000),
  STAR_RATES_ACK_REQUIRED: boolStr.default(false),
  RATE_PAY_READ_PER_MIN: posInt(30),
  RATE_PAY_ORDER_PER_MIN: posInt(3),
  RATE_PAY_ORDER_IP_PER_MIN: posInt(10),
  RATE_PAY_SYNC_PER_MIN: posInt(30),
  RATE_STARSHOP_SPEND_PER_MIN: posInt(30),
  PAY_ADMIN_GRANT_MAX_STARS: posInt(5_000),
  PAY_ADMIN_GRANT_DAILY_MAX_STARS_PER_ADMIN: posInt(20_000),
  PAY_ADMIN_GRANT_DAILY_MAX_STARS_TOTAL: posInt(50_000),
  PAY_ADMIN_GRANT_PENDING_HOURS: posInt(24),
};

export type PayRaw = z.output<z.ZodObject<typeof payShape>>;

export interface TierLimits {
  daily: number;
  monthly: number;
}

export interface PayConfig {
  enabled: boolean;
  mode: 'off' | 'mock' | 'sandbox' | 'live';
  publisherKey: string | null;
  partnerApiBase: string;
  quoteSecret: string | null;
  quoteTtlSeconds: number;
  orderExpireMinutes: number;
  language: string;
  initLostMinutes: number;
  finalizeMaxAttempts: number;
  syncMinIntervalSeconds: number;
  steamMaxCallsPerMin: number;
  watchDays: number;
  reportDays: number;
  limits: { standard: TierLimits; new: TierLimits; restricted: TierLimits };
  newAccountDays: number;
  maxOrdersPerHour: number;
  minOrderGapSeconds: number;
  failCooldownThreshold: number;
  sharedDeviceAccounts: number;
  countryChangeMax: number;
  refundSpendRatio: number;
  refundRestrictCount: number;
  refundBlockCount: number;
  burnMinutes: number;
  ipRetentionDays: number;
  spendDailyCap: number;
  ratesAckRequired: boolean;
  rate: { readPerMin: number; orderPerMin: number; orderIpPerMin: number; syncPerMin: number; spendPerMin: number };
  adminGrant: { maxStars: number; dailyPerAdmin: number; dailyTotal: number; pendingHours: number };
}

export interface PayCtx {
  prod: boolean;
  jwtSecret: string;
  steamAuthMode: 'off' | 'mock' | 'web_api';
  deployStage: 'test' | 'live';
  warn: (msg: string) => void;
}

const fail = (m: string): never => {
  throw new Error(`환경변수 검증 실패: ${m}`);
};

/** 16절 기동 검사 1~3번과 값 변환. 상품표·확률표 검사(4번)는 서버 기동 코드(initPayments)가 한다 */
export function buildPay(e: PayRaw, ctx: PayCtx): PayConfig {
  const live = e.PAYMENTS_STEAM_MODE === 'sandbox' || e.PAYMENTS_STEAM_MODE === 'live';
  if (live && !e.STEAM_PUBLISHER_API_KEY) fail('PAYMENTS_STEAM_MODE=sandbox|live 에는 STEAM_PUBLISHER_API_KEY 가 필요합니다');
  if (e.PAYMENTS_ENABLED) {
    if (e.PAYMENTS_STEAM_MODE === 'off') fail('PAYMENTS_ENABLED=true 에는 PAYMENTS_STEAM_MODE 가 mock(시험), sandbox, live 중 하나여야 합니다');
    if (!e.PAYMENT_QUOTE_SECRET || e.PAYMENT_QUOTE_SECRET.length < 32) fail('PAYMENTS_ENABLED=true 에는 32자 이상의 PAYMENT_QUOTE_SECRET 이 필요합니다');
    // 문서 16절은 sandbox|live만 허용하지만, 개발·시험의 가짜 Steam(mock)은 비운영에서 허용한다(구현 기록 20절)
    if (live && ctx.steamAuthMode !== 'web_api') fail('결제(sandbox|live)에는 STEAM_AUTH_MODE=web_api 가 필요합니다');
    if (live && !e.STAR_RATES_ACK_REQUIRED) fail('결제(sandbox|live)에는 STAR_RATES_ACK_REQUIRED=true 가 필요합니다');
  }
  if (e.PAYMENT_QUOTE_SECRET !== undefined && e.PAYMENT_QUOTE_SECRET === ctx.jwtSecret) fail('PAYMENT_QUOTE_SECRET 은 JWT_SECRET 과 달라야 합니다');
  if (ctx.prod) {
    if (e.PAYMENTS_STEAM_MODE === 'mock') fail('운영에서는 PAYMENTS_STEAM_MODE=mock 을 쓸 수 없습니다');
    if (e.PAYMENTS_STEAM_MODE !== 'off') {
      if (ctx.deployStage === 'live' && e.PAYMENTS_STEAM_MODE !== 'live') fail('DEPLOY_STAGE=live 에서는 PAYMENTS_STEAM_MODE=live 여야 합니다');
      if (ctx.deployStage === 'test' && e.PAYMENTS_STEAM_MODE !== 'sandbox') fail('DEPLOY_STAGE=test 에서는 PAYMENTS_STEAM_MODE=sandbox 여야 합니다');
    }
  }
  const std = { daily: e.PAY_LIMIT_DAILY_STARS, monthly: e.PAY_LIMIT_MONTHLY_STARS };
  const nw = { daily: e.PAY_LIMIT_NEW_DAILY_STARS, monthly: e.PAY_LIMIT_NEW_MONTHLY_STARS };
  const rs = { daily: e.PAY_LIMIT_RESTRICTED_DAILY_STARS, monthly: e.PAY_LIMIT_RESTRICTED_MONTHLY_STARS };
  if (!(rs.daily <= nw.daily && nw.daily <= std.daily && rs.monthly <= nw.monthly && nw.monthly <= std.monthly)) {
    fail('결제 한도는 restricted <= new <= standard 여야 합니다(일·월 각각)');
  }
  if (!(rs.daily <= rs.monthly && nw.daily <= nw.monthly && std.daily <= std.monthly)) fail('결제 일일 한도는 월 한도 이하여야 합니다');
  if (e.PAYMENTS_ENABLED && ctx.prod && e.PAY_ADMIN_GRANT_MAX_STARS > e.PAY_ADMIN_GRANT_DAILY_MAX_STARS_PER_ADMIN) {
    ctx.warn('PAY_ADMIN_GRANT_MAX_STARS 가 관리자별 일일 상한보다 큽니다');
  }
  return {
    enabled: e.PAYMENTS_ENABLED,
    mode: e.PAYMENTS_STEAM_MODE,
    publisherKey: e.STEAM_PUBLISHER_API_KEY ?? null,
    partnerApiBase: e.STEAM_PARTNER_API_BASE,
    quoteSecret: e.PAYMENT_QUOTE_SECRET ?? null,
    quoteTtlSeconds: e.PAYMENT_QUOTE_TTL_SECONDS,
    orderExpireMinutes: e.PAY_ORDER_EXPIRE_MINUTES,
    language: e.PAY_LANGUAGE,
    initLostMinutes: e.PAY_INIT_LOST_MINUTES,
    finalizeMaxAttempts: e.PAY_FINALIZE_MAX_ATTEMPTS,
    syncMinIntervalSeconds: e.PAY_SYNC_MIN_INTERVAL_SECONDS,
    steamMaxCallsPerMin: e.PAY_STEAM_MAX_CALLS_PER_MIN,
    watchDays: e.PAY_WATCH_DAYS,
    reportDays: e.PAY_REPORT_DAYS,
    limits: { standard: std, new: nw, restricted: rs },
    newAccountDays: e.PAY_NEW_ACCOUNT_DAYS,
    maxOrdersPerHour: e.PAY_MAX_ORDERS_PER_HOUR,
    minOrderGapSeconds: e.PAY_MIN_ORDER_GAP_SECONDS,
    failCooldownThreshold: e.PAY_FAIL_COOLDOWN_THRESHOLD,
    sharedDeviceAccounts: e.PAY_SHARED_DEVICE_ACCOUNTS,
    countryChangeMax: e.PAY_COUNTRY_CHANGE_MAX,
    refundSpendRatio: e.PAY_REFUND_SPEND_RATIO,
    refundRestrictCount: e.PAY_REFUND_RESTRICT_COUNT,
    refundBlockCount: e.PAY_REFUND_BLOCK_COUNT,
    burnMinutes: e.PAY_BURN_MINUTES,
    ipRetentionDays: e.PAY_IP_RETENTION_DAYS,
    spendDailyCap: e.STAR_SPEND_DAILY_CAP,
    ratesAckRequired: e.STAR_RATES_ACK_REQUIRED,
    rate: {
      readPerMin: e.RATE_PAY_READ_PER_MIN,
      orderPerMin: e.RATE_PAY_ORDER_PER_MIN,
      orderIpPerMin: e.RATE_PAY_ORDER_IP_PER_MIN,
      syncPerMin: e.RATE_PAY_SYNC_PER_MIN,
      spendPerMin: e.RATE_STARSHOP_SPEND_PER_MIN,
    },
    adminGrant: {
      maxStars: e.PAY_ADMIN_GRANT_MAX_STARS,
      dailyPerAdmin: e.PAY_ADMIN_GRANT_DAILY_MAX_STARS_PER_ADMIN,
      dailyTotal: e.PAY_ADMIN_GRANT_DAILY_MAX_STARS_TOTAL,
      pendingHours: e.PAY_ADMIN_GRANT_PENDING_HOURS,
    },
  };
}
