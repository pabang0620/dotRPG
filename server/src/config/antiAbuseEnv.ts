// 9단계(부정 행위 방지) 환경변수. 값의 뜻은 Docs/server/phase9_anti_abuse.md 3.5, 4.8, 5.6, 6, 7.6, 8.7, 10, 12.11.
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);
const nonNegInt = (def: number) => z.coerce.number().int().min(0).default(def);
const num = (def: number) => z.coerce.number().min(0).default(def);

export const MODES = ['off', 'log', 'enforce'] as const;
export type Mode = (typeof MODES)[number];
export type HoldMode = 'off' | 'log_only' | 'enforce';
const mode = (def: Mode) => z.enum(MODES).default(def);

export const antiAbuseShape = {
  // 3. 기기·세션
  DEVICE_INFO_REQUIRED: boolStr.default(false),
  DEVICE_HASH_PEPPER: z.string().optional(),
  SESSION_SINGLE_MODE: z.enum(['on', 'off']).default('on'),
  REFRESH_DEVICE_BIND: mode('log'),
  LOGIN_EVENT_RETENTION_DAYS: posInt(90),
  ACCOUNT_DEVICE_RETENTION_DAYS: posInt(180),
  // 4. 프레즌스
  DEVICE_MAX_CONCURRENT: posInt(2),
  DEVICE_LIMIT_MODE: mode('log'),
  IP_WATCH_CONCURRENT: posInt(4),
  PRESENCE_INTERVAL_SECONDS: posInt(30),
  PRESENCE_ONLINE_SECONDS: posInt(90),
  PRESENCE_MIN_GAP_SECONDS: posInt(5),
  PLAY_TIME_MAX_GAP_SECONDS: posInt(60),
  PRESENCE_MAP_GRACE_SECONDS: nonNegInt(20),
  PRESENCE_STALE_REJECT_SECONDS: posInt(120),
  PRESENCE_KILL_MODE: mode('log'),
  FIELD_BOSS_MIN_PRESENCE_SECONDS: nonNegInt(30),
  AUTOPLAY_UNATTENDED_MAX_SECONDS: posInt(2400),
  PLAY_TIME_RETENTION_DAYS: posInt(35),
  RATE_PRESENCE_PER_10S: posInt(3),
  RATE_PRESENCE_PER_MIN: posInt(12),
  // 5. 기여
  CONTRIBUTION_MODE: mode('enforce'),
  RAID_MIN_SHARE: z.coerce.number().min(0).max(1).default(0.05),
  RAID_MIN_HITS: nonNegInt(40),
  DUNGEON_MIN_SHARE: z.coerce.number().min(0).max(1).default(0.05),
  DUNGEON_MIN_HITS: nonNegInt(40),
  CONTRIBUTION_DISPUTE_RATIO: z.coerce.number().min(1).default(2.0),
  DUNGEON_UNDERLEVEL_GAP: posInt(10),
  DUNGEON_UNDERLEVEL_FACTOR: z.coerce.number().gt(0).max(1).default(0.25),
  DUNGEON_UNDERLEVEL_STEP: num(0.05),
  DUNGEON_UNDERLEVEL_MIN: z.coerce.number().gt(0).max(1).default(0.05),
  // 7. 경매
  AUCTION_BUYER_MIN_LEVEL: nonNegInt(10),
  AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS: nonNegInt(7),
  AUCTION_SHOP_CEIL_MULT: posInt(3),
  AUCTION_FLAG_CEIL_RATIO: z.coerce.number().gt(0).max(1).default(0.9),
  AUCTION_FLAG_NEW_BUYER_DAYS: posInt(14),
  AUCTION_FLAG_MIN_PRICE: posInt(100_000),
  AUCTION_FLAG_DEVICE_DAYS: posInt(30),
  AUCTION_FLAG_IP_DAYS: posInt(7),
  AUCTION_FLAG_PAIR_REPEAT: posInt(3),
  AUCTION_BLOCK_SAME_DEVICE: boolStr.default(false),
  AUCTION_NET_RATIO: num(0.5),
  AUCTION_NET_FLOOR: nonNegInt(100_000),
  // 8. 전직·각성
  CAREER_SERVER_TRUTH: mode('enforce'),
  CAREER_PROMOTE_MIN_LEVEL: posInt(15),
  AWAKEN_STAGE_MIN_GAP_SECONDS: nonNegInt(10),
  AWAKEN_TRIAL_MIN_SECONDS: nonNegInt(6),
  AWAKEN_TRIAL_MAX_SECONDS: posInt(120),
  RATE_CAREER_PER_SEC: posInt(1),
  // 10. 이름
  NAME_RESERVED_MODE: z.enum(['off', 'enforce']).default('enforce'),
  // 12. 경제 속도 정지
  ECONOMY_HOLD_MODE: z.enum(['off', 'log_only', 'enforce']).default('log_only'),
  INCOME_MAX_KILLS_PER_MIN: posInt(30),
  ECONOMY_HOLD_MULT: z.coerce.number().min(1).default(3),
  ECONOMY_HOLD_MIN_XP: nonNegInt(10_000),
  ECONOMY_HOLD_MIN_GOLD_EQ: nonNegInt(50_000),
  ECONOMY_HOLD_MIN_ORE: nonNegInt(200),
  ECONOMY_HOLD_MIN_ESSENCE: nonNegInt(60),
  ECONOMY_HOLD_MIN_UNIQUE: nonNegInt(3),
  ECONOMY_HOLD_CHECK_SECONDS: posInt(30),
  ECONOMY_HOLD_SWEEP_MINUTES: posInt(10),
  ECONOMY_HOLD_SHADOW_DEDUPE_HOURS: posInt(6),
  HOLD_LINK_DEVICE_DAYS: posInt(14),
  HOLD_LINK_DEVICE_MAX_ACCOUNTS: posInt(6),
  HOLD_LINK_MAX_ACCOUNTS: posInt(10),
};

export type AntiAbuseRaw = z.output<z.ZodObject<typeof antiAbuseShape>>;

export interface AntiAbuseConfig {
  deviceInfoRequired: boolean;
  devicePepper: string;
  sessionSingle: boolean;
  refreshDeviceBind: Mode;
  loginEventDays: number;
  accountDeviceDays: number;
  device: { maxConcurrent: number; mode: Mode; ipWatchConcurrent: number };
  presence: {
    intervalSeconds: number;
    onlineSeconds: number;
    minGapSeconds: number;
    maxGapSeconds: number;
    mapGraceSeconds: number;
    staleRejectSeconds: number;
    killMode: Mode;
    fieldBossMinSeconds: number;
    unattendedMaxSeconds: number;
    retentionDays: number;
    rate10s: number;
    rateMin: number;
  };
  contribution: {
    mode: Mode;
    raidMinShare: number;
    raidMinHits: number;
    dungeonMinShare: number;
    dungeonMinHits: number;
    disputeRatio: number;
    underlevelGap: number;
    underlevelFactor: number;
    underlevelStep: number;
    underlevelMin: number;
  };
  auction: {
    buyerMinLevel: number;
    buyerMinAccountAgeDays: number;
    shopCeilMult: number;
    flagCeilRatio: number;
    flagNewBuyerDays: number;
    flagMinPrice: number;
    flagDeviceDays: number;
    flagIpDays: number;
    flagPairRepeat: number;
    blockSameDevice: boolean;
    netRatio: number;
    netFloor: number;
  };
  career: {
    serverTruth: Mode;
    promoteMinLevel: number;
    stageMinGapSeconds: number;
    trialMinSeconds: number;
    trialMaxSeconds: number;
    ratePerSec: number;
  };
  nameMode: 'off' | 'enforce';
  hold: {
    mode: HoldMode;
    killsPerMin: number;
    mult: number;
    minXp: number;
    minGoldEq: number;
    minOre: number;
    minEssence: number;
    minUnique: number;
    checkSeconds: number;
    sweepMinutes: number;
    shadowDedupeHours: number;
    linkDeviceDays: number;
    linkDeviceMaxAccounts: number;
    linkMaxAccounts: number;
  };
}

const fail = (msg: string): never => {
  throw new Error(`환경변수 검증 실패: ${msg}`);
};

function isWeak(v: string): boolean {
  if (/^(.)\1+$/.test(v)) return true;
  return /(changeme|change-me|replace|example|placeholder|secret-secret|your[-_]?secret|password)/i.test(v);
}

/** income_caps.json의 schema 값을 읽는다. 파일이 없거나 깨졌으면 null */
export function readIncomeCapsSchema(gameDataDir: string): number | null {
  try {
    const raw = JSON.parse(fs.readFileSync(path.join(gameDataDir, 'income_caps.json'), 'utf8')) as { schema?: unknown };
    return typeof raw.schema === 'number' ? raw.schema : null;
  } catch {
    return null;
  }
}

/** 9단계 설정 조립과 기동 검사(14절). 기능이 하나라도 켜지면 DEVICE_HASH_PEPPER가 필요하다(개발·시험은 JWT 비밀에서 파생) */
export function buildAntiAbuse(
  e: AntiAbuseRaw,
  ctx: { prod: boolean; jwtSecret: string; gameDataDir: string; fieldCarry: { slack: number; hardGap: number }; warn: (m: string) => void },
): AntiAbuseConfig {
  let pepper: string;
  if (e.DEVICE_HASH_PEPPER === undefined) {
    if (ctx.prod) fail('운영에는 DEVICE_HASH_PEPPER(32자 이상)가 필요합니다');
    pepper = createHash('sha256').update(`device-pepper:${ctx.jwtSecret}`).digest('base64url');
  } else {
    pepper = e.DEVICE_HASH_PEPPER;
    if (pepper.length < 32) fail('DEVICE_HASH_PEPPER 는 32자 이상이어야 합니다');
    if (pepper === ctx.jwtSecret) fail('DEVICE_HASH_PEPPER 는 JWT_SECRET 과 달라야 합니다');
    if (isWeak(pepper)) fail('DEVICE_HASH_PEPPER 가 자리표시 값이거나 반복 문자입니다');
  }
  if (ctx.prod && (e.AUCTION_BUYER_MIN_LEVEL === 0 || e.AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS === 0)) {
    fail('운영에서는 AUCTION_BUYER_MIN_LEVEL 과 AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS 를 0으로 둘 수 없습니다');
  }
  if (ctx.fieldCarry.hardGap <= ctx.fieldCarry.slack) fail('FIELD_CARRY_HARD_GAP 은 FIELD_CARRY_SLACK 보다 커야 합니다');
  const capsSchema = readIncomeCapsSchema(ctx.gameDataDir);
  if (e.ECONOMY_HOLD_MODE === 'enforce' && capsSchema !== 1) {
    fail('ECONOMY_HOLD_MODE=enforce 에는 server/data/income_caps.json(schema 1)이 필요합니다(Tools/balance/theory_income.py 로 만든다)');
  }
  if (e.ECONOMY_HOLD_MODE === 'log_only' && capsSchema !== 1) {
    ctx.warn('income_caps.json 이 없거나 schema 가 다릅니다: 경제 속도 감시는 평가를 건너뜁니다');
  }
  return {
    deviceInfoRequired: e.DEVICE_INFO_REQUIRED,
    devicePepper: pepper,
    sessionSingle: e.SESSION_SINGLE_MODE === 'on',
    refreshDeviceBind: e.REFRESH_DEVICE_BIND,
    loginEventDays: e.LOGIN_EVENT_RETENTION_DAYS,
    accountDeviceDays: e.ACCOUNT_DEVICE_RETENTION_DAYS,
    device: { maxConcurrent: e.DEVICE_MAX_CONCURRENT, mode: e.DEVICE_LIMIT_MODE, ipWatchConcurrent: e.IP_WATCH_CONCURRENT },
    presence: {
      intervalSeconds: e.PRESENCE_INTERVAL_SECONDS,
      onlineSeconds: e.PRESENCE_ONLINE_SECONDS,
      minGapSeconds: e.PRESENCE_MIN_GAP_SECONDS,
      maxGapSeconds: e.PLAY_TIME_MAX_GAP_SECONDS,
      mapGraceSeconds: e.PRESENCE_MAP_GRACE_SECONDS,
      staleRejectSeconds: e.PRESENCE_STALE_REJECT_SECONDS,
      killMode: e.PRESENCE_KILL_MODE,
      fieldBossMinSeconds: e.FIELD_BOSS_MIN_PRESENCE_SECONDS,
      unattendedMaxSeconds: e.AUTOPLAY_UNATTENDED_MAX_SECONDS,
      retentionDays: e.PLAY_TIME_RETENTION_DAYS,
      rate10s: e.RATE_PRESENCE_PER_10S,
      rateMin: e.RATE_PRESENCE_PER_MIN,
    },
    contribution: {
      mode: e.CONTRIBUTION_MODE,
      raidMinShare: e.RAID_MIN_SHARE,
      raidMinHits: e.RAID_MIN_HITS,
      dungeonMinShare: e.DUNGEON_MIN_SHARE,
      dungeonMinHits: e.DUNGEON_MIN_HITS,
      disputeRatio: e.CONTRIBUTION_DISPUTE_RATIO,
      underlevelGap: e.DUNGEON_UNDERLEVEL_GAP,
      underlevelFactor: e.DUNGEON_UNDERLEVEL_FACTOR,
      underlevelStep: e.DUNGEON_UNDERLEVEL_STEP,
      underlevelMin: e.DUNGEON_UNDERLEVEL_MIN,
    },
    auction: {
      buyerMinLevel: e.AUCTION_BUYER_MIN_LEVEL,
      buyerMinAccountAgeDays: e.AUCTION_BUYER_MIN_ACCOUNT_AGE_DAYS,
      shopCeilMult: e.AUCTION_SHOP_CEIL_MULT,
      flagCeilRatio: e.AUCTION_FLAG_CEIL_RATIO,
      flagNewBuyerDays: e.AUCTION_FLAG_NEW_BUYER_DAYS,
      flagMinPrice: e.AUCTION_FLAG_MIN_PRICE,
      flagDeviceDays: e.AUCTION_FLAG_DEVICE_DAYS,
      flagIpDays: e.AUCTION_FLAG_IP_DAYS,
      flagPairRepeat: e.AUCTION_FLAG_PAIR_REPEAT,
      blockSameDevice: e.AUCTION_BLOCK_SAME_DEVICE,
      netRatio: e.AUCTION_NET_RATIO,
      netFloor: e.AUCTION_NET_FLOOR,
    },
    career: {
      serverTruth: e.CAREER_SERVER_TRUTH,
      promoteMinLevel: e.CAREER_PROMOTE_MIN_LEVEL,
      stageMinGapSeconds: e.AWAKEN_STAGE_MIN_GAP_SECONDS,
      trialMinSeconds: e.AWAKEN_TRIAL_MIN_SECONDS,
      trialMaxSeconds: e.AWAKEN_TRIAL_MAX_SECONDS,
      ratePerSec: e.RATE_CAREER_PER_SEC,
    },
    nameMode: e.NAME_RESERVED_MODE,
    hold: {
      mode: e.ECONOMY_HOLD_MODE,
      killsPerMin: e.INCOME_MAX_KILLS_PER_MIN,
      mult: e.ECONOMY_HOLD_MULT,
      minXp: e.ECONOMY_HOLD_MIN_XP,
      minGoldEq: e.ECONOMY_HOLD_MIN_GOLD_EQ,
      minOre: e.ECONOMY_HOLD_MIN_ORE,
      minEssence: e.ECONOMY_HOLD_MIN_ESSENCE,
      minUnique: e.ECONOMY_HOLD_MIN_UNIQUE,
      checkSeconds: e.ECONOMY_HOLD_CHECK_SECONDS,
      sweepMinutes: e.ECONOMY_HOLD_SWEEP_MINUTES,
      shadowDedupeHours: e.ECONOMY_HOLD_SHADOW_DEDUPE_HOURS,
      linkDeviceDays: e.HOLD_LINK_DEVICE_DAYS,
      linkDeviceMaxAccounts: e.HOLD_LINK_DEVICE_MAX_ACCOUNTS,
      linkMaxAccounts: e.HOLD_LINK_MAX_ACCOUNTS,
    },
  };
}
