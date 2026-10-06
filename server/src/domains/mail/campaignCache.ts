// 진행 중인 캠페인 메모리 캐시(설계 7.2 2번). 후보가 없으면 DB를 전혀 건드리지 않는다(평소 비용 0).
// CAMPAIGN_CACHE_SECONDS마다 다시 읽고, 배달 트랜잭션이 상한·취소를 만나면 즉시 다시 읽게 표시한다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import type { Bind } from '../economy/economyRepository';
import type { AttachKind } from './mailAttachments';

export interface CampaignTarget {
  all?: boolean;
  min_account_level?: number;
  max_account_level?: number;
  min_level?: number;
  max_level?: number;
  classes?: string[];
  account_created_from?: string;
  account_created_to?: string;
  last_login_before?: string;
  account_ids?: string[];
}

export interface CachedCampaign {
  id: number;
  uuid: string;
  title: string;
  body: string;
  category: string;
  deliveryUnit: 'account' | 'character';
  target: CampaignTarget;
  mailDays: number;
  startsAt: Date;
  endsAt: Date;
  capCount: number;
  issuedCount: number;
  attachments: { slot: number; kind: AttachKind; itemKey: string | null; amount: number; bind: Bind | null }[];
}

interface RawCampaign {
  id: string;
  uuid: string;
  title: string;
  body: string;
  category: string;
  delivery_unit: 'account' | 'character';
  target: CampaignTarget;
  mail_days: number;
  starts_at: Date;
  ends_at: Date;
  cap_count: number;
  issued_count: number;
}

interface RawAtt {
  campaign_id: string;
  slot: number;
  kind: AttachKind;
  item_key: string | null;
  amount: string;
  bind: Bind | null;
}

let cache: { loadedAt: number; rows: CachedCampaign[] } | null = null;

/** 다음 조회에서 DB를 다시 읽게 한다(상한 도달, 승인, 취소, 테스트) */
export function invalidateCampaignCache(): void {
  cache = null;
}

/** 지금 배달 중인 캠페인 후보(승인됨, 기간 안, 상한 미달). 서버 시각으로 거른다 */
export async function activeCampaigns(now: Date): Promise<CachedCampaign[]> {
  const ttl = getConfig().sweep.campaignCacheSeconds * 1000;
  if (!cache || Date.now() - cache.loadedAt >= ttl) {
    const db = getPool();
    // 기간은 캐시 수명(최대 TTL) 동안 바뀔 수 있어 약간 넓게 읽고 아래에서 now로 다시 거른다
    const r = await db.query<RawCampaign>(
      `SELECT id, uuid, title, body, category, delivery_unit, target, mail_days, starts_at, ends_at, cap_count, issued_count
         FROM mail_campaigns WHERE status = 'active' AND issued_count < cap_count ORDER BY id`,
    );
    const ids = r.rows.map((x) => Number(x.id));
    const atts = ids.length
      ? await db.query<RawAtt>(
          'SELECT campaign_id, slot, kind, item_key, amount, bind FROM mail_campaign_attachments WHERE campaign_id = ANY($1::bigint[]) ORDER BY campaign_id, slot',
          [ids],
        )
      : { rows: [] as RawAtt[] };
    cache = {
      loadedAt: Date.now(),
      rows: r.rows.map((x) => ({
        id: Number(x.id),
        uuid: x.uuid,
        title: x.title,
        body: x.body,
        category: x.category,
        deliveryUnit: x.delivery_unit,
        target: x.target,
        mailDays: x.mail_days,
        startsAt: x.starts_at,
        endsAt: x.ends_at,
        capCount: x.cap_count,
        issuedCount: x.issued_count,
        attachments: atts.rows
          .filter((a) => Number(a.campaign_id) === Number(x.id))
          .map((a) => ({ slot: a.slot, kind: a.kind, itemKey: a.item_key, amount: Number(a.amount), bind: a.bind })),
      })),
    };
  }
  return cache.rows.filter((c) => c.startsAt.getTime() <= now.getTime() && now.getTime() < c.endsAt.getTime());
}
