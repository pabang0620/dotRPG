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
import type { StoredResult } from '../economy/economyService';
import * as repo from './mailRepository';
import type { MailRow } from './mailRepository';
import type { ClaimAllBody, ClaimBody, ListQuery, SummaryQuery } from './mailValidation';

const DAY_MS = 86_400_000;

async function mustOwn(accountId: number, characterUuid: string) {
  const c = await ownedChar(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

const mailView = (m: MailRow, now: Date) => ({
  id: m.uuid,
  kind: m.kind,
  ...(m.kind === 'system' ? { system_code: m.systemCode } : {}),
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
  return { data: { mails: r.rows.map((m) => mailView(m, now)) }, meta: { total: r.total, page: q.page, limit: q.limit } };
}

/** M4 빨간 점·새 우편 폴링 */
export async function mailSummary(accountId: number, characterUuid: string, q: SummaryQuery) {
  const c = await mustOwn(accountId, characterUuid);
  await settleDueOfCharacter(c.id);
  const now = getNow();
  const s = await repo.summaryOf(getPool(), c.id, now, q.since ? new Date(q.since) : null);
  return {
    unclaimed: s.unclaimed,
    latest_at: s.latestAt ? s.latestAt.toISOString() : null,
    active_listings: await countActiveOfSeller(getPool(), c.id),
    my_top_bids: await activeCountOfBidder(getPool(), c.id),
    new: s.fresh.map((m) => ({ kind: m.kind, ...(m.kind === 'system' ? { system_code: m.systemCode } : {}), at: m.createdAt.toISOString(), ref_item_key: m.refItemKey, gold: m.gold })),
    server_time: now.toISOString(),
  };
}

/** 우편 한 통을 받는다(캐릭터 행과 그 우편 행이 잠겨 있다). 골드 상한이면 false(우편은 남는다) */
async function claimOne(ctx: EconCtx, m: MailRow, now: Date, strict: boolean): Promise<boolean> {
  const cap = getConfig().auction.goldClientMax;
  if (m.gold > 0 && ctx.gold + m.gold > cap) {
    if (strict) throw new AppError(422, '골드가 보유 상한을 넘어 받을 수 없습니다.', 'GOLD_CAP_EXCEEDED', { cap });
    return false;
  }
  if (!(await repo.markClaimed(ctx.client, m.id, now))) {
    throw new AppError(409, '이미 받은 우편입니다.', 'MAIL_ALREADY_CLAIMED');
  }
  if (m.gold > 0) await ctx.changeGold(m.gold, 'mail_claim', m.uuid);
  if (m.itemKey !== null && m.count !== null && m.bind !== null) {
    await insertItemLedger(ctx.client, ctx.char.id, m.itemKey, -m.count, 0, 'mail', 'mail_claim', m.uuid, ctx.requestId);
    await ctx.addItem('bag', m.itemKey, m.count, 'mail_claim', m.uuid, m.bind);
  }
  return true;
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
      await claimOne(ctx, m, now, true);
      return {
        status: 200,
        data: {
          claimed: { item: m.itemKey === null ? null : { item_key: m.itemKey, count: m.count, bind: m.bind }, gold: m.gold },
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
      const now = getNow();
      const max = getConfig().auction.mailClaimAllMax;
      const total = await repo.countOpenMails(ctx.client, ctx.char.id, now);
      const mails = await repo.lockOpenMails(ctx.client, ctx.char.id, now, max);
      let claimed = 0;
      let skipped = 0;
      for (const m of mails) {
        if (await claimOne(ctx, m, now, false)) claimed++;
        else skipped++;
      }
      return {
        status: 200,
        data: { claimed_count: claimed, remaining: total - mails.length, skipped, delta: ctx.delta() },
      };
    },
  });
}
