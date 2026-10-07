// MC1~MC6: 운영 우편 캠페인 관리자 API(설계 7.4). 만든 뒤 내용은 바뀌지 않고, 승인은 작성자 외 owner(2인 확인)가 한다.
// 캠페인은 대상이 접속할 때 우편을 만든다(전체 발송 때 한 번에 넣지 않는다): 배달은 domains/mail/campaignDelivery.
import { hashRequest } from '../../db/idempotency';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { strongerBind, type Bind } from '../../domains/economy/economyRepository';
import { invalidateCampaignCache } from '../../domains/mail/campaignCache';
import { attachmentView, type Attachment } from '../../domains/mail/mailAttachments';
import { getGameData } from '../../gamedata/loader';
import { isSweepTicketKey } from '../../gamedata/sweepData';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { parseItemKey } from '../../utils/itemKey';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './mailCampaignsRepository';
import type { CampaignRow } from './mailCampaignsRepository';
import type { ApproveBody, CancelBody, CreateBody, DeliveriesQuery, ListQuery } from './mailCampaignsValidation';

const DAY_MS = 86_400_000;

const limitErr = (field: string, message: string) => new AppError(422, message, 'CAMPAIGN_LIMIT', { field });
const targetErr = (message: string) => new AppError(422, message, 'CAMPAIGN_TARGET_INVALID');

function campaignView(c: CampaignRow, atts: Attachment[]) {
  return {
    id: c.uuid,
    title: c.title,
    body: c.body,
    category: c.category,
    status: c.status,
    delivery_unit: c.deliveryUnit,
    target: c.target,
    mail_days: c.mailDays,
    starts_at: c.startsAt.toISOString(),
    ends_at: c.endsAt.toISOString(),
    cap_count: c.capCount,
    issued_count: c.issuedCount,
    attachments: atts.map(attachmentView),
    created_by: c.createdByName,
    memo: c.memo,
    ...(c.status === 'pending' ? { needs_approval_by: 'owner other than author' } : {}),
  };
}

const detailView = (c: CampaignRow, atts: Attachment[]) => ({
  ...campaignView(c, atts),
  approved_by: c.approvedByName,
  approved_at: c.approvedAt ? c.approvedAt.toISOString() : null,
  cancelled_by: c.cancelledByName,
  cancel_reason: c.cancelReason,
});

/** 만들 때 검증(7.4 MC1). 첨부를 서버 기준으로 정리해 돌려준다(귀속은 키의 하한보다 약해지지 않는다) */
async function validateCreate(b: CreateBody, now: Date): Promise<{ startsAt: Date; endsAt: Date; atts: Attachment[] }> {
  const cfg = getConfig();
  const sw = cfg.sweep;
  const startsAt = new Date(b.starts_at);
  const endsAt = new Date(b.ends_at);
  if (endsAt.getTime() <= startsAt.getTime()) throw limitErr('ends_at', '종료 시각은 시작 시각보다 뒤여야 합니다.');
  if (endsAt.getTime() - startsAt.getTime() > sw.campaignMaxWindowDays * DAY_MS) throw limitErr('ends_at', `배달 기간은 최대 ${sw.campaignMaxWindowDays}일입니다.`);
  if (endsAt.getTime() <= now.getTime()) throw limitErr('ends_at', '종료 시각이 이미 지났습니다.');
  if (b.cap_count > sw.campaignMaxCapCount) throw limitErr('cap_count', `총 지급 통수는 최대 ${sw.campaignMaxCapCount}통입니다.`);

  const gd = getGameData();
  const atts: Attachment[] = [];
  const seenKeys = new Set<string>();
  let hasGold = false;
  let hasTicket = false;
  for (const a of b.attachments) {
    const slot = atts.length + 1;
    if (a.kind === 'gold') {
      if (hasGold) throw limitErr('attachments', '골드 첨부는 한 개까지입니다.');
      hasGold = true;
      if (a.amount > sw.campaignMaxGoldPerMail) throw limitErr('attachments.gold', `1통 골드 상한(${sw.campaignMaxGoldPerMail})을 넘었습니다.`);
      if (sw.campaignMaxGoldTotal === null) throw limitErr('attachments.gold', '총 지급 골드 상한(CAMPAIGN_MAX_GOLD_TOTAL)이 설정되지 않아 골드 첨부를 만들 수 없습니다.');
      if (a.amount * b.cap_count > sw.campaignMaxGoldTotal) throw limitErr('attachments.gold', `총 지급 골드(골드 x 통수)가 상한(${sw.campaignMaxGoldTotal})을 넘었습니다.`);
      atts.push({ slot, kind: 'gold', itemKey: null, amount: a.amount, bind: null });
    } else if (a.kind === 'item') {
      const parsed = parseItemKey(a.item_key);
      const def = parsed ? gd.economy.items.get(parsed.base) : undefined;
      if (!parsed || !def || def.kind === 'currency' || isSweepTicketKey(gd.sweep, parsed.base)) throw new AppError(422, '지급할 수 없는 아이템입니다.', 'ITEM_NOT_FOUND');
      if (seenKeys.has(a.item_key)) throw limitErr('attachments', '같은 아이템을 두 번 넣을 수 없습니다.');
      seenKeys.add(a.item_key);
      if (a.count > cfg.admin.grantMaxItemCount) throw limitErr('attachments.item', `아이템 수량 한도(${cfg.admin.grantMaxItemCount})를 넘었습니다.`);
      // 귀속은 서버가 정한다: 장비는 최소 캐릭터 귀속, 그 밖은 키의 하한. 요청이 더 강하게는 줄 수 있다
      const floor: Bind = gd.economy.shop.equipment.has(parsed.base) ? strongerBind('character', def.bind) : def.bind;
      atts.push({ slot, kind: 'item', itemKey: a.item_key, amount: a.count, bind: strongerBind(a.bind ?? 'none', floor) });
    } else {
      if (hasTicket) throw limitErr('attachments', '클리어권 첨부는 한 개까지입니다.');
      hasTicket = true;
      if (!gd.sweep) throw limitErr('attachments.sweep_ticket', '클리어권 데이터(sweep.json)가 없어 클리어권 첨부를 만들 수 없습니다.');
      if (a.count > sw.campaignMaxTicketsPerMail) throw limitErr('attachments.sweep_ticket', `1통 클리어권 상한(${sw.campaignMaxTicketsPerMail}장)을 넘었습니다.`);
      atts.push({ slot, kind: 'sweep_ticket', itemKey: gd.sweep.eventTicketItem, amount: a.count, bind: null });
    }
  }

  const t = b.target;
  if (b.delivery_unit === 'account' && (t.classes !== undefined || t.min_level !== undefined || t.max_level !== undefined)) {
    throw targetErr('직업·캐릭터 레벨 조건은 캐릭터 단위 캠페인에서만 쓸 수 있습니다.');
  }
  if (t.min_account_level !== undefined && t.max_account_level !== undefined && t.min_account_level > t.max_account_level) throw targetErr('계정 레벨 범위가 올바르지 않습니다.');
  if (t.min_level !== undefined && t.max_level !== undefined && t.min_level > t.max_level) throw targetErr('레벨 범위가 올바르지 않습니다.');
  if (t.account_created_from && t.account_created_to && Date.parse(t.account_created_from) > Date.parse(t.account_created_to)) throw targetErr('가입일 범위가 올바르지 않습니다.');
  if (t.account_ids) {
    if (t.account_ids.length > sw.campaignMaxTargetIds) throw limitErr('target.account_ids', `특정 계정 목록은 최대 ${sw.campaignMaxTargetIds}개입니다.`);
    const found = await repo.existingAccountUuids(getPool(), t.account_ids);
    if (t.account_ids.some((id) => !found.has(id))) throw targetErr('존재하지 않는 계정 id가 있습니다.');
  }
  return { startsAt, endsAt, atts };
}

/** MC1 작성(operator 이상). 승인 대기로 만든다 */
export function createCampaign(admin: AdminCtx, ip: string, body: CreateBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'campaign.create',
    targetType: 'campaign',
    requestId: body.request_id,
    params: {
      title: body.title,
      category: body.category,
      delivery_unit: body.delivery_unit,
      mail_days: body.mail_days,
      starts_at: body.starts_at,
      ends_at: body.ends_at,
      cap_count: body.cap_count,
      attachments: body.attachments,
      target_hash: hashRequest(body.target),
      body_hash: hashRequest(body.body),
      memo_length: body.memo.length,
    },
    handler: async (client) => {
      const now = getNow();
      const v = await validateCreate(body, now);
      const c = await repo.insertCampaign(client, {
        title: body.title,
        body: body.body,
        category: body.category,
        deliveryUnit: body.delivery_unit,
        target: body.target,
        mailDays: body.mail_days,
        startsAt: v.startsAt,
        endsAt: v.endsAt,
        capCount: body.cap_count,
        memo: body.memo,
        createdBy: admin.id,
      });
      await repo.insertCampaignAttachments(client, c.id, v.atts);
      const row = (await repo.findByUuid(client, c.uuid)) as CampaignRow;
      return { status: 201, data: { campaign: campaignView(row, v.atts) }, targetUuid: c.uuid };
    },
  });
}

const NOT_FOUND = () => new AppError(404, '캠페인을 찾을 수 없습니다.', 'CAMPAIGN_NOT_FOUND');

/** MC2 목록 */
export async function listCampaigns(admin: AdminCtx, ip: string, q: ListQuery) {
  const rows = await repo.list(getPool(), q.status, q.limit, q.before ? Number(q.before) : undefined);
  const page = rows.slice(0, q.limit);
  await auditView(admin, ip, 'campaign.list', 'campaign', null, { status: q.status ?? null });
  return {
    items: page.map((c) => ({
      id: c.uuid,
      title: c.title,
      category: c.category,
      status: c.status,
      delivery_unit: c.deliveryUnit,
      starts_at: c.startsAt.toISOString(),
      ends_at: c.endsAt.toISOString(),
      cap_count: c.capCount,
      issued_count: c.issuedCount,
      created_by: c.createdByName,
      approved_by: c.approvedByName,
    })),
    next_before: rows.length > q.limit ? String((page[page.length - 1] as CampaignRow).id) : null,
  };
}

/** MC3 상세와 현황 */
export async function showCampaign(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const c = await repo.findByUuid(db, uuid);
  if (!c) throw NOT_FOUND();
  const now = getNow();
  const [atts, stats, remaining] = await Promise.all([repo.campaignAttachments(db, c.id), repo.stats(db, c.id, now), repo.revokeRemaining(db, c.id, now)]);
  await auditView(admin, ip, 'campaign.view', 'campaign', uuid);
  return { campaign: detailView(c, atts), stats, revoke: { requested: c.revokeRequested, remaining: c.revokeRequested ? remaining : 0 } };
}

/** MC4 승인(owner, 작성자 본인 불가). 승인된 순간부터 접속하는 대상에게 우편이 만들어진다 */
export async function approveCampaign(admin: AdminCtx, ip: string, uuid: string, body: ApproveBody): Promise<ActionResult> {
  // 기능 플래그가 꺼져 있으면 승인도 하지 않는다(11절: 새 우편 클라이언트가 퍼진 뒤 켠다)
  if (!getConfig().sweep.campaignDeliveryEnabled) throw new AppError(503, '아직 준비 중인 기능입니다.', 'FEATURE_DISABLED');
  return runAdminAction({
    admin,
    ip,
    action: 'campaign.approve',
    targetType: 'campaign',
    targetUuid: uuid,
    requestId: body.request_id,
    params: {},
    handler: async (client) => {
      const c = await repo.lockByUuid(client, uuid);
      if (!c) throw NOT_FOUND();
      if (c.status !== 'pending') throw new AppError(409, '승인 대기 중인 캠페인이 아닙니다.', 'CAMPAIGN_STATE');
      if (c.createdBy === admin.id) throw new AppError(403, '작성자는 승인할 수 없습니다. 다른 owner가 승인해야 합니다.', 'CAMPAIGN_SELF_APPROVAL');
      const now = getNow();
      if (c.endsAt.getTime() <= now.getTime()) throw new AppError(422, '배달 기간이 이미 끝났습니다.', 'CAMPAIGN_WINDOW_PASSED');
      await repo.markApproved(client, c.id, admin.id, now);
      invalidateCampaignCache();
      const row = (await repo.findByUuid(client, uuid)) as CampaignRow;
      return { status: 200, data: { campaign: detailView(row, await repo.campaignAttachments(client, c.id)) } };
    },
  });
}

/** MC5 취소(대기 중은 작성자도, 진행·종료는 owner만). revoke_unclaimed면 작업이 미수령 우편만 회수한다 */
export function cancelCampaign(admin: AdminCtx, ip: string, uuid: string, body: CancelBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'campaign.cancel',
    targetType: 'campaign',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { reason_length: body.reason.length, revoke_unclaimed: body.revoke_unclaimed },
    handler: async (client) => {
      const c = await repo.lockByUuid(client, uuid);
      if (!c) throw NOT_FOUND();
      const owner = admin.role === 'owner';
      if (c.status === 'cancelled') throw new AppError(409, '이미 취소된 캠페인입니다.', 'CAMPAIGN_STATE');
      if (c.status === 'pending' ? !(owner || c.createdBy === admin.id) : !owner) throw new AppError(403, '권한이 없습니다.', 'FORBIDDEN_ROLE');
      const now = getNow();
      if (c.status === 'ended') {
        if (!body.revoke_unclaimed) throw new AppError(409, '이미 끝난 캠페인은 미수령 회수만 요청할 수 있습니다.', 'CAMPAIGN_STATE');
        await repo.requestRevoke(client, c.id);
      } else {
        await repo.markCancelled(client, c.id, admin.id, now, body.reason, body.revoke_unclaimed);
      }
      invalidateCampaignCache();
      const row = (await repo.findByUuid(client, uuid)) as CampaignRow;
      return {
        status: 200,
        data: { campaign: detailView(row, await repo.campaignAttachments(client, c.id)), revoke: { requested: row.revokeRequested, remaining: row.revokeRequested ? await repo.revokeRemaining(client, c.id, now) : 0 } },
      };
    },
  });
}

/** MC6 발송 대상별 수령 현황(개인정보 성격이라 조회 기록을 남긴다) */
export async function campaignDeliveries(admin: AdminCtx, ip: string, uuid: string, q: DeliveriesQuery) {
  const db = getPool();
  const c = await repo.findByUuid(db, uuid);
  if (!c) throw NOT_FOUND();
  const rows = await repo.deliveries(db, c.id, getNow(), q.state, q.limit, q.before ? Number(q.before) : undefined);
  const page = rows.slice(0, q.limit);
  await auditView(admin, ip, 'campaign.deliveries', 'campaign', uuid, { state: q.state ?? null });
  return {
    items: page.map((d) => ({
      account_id: d.account_id,
      character_id: d.character_id,
      character_name: d.character_name,
      delivered_at: d.delivered_at.toISOString(),
      state: d.state,
      claimed_at: d.claimed_at ? d.claimed_at.toISOString() : null,
    })),
    next_before: rows.length > q.limit ? String((page[page.length - 1] as { mail_row_id: string }).mail_row_id) : null,
  };
}
