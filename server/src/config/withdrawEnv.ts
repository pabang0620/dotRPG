// 회원 탈퇴 환경변수. 값의 뜻은 Docs/server/phase12_withdrawal.md 13절.
// [법무 확인 전 기본값] 유예 일수, 보관 연수, 이월 보관 기간, 보류 상한은 모두 여기서만 바꾼다(코드에 숫자를 두지 않는다).
import { z } from 'zod';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);
const nonNegInt = (def: number) => z.coerce.number().int().min(0).default(def);

export const withdrawShape = {
  WITHDRAW_ENABLED: boolStr.default(true),
  WITHDRAW_GRACE_DAYS: posInt(30),
  WITHDRAW_CONFIRM_PHRASE: z.string().min(1).max(40).default('탈퇴합니다'),
  WITHDRAW_REAUTH_REQUIRED: boolStr.default(true),
  WITHDRAW_REQUEST_MAX_PER_30D: posInt(3),
  WITHDRAW_DEFER_MAX_DAYS: posInt(180),
  WITHDRAW_JOB_BATCH: posInt(20),
  /** 이월 표시용 HMAC 키(비밀). 운영에서 필수, 32자 이상. 개발·시험에서 없으면 JWT_SECRET에서 파생한다 */
  WITHDRAW_ID_HMAC_KEY: z.string().min(32, 'WITHDRAW_ID_HMAC_KEY는 32자 이상이어야 합니다').optional(),
  WITHDRAW_TOMBSTONE_MAX_DAYS: posInt(1095),
  WITHDRAW_REJOIN_COOLDOWN_DAYS: nonNegInt(0),
  WITHDRAW_RETAIN_PAID_YEARS: posInt(5),
  WITHDRAW_RETAIN_LEDGER_YEARS: posInt(5),
  WITHDRAW_DESTROY_ENABLED: boolStr.default(false),
  /** 역할 dotrpg_purge 접속 문자열(비밀). 5년 실삭제를 켤 때만 필요 */
  PURGE_DATABASE_URL: z.string().min(1).optional(),
  RATE_WITHDRAW_INFO_PER_MIN: posInt(10),
  RATE_WITHDRAW_PER_HOUR: posInt(3),
  RATE_WITHDRAW_IP_PER_HOUR: posInt(10),
  RATE_WITHDRAW_CANCEL_ACCOUNT_PER_HOUR: posInt(5),
  RATE_WITHDRAW_CANCEL_IP_PER_HOUR: posInt(10),
};

export type WithdrawRaw = z.output<z.ZodObject<typeof withdrawShape>>;

export interface WithdrawConfig {
  enabled: boolean;
  graceDays: number;
  confirmPhrase: string;
  reauthRequired: boolean;
  requestMaxPer30d: number;
  deferMaxDays: number;
  jobBatch: number;
  /** HMAC-SHA256 키(바이트). 이월 표시의 identity_hash 계산 전용 */
  idHmacKey: Buffer;
  tombstoneMaxDays: number;
  rejoinCooldownDays: number;
  retainPaidYears: number;
  retainLedgerYears: number;
  destroyEnabled: boolean;
  purgeDatabaseUrl: string | null;
  rate: { infoPerMin: number; perHour: number; ipPerHour: number; cancelAccountPerHour: number; cancelIpPerHour: number };
}

export function buildWithdraw(e: WithdrawRaw, ctx: { prod: boolean; jwtSecret: string }): WithdrawConfig {
  const fail = (m: string): never => {
    throw new Error(`환경변수 검증 실패: ${m}`);
  };
  if (ctx.prod) {
    if (e.WITHDRAW_ENABLED && e.WITHDRAW_ID_HMAC_KEY === undefined) fail('운영에서 WITHDRAW_ENABLED=true 에는 WITHDRAW_ID_HMAC_KEY(32자 이상)가 필요합니다');
    if (!e.WITHDRAW_REAUTH_REQUIRED) fail('운영에서는 WITHDRAW_REAUTH_REQUIRED=false 를 쓸 수 없습니다');
  }
  if (e.WITHDRAW_ID_HMAC_KEY !== undefined && e.WITHDRAW_ID_HMAC_KEY === ctx.jwtSecret) fail('WITHDRAW_ID_HMAC_KEY 는 JWT_SECRET 과 달라야 합니다');
  if (e.WITHDRAW_DESTROY_ENABLED && e.PURGE_DATABASE_URL === undefined) fail('WITHDRAW_DESTROY_ENABLED=true 에는 PURGE_DATABASE_URL(역할 dotrpg_purge)이 필요합니다');
  const key = e.WITHDRAW_ID_HMAC_KEY ?? `withdraw-id-dev:${ctx.jwtSecret}`;
  return {
    enabled: e.WITHDRAW_ENABLED,
    graceDays: e.WITHDRAW_GRACE_DAYS,
    confirmPhrase: e.WITHDRAW_CONFIRM_PHRASE,
    reauthRequired: e.WITHDRAW_REAUTH_REQUIRED,
    requestMaxPer30d: e.WITHDRAW_REQUEST_MAX_PER_30D,
    deferMaxDays: e.WITHDRAW_DEFER_MAX_DAYS,
    jobBatch: e.WITHDRAW_JOB_BATCH,
    idHmacKey: Buffer.from(key, 'utf8'),
    tombstoneMaxDays: e.WITHDRAW_TOMBSTONE_MAX_DAYS,
    rejoinCooldownDays: e.WITHDRAW_REJOIN_COOLDOWN_DAYS,
    retainPaidYears: e.WITHDRAW_RETAIN_PAID_YEARS,
    retainLedgerYears: e.WITHDRAW_RETAIN_LEDGER_YEARS,
    destroyEnabled: e.WITHDRAW_DESTROY_ENABLED,
    purgeDatabaseUrl: e.PURGE_DATABASE_URL ?? null,
    rate: {
      infoPerMin: e.RATE_WITHDRAW_INFO_PER_MIN,
      perHour: e.RATE_WITHDRAW_PER_HOUR,
      ipPerHour: e.RATE_WITHDRAW_IP_PER_HOUR,
      cancelAccountPerHour: e.RATE_WITHDRAW_CANCEL_ACCOUNT_PER_HOUR,
      cancelIpPerHour: e.RATE_WITHDRAW_CANCEL_IP_PER_HOUR,
    },
  };
}
