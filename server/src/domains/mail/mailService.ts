// 우편: 목록, 수령, 모두 받기, 요약. 수령 락 순서는 요청자 캐릭터 행 -> 자기 우편 행들(phase6_api.md 8.1).
import { getConfig } from '../../config/env';
import { assertNoHold } from '../antiabuse/holds';
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { FlagError, runAuction } from '../auction/auctionFlags';
import { countActiveOfSeller } from '../auction/auctionRepository';
import { activeCountOfBidder, ownedChar } from '../auction/auctionSearchRepository';
import { settleDueOfCharacter } from '../auction/auctionTicker';
import type { EconCtx } from '../economy/economyContext';
import { insertItemLedger } from '../economy/economyRepository';
import * as wallet from '../sweep/ticketWallet';
import { deliverCampaignsInBackground } from './campaignDelivery';
import type { StoredResult } from '../economy/economyService';
import { attachmentView, attachmentsOf, claimAttachments, goldOf, legacyAttachments, type Attachment } from './mailAttachments';
import * as repo from './mailRepository';
import type { MailRow } from './mailRepository';
import type { ClaimAllBody, ClaimBody, ListQuery, SummaryQuery } from './mailValidation';

const DAY_MS = 86_400_000;

async function mustOwn(accountId: number, characterUuid: string) {
  const c = await ownedChar(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

/** 첨부 표가 있으면 그것을, 없으면(옛 우편) gold -> item 순서로 합성한다 */
const attachmentsFor = (m: MailRow, byMail: Map<number, Attachment[]>): Attachment[] => (m.attachN > 0 ? (byMail.get(m.id) ?? []) : legacyAttachments(m));

const mailView = (m: MailRow, now: Date, byMail: Map<number, Attachment[]>) => ({
  id: m.uuid,
  kind: m.kind,
  ...(m.kind === 'system' ? { system_code: m.systemCode } : {}),
  title: m.title,
  body: m.body,
  campaign: m.campaignId !== null,
  attachments: attachmentsFor(m, byMail).map(attachmentView),
  ref_item_key: m.refItemKey,
  ref_count: m.refCount,
  item: m.itemKey === null ? null : { item_key: m.itemKey, count: m.count, bind: m.bind },
  gold: m.gold,
  created_at: m.createdAt.toISOString(),
  expires_at: m.expiresAt.toISOString(),
  days_left: Math.max(0, Math.ceil((m.expiresAt.getTime() - now.getTime()) / DAY_MS)),
});

/** M1 받을 우편 목록 */
export async function listMail(accountId: number, characterUuid: string, q: ListQuery) {
  const c = await mustOwn(accountId, characterUuid);
  await settleDueOfCharacter(c.id);
  const now = getNow();
  const r = await repo.listOpenMails(getPool(), c.id, now, q.tab, q.limit, (q.page - 1) * q.limit);
  const atts = await attachmentsOf(getPool(), r.rows.filter((m) => m.attachN > 0).map((m) => m.id));
  return { data: { mails: r.rows.map((m) => mailView(m, now, atts)) }, meta: { total: r.total, page: q.page, limit: q.limit } };
}

/** M4 빨간 점·새 우편 폴링 */
export async function mailSummary(accountId: number, characterUuid: string, q: SummaryQuery) {
  const c = await mustOwn(accountId, characterUuid);
  await settleDueOfCharacter(c.id);
  const now = getNow();
  const s = await repo.summaryOf(getPool(), c.id, now, q.since ? new Date(q.since) : null);
  // 10단계 E12: 이미 접속 중인 사람이 승인 직후 캠페인 우편을 받게 하는 두 번째 트리거. 응답에 영향 없이 뒤에서 돈다
  deliverCampaignsInBackground(accountId, c.id);
  return {
    unclaimed: s.unclaimed,
    latest_at: s.latestAt ? s.latestAt.toISOString() : null,
    active_listings: await countActiveOfSeller(getPool(), c.id),
    my_top_bids: await activeCountOfBidder(getPool(), c.id),
    new: s.fresh.map((m) => ({ kind: m.kind, ...(m.kind === 'system' ? { system_code: m.systemCode } : {}), title: m.title, at: m.createdAt.toISOString(), ref_item_key: m.refItemKey, gold: m.gold })),
    server_time: now.toISOString(),
  };
}

interface ClaimedOne {
  claimed: boolean;
  attachments: Attachment[];
  tickets: boolean;
}

/** 우편 한 통을 받는다(캐릭터 행·계정 행·그 우편 행이 잠겨 있다). 골드 상한이면 claimed=false(우편은 남는다, 부분 수령 없음) */
async function claimOne(ctx: EconCtx, m: MailRow, now: Date, strict: boolean): Promise<ClaimedOne> {
  const cap = getConfig().auction.goldClientMax;
  if (m.attachN > 0) {
    const atts = (await attachmentsOf(ctx.client, [m.id])).get(m.id) ?? [];
    const gold = goldOf(atts);
    if (gold > 0 && ctx.gold + gold > cap) {
      if (strict) throw new AppError(422, '골드가 보유 상한을 넘어 받을 수 없습니다.', 'GOLD_CAP_EXCEEDED', { cap });
      return { claimed: false, attachments: atts, tickets: false };
    }
    if (!(await repo.markClaimed(ctx.client, m.id, now))) throw new AppError(409, '이미 받은 우편입니다.', 'MAIL_ALREADY_CLAIMED');
    const r = await claimAttachments(ctx, m, atts, now);
    return { claimed: true, attachments: atts, tickets: r.tickets };
  }
  if (m.gold > 0 && ctx.gold + m.gold > cap) {
    if (strict) throw new AppError(422, '골드가 보유 상한을 넘어 받을 수 없습니다.', 'GOLD_CAP_EXCEEDED', { cap });
    return { claimed: false, attachments: [], tickets: false };
  }
  if (!(await repo.markClaimed(ctx.client, m.id, now))) {
    throw new AppError(409, '이미 받은 우편입니다.', 'MAIL_ALREADY_CLAIMED');
  }
  if (m.gold > 0) await ctx.changeGold(m.gold, 'mail_claim', m.uuid);
  if (m.itemKey !== null && m.count !== null && m.bind !== null) {
    await insertItemLedger(ctx.client, ctx.char.id, m.itemKey, -m.count, 0, 'mail', 'mail_claim', m.uuid, ctx.requestId);
    await ctx.addItem('bag', m.itemKey, m.count, 'mail_claim', m.uuid, m.bind);
  }
  return { claimed: true, attachments: legacyAttachments(m), tickets: false };
}

/** M2 우편 한 통 수령 */
export function claimMail(accountId: number, characterUuid: string, mailUuid: string, body: ClaimBody): Promise<StoredResult> {
  return runAuction({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/mail/:mail_id/claim',
    requestId: body.request_id,
    payload: { mail_id: mailUuid },
    handler: async (ctx) => {
      // 9단계 12.5: 경제 정지 중에는 우편을 받을 수 없다(우편은 쌓이므로 의심 거래 대금이 정지 중 인출되지 않는다)
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      // 10단계 락 순서: 캐릭터 행 다음에 계정 행(우편 행을 읽기 전에). 클리어권 첨부가 있는지는 읽기 전에는 모른다
      await wallet.lockAccount(ctx.client, ctx.char.accountId);
      const m = await repo.lockMailByUuid(ctx.client, mailUuid);
      if (!m || m.characterId !== ctx.char.id) {
        const err = new AppError(404, '우편을 찾을 수 없습니다.', 'MAIL_NOT_FOUND');
        if (!m) throw err;
        throw new FlagError(404, err.message, 'MAIL_NOT_FOUND', {
          accountId: ctx.char.accountId,
          characterId: ctx.char.id,
          kind: 'foreign_id',
          severity: 1,
          detail: { mail: mailUuid },
        });
      }
      if (m.claimedAt) throw new AppError(409, '이미 받은 우편입니다.', 'MAIL_ALREADY_CLAIMED');
      const now = getNow();
      if (m.expiredAt || m.expiresAt.getTime() <= now.getTime()) {
        throw new AppError(410, '보관 기한이 지나 사라진 우편입니다.', 'MAIL_EXPIRED');
      }
      const done = await claimOne(ctx, m, now, true);
      const tickets = done.tickets ? wallet.viewOf(await wallet.readLive(ctx.client, ctx.char.accountId, now)) : undefined;
      return {
        status: 200,
        data: {
          claimed: {
            item: m.itemKey === null ? null : { item_key: m.itemKey, count: m.count, bind: m.bind },
            gold: m.gold,
            attachments: done.attachments.map(attachmentView),
            ...(tickets ? { tickets } : {}),
          },
          delta: ctx.delta(),
        },
      };
    },
  });
}

/** M3 모두 받기(오래된 순, 최대 MAIL_CLAIM_ALL_MAX통) */
export function claimAll(accountId: number, characterUuid: string, body: ClaimAllBody): Promise<StoredResult> {
  return runAuction({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/mail/claim-all',
    requestId: body.request_id,
    payload: {},
    handler: async (ctx) => {
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      await wallet.lockAccount(ctx.client, ctx.char.accountId);
      const now = getNow();
      const max = getConfig().auction.mailClaimAllMax;
      const total = await repo.countOpenMails(ctx.client, ctx.char.id, now);
      const mails = await repo.lockOpenMails(ctx.client, ctx.char.id, now, max);
      let claimed = 0;
      let skipped = 0;
      let anyTickets = false;
      for (const m of mails) {
        const one = await claimOne(ctx, m, now, false);
        if (one.claimed) claimed++;
        else skipped++;
        if (one.tickets) anyTickets = true;
      }
      const tickets = anyTickets ? wallet.viewOf(await wallet.readLive(ctx.client, ctx.char.accountId, now)) : undefined;
      return {
        status: 200,
        data: { claimed_count: claimed, remaining: total - mails.length, skipped, ...(tickets ? { tickets } : {}), delta: ctx.delta() },
      };
    },
  });
}
