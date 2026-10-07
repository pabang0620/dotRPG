// 10단계(던전 클리어권 소탕, 운영 우편 캠페인) 환경변수. 값의 뜻은 Docs/server/phase10_sweep_mail.md 11절.
import { z } from 'zod';

const boolStr = z.enum(['true', 'false']).transform((v) => v === 'true');
const posInt = (def: number) => z.coerce.number().int().positive().default(def);

export const sweepShape = {
  SWEEP_ENABLED: boolStr.default(false),
  CAMPAIGN_DELIVERY_ENABLED: boolStr.default(false),
  CAMPAIGN_CACHE_SECONDS: posInt(30),
  CAMPAIGN_MAX_CAP_COUNT: posInt(200_000),
  CAMPAIGN_MAX_WINDOW_DAYS: posInt(60),
  /** 운영 필수(개발은 ADMIN_GRANT_MAX_GOLD와 같다) */
  CAMPAIGN_MAX_GOLD_PER_MAIL: z.coerce.number().int().positive().optional(),
  /** 운영 필수. 비어 있으면 골드 첨부 캠페인을 거절한다 */
  CAMPAIGN_MAX_GOLD_TOTAL: z.coerce.number().int().positive().optional(),
  CAMPAIGN_MAX_TICKETS_PER_MAIL: posInt(10),
  CAMPAIGN_MAX_TARGET_IDS: posInt(2000),
  CAMPAIGN_REVOKE_BATCH: posInt(2000),
  RATE_SWEEP_STATUS_PER_SEC: posInt(2),
  RATE_SWEEP_RUN_PER_SEC: posInt(2),
  RATE_SWEEP_ALL_PER_SEC: posInt(1),
  RATE_SWEEP_BUY_PER_SEC: posInt(1),
  RATE_SWEEP_CLAIM_PER_SEC: posInt(1),
};

export type SweepRaw = z.output<z.ZodObject<typeof sweepShape>>;

export interface SweepConfig {
  enabled: boolean;
  campaignDeliveryEnabled: boolean;
  campaignCacheSeconds: number;
  campaignMaxCapCount: number;
  campaignMaxWindowDays: number;
  campaignMaxGoldPerMail: number;
  /** null이면 골드 첨부 캠페인을 만들 수 없다 */
  campaignMaxGoldTotal: number | null;
  campaignMaxTicketsPerMail: number;
  campaignMaxTargetIds: number;
  campaignRevokeBatch: number;
  rate: { statusPerSec: number; runPerSec: number; allPerSec: number; buyPerSec: number; claimPerSec: number };
}

export function buildSweep(e: SweepRaw, ctx: { prod: boolean; adminGrantMaxGold: number }): SweepConfig {
  if (ctx.prod && e.CAMPAIGN_DELIVERY_ENABLED && (e.CAMPAIGN_MAX_GOLD_TOTAL === undefined || e.CAMPAIGN_MAX_GOLD_PER_MAIL === undefined)) {
    throw new Error('환경변수 검증 실패: 운영에서 CAMPAIGN_DELIVERY_ENABLED=true 에는 CAMPAIGN_MAX_GOLD_PER_MAIL 과 CAMPAIGN_MAX_GOLD_TOTAL 이 필요합니다');
  }
  return {
    enabled: e.SWEEP_ENABLED,
    campaignDeliveryEnabled: e.CAMPAIGN_DELIVERY_ENABLED,
    campaignCacheSeconds: e.CAMPAIGN_CACHE_SECONDS,
    campaignMaxCapCount: e.CAMPAIGN_MAX_CAP_COUNT,
    campaignMaxWindowDays: e.CAMPAIGN_MAX_WINDOW_DAYS,
    campaignMaxGoldPerMail: e.CAMPAIGN_MAX_GOLD_PER_MAIL ?? ctx.adminGrantMaxGold,
    campaignMaxGoldTotal: e.CAMPAIGN_MAX_GOLD_TOTAL ?? null,
    campaignMaxTicketsPerMail: e.CAMPAIGN_MAX_TICKETS_PER_MAIL,
    campaignMaxTargetIds: e.CAMPAIGN_MAX_TARGET_IDS,
    campaignRevokeBatch: e.CAMPAIGN_REVOKE_BATCH,
    rate: {
      statusPerSec: e.RATE_SWEEP_STATUS_PER_SEC,
      runPerSec: e.RATE_SWEEP_RUN_PER_SEC,
      allPerSec: e.RATE_SWEEP_ALL_PER_SEC,
      buyPerSec: e.RATE_SWEEP_BUY_PER_SEC,
      claimPerSec: e.RATE_SWEEP_CLAIM_PER_SEC,
    },
  };
}
