// EC1~EC3: 일일 경제 요약, 운영 지급(system 우편으로만), 지급 이력.
// 캐릭터 잔액을 직접 고치는 경로는 없다: 골드는 우편이 들고 있다가 수령 때 gold_ledger(mail_claim)로, 아이템은 item_ledger(admin_grant)로 남는다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { createSystemMail } from '../../domains/mail/mailRepository';
import { strongerBind, type Bind } from '../../domains/economy/economyRepository';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { parseItemKey } from '../../utils/itemKey';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './economyAdminRepository';
import type { GrantBody, GrantListQuery } from './economyAdminValidation';

const DAY_MS = 86_400_000;

const median = (xs: number[]): number => {
  const s = [...xs].sort((a, b) => a - b);
  return s.length % 2 === 1 ? (s[(s.length - 1) / 2] as number) : (((s[s.length / 2 - 1] as number) + (s[s.length / 2] as number)) / 2);
};

/** EC1: day는 게임 일(06:00 KST 시작) 날짜. 기본은 어제 */
export async function daily(day: string | undefined) {
  const db = getPool();
  const now = getNow();
  const start = day
    ? new Date(resetBoundaries(new Date(Date.parse(`${day}T12:00:00+09:00`))).dailyStartAt)
    : new Date(Date.parse(resetBoundaries(now).dailyStartAt) - DAY_MS);
  const w = { start, end: new Date(start.getTime() + DAY_MS) };
  const [gold, xp, items, auction, accounts, prev] = await Promise.all([
    repo.goldByReason(db, w),
    repo.xpByReason(db, w),
    repo.itemByReason(db, w),
    repo.auctionDay(db, w),
    repo.accountsDay(db, w),
    repo.goldInflowPrev7(db, start),
  ]);
  const inflow = gold.reduce((a, r) => a + Number(r.inflow), 0);
  const outflow = gold.reduce((a, r) => a + Number(r.outflow), 0);
  const med = median(prev);
  return {
    day: { start: start.toISOString(), end: w.end.toISOString() },
    gold: {
      inflow,
      outflow,
      by_reason: gold.map((r) => ({ reason: r.reason, inflow: Number(r.inflow), outflow: Number(r.outflow), lines: Number(r.n) })),
      prev7_median_inflow: med,
      inflow_vs_median: med > 0 ? Math.round((inflow / med) * 100) / 100 : null,
    },
    xp: xp.map((r) => ({ reason: r.reason, total: Number(r.total), lines: Number(r.n) })),
    items: items.map((r) => ({ reason: r.reason, plus: Number(r.plus), minus: Number(r.minus), lines: Number(r.n) })),
    auction: {
      trades: Number(auction.trades.n),
      gold: Number(auction.trades.gold),
      fee: Number(auction.trades.fee),
      sinks: auction.sinks.map((s) => ({ kind: s.kind, amount: Number(s.amount) })),
      pair_limit_hits: auction.pairLimit,
    },
    accounts: { new: Number(accounts.created), active: Number(accounts.active) },
  };
}

/** EC2 운영 지급(owner). 한도는 서버 환경변수가 다시 검사한다 */
export function createGrant(admin: AdminCtx, ip: string, body: GrantBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'grant.create',
    targetType: 'character',
    targetUuid: body.character_id,
    requestId: body.request_id,
    params: {
      system_code: body.system_code,
      ...(body.gold ? { gold: body.gold } : {}),
      ...(body.item ? { item: body.item } : {}),
      memo_length: body.memo.length,
    },
    handler: async (client) => {
      const cfg = getConfig().admin;
      const gd = getGameData();
      if (body.gold !== undefined && body.gold > cfg.grantMaxGold) throw new AppError(422, `1회 지급 골드 한도(${cfg.grantMaxGold})를 넘었습니다.`, 'GRANT_LIMIT');
      let bind: Bind | null = null;
      if (body.item) {
        if (body.item.count > cfg.grantMaxItemCount) throw new AppError(422, `1회 지급 아이템 수량 한도(${cfg.grantMaxItemCount})를 넘었습니다.`, 'GRANT_LIMIT');
        const parsed = parseItemKey(body.item.item_key);
        const def = parsed ? gd.economy.items.get(parsed.base) : undefined;
        if (!parsed || !def || def.kind === 'currency') throw new AppError(422, '지급할 수 없는 아이템입니다.', 'ITEM_NOT_FOUND');
        // 귀속은 서버가 정한다: 장비는 캐릭터 귀속, 재료·소모품은 아이템 종류의 하한(보통 귀속 없음)
        bind = gd.economy.shop.equipment.has(parsed.base) ? strongerBind('character', def.bind) : def.bind;
      }
      // 관리자 행(일일 한도 검사 직렬화) -> 대상 캐릭터 행 순으로 잠근다
      await repo.lockAdmin(client, admin.id);
      const ch = await repo.lockCharacterByUuid(client, body.character_id);
      if (!ch) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
      const now = getNow();
      if (body.gold !== undefined) {
        const dayStart = new Date(resetBoundaries(now).dailyStartAt);
        const used = await repo.grantedGoldSince(client, admin.id, dayStart);
        if (used + body.gold > cfg.grantDailyGold) {
          throw new AppError(422, `오늘 지급 한도(${cfg.grantDailyGold}G)를 넘습니다.`, 'GRANT_LIMIT', { used, limit: cfg.grantDailyGold });
        }
      }
      const mail = await createSystemMail(client, {
        characterId: ch.id,
        systemCode: body.system_code,
        gold: body.gold ?? 0,
        item: body.item && bind ? { itemKey: body.item.item_key, count: body.item.count, bind } : null,
        now,
        expiresAt: new Date(now.getTime() + gd.auction.mailDays * DAY_MS),
        requestId: body.request_id,
      });
      const g = await repo.insertGrant(client, {
        adminId: admin.id,
        characterId: ch.id,
        mailId: mail.id,
        requestId: body.request_id,
        systemCode: body.system_code,
        gold: body.gold ?? 0,
        itemKey: body.item?.item_key ?? null,
        count: body.item?.count ?? null,
        memo: body.memo,
      });
      return {
        status: 201,
        data: {
          grant: { id: g.uuid, character_id: ch.uuid, gold: body.gold ?? 0, item: body.item ?? null, system_code: body.system_code },
          mail_id: mail.uuid,
        },
      };
    },
  });
}

/** EC3 */
export async function listGrants(q: GrantListQuery) {
  const db = getPool();
  let characterId: number | null = null;
  if (q.character) {
    characterId = await repo.characterIdByUuid(db, q.character);
    if (characterId === null) return { items: [], next_before: null };
  }
  const rows = await repo.listGrants(db, characterId, q.before ? Number(q.before) : null, q.limit);
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return {
    items: page.map((r) => ({
      id: r.uuid,
      by: r.admin,
      character: { id: r.character_uuid, name: r.character_name },
      system_code: r.system_code,
      gold: Number(r.gold),
      item: r.item_key ? { item_key: r.item_key, count: r.count } : null,
      memo: r.memo,
      at: r.created_at.toISOString(),
      mail_state: r.claimed_at ? 'claimed' : r.expired_at ? 'expired' : 'unclaimed',
    })),
    next_before: rows.length > q.limit && last ? last.id : null,
  };
}
