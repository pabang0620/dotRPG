// 캠페인 우편 배달(설계 7.2): 대상이 접속(프레즌스 진입)하거나 우편 요약을 부를 때 계정당 한 통을 그때 만든다.
// 한 통은 한 트랜잭션이다. 마지막 문장이 issued_count < cap_count 조건부 UPDATE라 총 지급 상한을 DB가 강제한다.
// 배달 실패는 원래 요청에 영향을 주지 않고 로그만 남긴다.
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { insertItemLedger } from '../economy/economyRepository';
import { announceMail } from './mailNotify';
import { activeCampaigns, invalidateCampaignCache, type CachedCampaign } from './campaignCache';
import { insertAttachments } from './mailAttachments';
import { insertMail, type SystemCode } from './mailRepository';

export interface Facts {
  accountUuid: string;
  createdAt: Date;
  lastLoginAt: Date | null;
  /** The login before the current one (0022): delivery runs right after a login, so this is when they last played. */
  prevLoginAt: Date | null;
  accountMaxLevel: number;
  charLevel: number;
  charClass: string;
  charUuid: string;
}

async function loadFacts(accountId: number, characterId: number): Promise<Facts | null> {
  const r = await getPool().query<{
    account_uuid: string;
    created_at: Date;
    last_login_at: Date | null;
    prev_login_at: Date | null;
    max_level: number;
    level: number;
    class: string;
    char_uuid: string;
  }>(
    `SELECT a.uuid AS account_uuid, a.created_at, a.last_login_at, a.prev_login_at,
            (SELECT max(level) FROM characters WHERE account_id = a.id) AS max_level,
            c.level, c.class, c.uuid AS char_uuid
       FROM accounts a JOIN characters c ON c.id = $2 AND c.account_id = a.id AND c.deleted_at IS NULL
      WHERE a.id = $1 AND a.deleted_at IS NULL`,
    [accountId, characterId],
  );
  const x = r.rows[0];
  return x
    ? { accountUuid: x.account_uuid, createdAt: x.created_at, lastLoginAt: x.last_login_at, prevLoginAt: x.prev_login_at, accountMaxLevel: x.max_level, charLevel: x.level, charClass: x.class, charUuid: x.char_uuid }
    : null;
}

/** 대상 조건(7.3): 모든 조건은 AND. all이면 무조건 대상. 메모리에서 판정한다 */
export function matchesTarget(c: Pick<CachedCampaign, 'target'>, f: Facts): boolean {
  const t = c.target;
  if (t.all === true) return true;
  if (t.min_account_level !== undefined && f.accountMaxLevel < t.min_account_level) return false;
  if (t.max_account_level !== undefined && f.accountMaxLevel > t.max_account_level) return false;
  if (t.min_level !== undefined && f.charLevel < t.min_level) return false;
  if (t.max_level !== undefined && f.charLevel > t.max_level) return false;
  if (t.classes !== undefined && !t.classes.includes(f.charClass)) return false;
  if (t.account_created_from !== undefined && f.createdAt.getTime() < Date.parse(t.account_created_from)) return false;
  if (t.account_created_to !== undefined && f.createdAt.getTime() > Date.parse(t.account_created_to)) return false;
  // 휴면 복귀: 이번 로그인 바로 앞의 접속이 기준 시각보다 이전이어야 한다(0022 prev_login_at, 첫 로그인은 해당 없음)
  if (t.last_login_before !== undefined && !(f.prevLoginAt !== null && f.prevLoginAt.getTime() < Date.parse(t.last_login_before))) return false;
  if (t.account_ids !== undefined && !t.account_ids.includes(f.accountUuid)) return false;
  return true;
}

const DELIVERY_UNIQUE = 'mail_campaign_deliveries_campaign_id_delivery_key_key';
const MAIL_ONCE_UNIQUE = 'mails_campaign_once';

/** 상한 도달·취소·기간 종료로 마지막 문장이 0행이면 던져 전체를 롤백한다 */
class CapReached extends Error {}

type DeliverResult = 'delivered' | 'already' | 'closed';

async function deliverOne(c: CachedCampaign, accountId: number, f: Facts, characterId: number, now: Date): Promise<DeliverResult> {
  const key = c.deliveryUnit === 'account' ? accountId : characterId;
  const seen = await getPool().query('SELECT 1 FROM mail_campaign_deliveries WHERE campaign_id = $1 AND delivery_key = $2', [c.id, key]);
  if ((seen.rowCount ?? 0) > 0) return 'already';
  try {
    await withTransaction(async (client) => {
      const mail = await insertMail(client, {
        characterId,
        kind: 'system',
        systemCode: c.category as SystemCode,
        listingId: null,
        bidId: null,
        refItemKey: null,
        refCount: null,
        itemKey: null,
        count: null,
        bind: null,
        gold: 0,
        createdAt: now,
        expiresAt: new Date(now.getTime() + c.mailDays * 86_400_000),
        title: c.title,
        body: c.body,
        campaignId: c.id,
        attachN: c.attachments.length,
      });
      await insertAttachments(
        client,
        mail.id,
        c.attachments.map((a) => ({ slot: a.slot, kind: a.kind, itemKey: a.itemKey, amount: a.amount, bind: a.bind })),
      );
      for (const a of c.attachments) {
        if (a.kind === 'item' && a.itemKey !== null) {
          await insertItemLedger(client, characterId, a.itemKey, a.amount, a.amount, 'mail', 'admin_grant', mail.uuid, null);
        }
      }
      await client.query(
        'INSERT INTO mail_campaign_deliveries (campaign_id, delivery_key, account_id, character_id, mail_id) VALUES ($1, $2, $3, $4, $5)',
        [c.id, key, accountId, characterId, mail.id],
      );
      // 마지막 한 문장: 총 지급 상한, 승인 상태, 기간을 DB가 강제한다
      const up = await client.query(
        `UPDATE mail_campaigns SET issued_count = issued_count + 1
          WHERE id = $1 AND status = 'active' AND $2 >= starts_at AND $2 < ends_at AND issued_count < cap_count`,
        [c.id, now],
      );
      if ((up.rowCount ?? 0) !== 1) throw new CapReached();
      announceMail(client, { characterUuid: f.charUuid, mailUuid: mail.uuid, kind: 'system', refItemKey: null, gold: 0, at: now, title: c.title });
    });
    return 'delivered';
  } catch (err) {
    if (err instanceof CapReached) {
      invalidateCampaignCache();
      return 'closed';
    }
    // 같은 계정의 동시 접속 두 요청이 겹쳤다: 먼저 끝난 쪽이 한 통을 만들었다
    if (isUniqueViolation(err, DELIVERY_UNIQUE) || isUniqueViolation(err, MAIL_ONCE_UNIQUE)) return 'already';
    throw err;
  }
}

/** 이 접속자에게 줄 캠페인 우편을 만든다. 만든 통수를 돌려준다 */
export async function deliverCampaignsFor(accountId: number, characterId: number, now: Date = getNow()): Promise<number> {
  if (!getConfig().sweep.campaignDeliveryEnabled) return 0;
  const cands = await activeCampaigns(now);
  if (cands.length === 0) return 0;
  const facts = await loadFacts(accountId, characterId);
  if (!facts) return 0;
  let n = 0;
  for (const c of cands) {
    if (!matchesTarget(c, facts)) continue;
    try {
      if ((await deliverOne(c, accountId, facts, characterId, now)) === 'delivered') n++;
    } catch (err) {
      logger.error({ err, campaign: c.uuid }, 'campaign.delivery_failed');
    }
  }
  return n;
}

const pending = new Set<Promise<unknown>>();

/** 요청 처리 뒤 비동기 배달(실패해도 응답에 영향 없음, 로그만) */
export function deliverCampaignsInBackground(accountId: number, characterId: number): void {
  if (!getConfig().sweep.campaignDeliveryEnabled) return;
  const p = deliverCampaignsFor(accountId, characterId).catch((err: unknown) => {
    logger.error({ err }, 'campaign.delivery_failed');
    return 0;
  });
  pending.add(p);
  void p.finally(() => pending.delete(p));
}

/** 테스트용: 진행 중인 백그라운드 배달이 끝나기를 기다린다 */
export async function awaitCampaignDeliveries(): Promise<void> {
  while (pending.size > 0) await Promise.all([...pending]);
}
