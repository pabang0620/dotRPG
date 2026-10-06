// H1~H9: 경제 정지 목록·상세·해제·회수·수동 정지, 계정 연결 조회, 의심 거래 목록, 상한 표, 캐릭터 속도.
// 경제 정지는 막는 쪽만 한다. 회수(H4)는 금액을 요청으로 받지 않고 정지 근거 구간의 원장에서 서버가 계산한다(골드·아이템·우편, 경험치는 1단계 범위 밖).
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation, type Queryable } from '../../db/pool';
import * as holdsRepo from '../../domains/antiabuse/holdsRepository';
import { ipGroup } from '../../domains/antiabuse/deviceRecords';
import { ITEM_REASONS } from '../../domains/antiabuse/incomeMeter';
import { bandOf, evaluateBuckets, loadBuckets, METRICS, WINDOW_HOURS, WINDOWS, type MetricValues, type WindowKind } from '../../domains/antiabuse/velocity';
import * as econRepo from '../../domains/economy/economyRepository';
import { getIncomeCaps } from '../../gamedata/antiAbuseData';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import type { AdminCtx } from '../common/adminTypes';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import * as repo from './economyHoldsRepository';
import type { ClawbackBody, HoldListQuery, LinksQuery, ManualHoldBody, ReleaseBody, TradeFlagsQuery } from './economyHoldsValidation';

const HOUR_MS = 3_600_000;
const NOT_FOUND = () => new AppError(404, '경제 정지를 찾을 수 없습니다.', 'HOLD_NOT_FOUND');
const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);

const evidenceSummary = (e: Record<string, unknown>): { metric: unknown; value: unknown; cap: unknown } => ({ metric: e.metric ?? null, value: e.value ?? null, cap: e.cap ?? null });

// ---------- H1 ----------

export async function listHolds(q: HoldListQuery) {
  const rows = await repo.listHolds(getPool(), { state: q.state, kind: q.kind, q: q.q, cursor: repo.decodeCursor(q.cursor), limit: q.limit });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return {
    items: page.map((h) => ({
      id: h.uuid,
      state: h.state,
      kind: h.kind,
      account_id: h.account_uuid,
      character: h.character_uuid ? { id: h.character_uuid, name: h.character_name, level: h.character_level } : null,
      window_kind: h.window_kind,
      evidence_summary: evidenceSummary(h.evidence),
      created_at: h.created_at.toISOString(),
      linked_count: h.linked_count,
    })),
    next_cursor: rows.length > q.limit && last ? repo.encodeCursor(last.id) : null,
  };
}

// ---------- 창별 수치(H2, H9) ----------

type WindowView = Record<string, unknown>;

const KEY: Record<string, string> = { goldEq: 'gold_eq', epicPlus: 'epic_plus', uniquePlus: 'unique_plus' };
const snake = (m: string): string => KEY[m] ?? m;
const round = (n: number): number => Math.round(n * 100) / 100;
const named = (v: MetricValues): Record<string, number> => Object.fromEntries(METRICS.map((m) => [snake(m), round(v[m])]));

/** 한 캐릭터의 현재 창별 수입·활동 시간·상한. 상한 표가 없으면 null */
export async function windowsOf(db: Queryable, characterId: number, level: number, now: Date, accountId: number): Promise<Record<WindowKind, WindowView> | null> {
  const caps = getIncomeCaps();
  if (!caps) return null;
  const baseline = await holdsRepo.baselineOf(db, accountId, characterId);
  const out = {} as Record<WindowKind, WindowView>;
  for (const w of WINDOWS) {
    const buckets = await loadBuckets(db, characterId, now, w, baseline);
    const ev = evaluateBuckets(caps, w, buckets, level, getConfig().aa.hold.mult);
    out[w] = {
      ...named(ev.values),
      auction_net: Math.round(buckets.reduce((a, b) => a + b.auctionInW - b.auctionOut, 0)),
      active_seconds: ev.activeSeconds,
      caps: named(ev.caps),
      over: ev.over.map(snake),
    };
  }
  return out;
}

// ---------- H2 ----------

export async function holdDetail(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const h = await repo.holdListRow(db, uuid);
  if (!h) throw NOT_FOUND();
  await auditView(admin, ip, 'economy.hold.view', 'hold', uuid);
  const now = getNow();
  // 계정 단위 정지의 창별 수치는 근거에 적힌 캐릭터, 없으면 가장 최근에 소득이 기록된 캐릭터로 보여 준다
  const evChar = typeof h.evidence.character_id === 'number' ? (h.evidence.character_id as number) : null;
  const target = h.character_id ?? evChar ?? (await repo.latestCharacter(db, h.account_id))?.id ?? null;
  const ident = target ? await repo.characterIdentity(db, target) : null;
  const windows = target && ident ? await windowsOf(db, target, ident.level, now, h.account_id) : null;
  const from = h.window_start ?? new Date(now.getTime() - 24 * HOUR_MS);
  const to = h.window_end ?? now;
  const chars = h.character_id ? [h.character_id] : await repo.aliveCharacterIds(db, h.account_id);
  const children = await repo.linkedHolds(db, h.id);
  const linked: { account_id: string; relation: string; hold_id?: string }[] = children.map((c) => ({
    account_id: c.account_uuid,
    relation: Array.isArray(c.evidence.via) && c.evidence.via.length > 0 ? String((c.evidence.via as unknown[])[0]) : 'device',
    hold_id: c.uuid,
  }));
  if (h.origin_hold_id !== null) {
    const origin = await holdsRepo.holdById(db, h.origin_hold_id);
    const originAcct = origin ? await repo.accountUuid(db, origin.account_id) : null;
    if (origin && originAcct) linked.push({ account_id: originAcct, relation: 'origin', hold_id: origin.uuid });
  }
  return {
    hold: {
      id: h.uuid,
      state: h.state,
      kind: h.kind,
      account_id: h.account_uuid,
      character: h.character_uuid ? { id: h.character_uuid, name: h.character_name, level: h.character_level } : null,
      window_kind: h.window_kind,
      window_start: iso(h.window_start),
      window_end: iso(h.window_end),
      created_at: h.created_at.toISOString(),
      reviewed_by: h.reviewed_by,
      reviewed_at: iso(h.reviewed_at),
      released_at: iso(h.released_at),
      note: h.note,
      clawback: h.clawback,
    },
    evidence: h.evidence,
    windows,
    linked,
    flagged_trades: (await repo.flaggedTradesOf(db, h.account_id, new Date(now.getTime() - 7 * 24 * HOUR_MS))).map((t) => ({ ...t, traded_at: t.traded_at.toISOString() })),
    ledger_by_reason: {
      gold: await repo.ledgerByReason(db, 'gold_ledger', chars, from, to),
      item: await repo.ledgerByReason(db, 'item_ledger', chars, from, to),
      xp: await repo.ledgerByReason(db, 'xp_ledger', chars, from, to),
    },
  };
}

// ---------- H3 ----------

export function release(admin: AdminCtx, ip: string, uuid: string, body: ReleaseBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'economy.hold.release',
    targetType: 'hold',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { release_linked: body.release_linked === true, note_length: body.note.length },
    handler: async (client) => {
      const h = await holdsRepo.lockHoldByUuid(client, uuid);
      if (!h) throw NOT_FOUND();
      const now = getNow();
      let released = (await holdsRepo.release(client, h.id, admin.loginId, body.note, now)) ? 1 : 0;
      if (body.release_linked === true) {
        for (const id of await holdsRepo.linkedOf(client, h.id)) {
          if (await holdsRepo.release(client, id, admin.loginId, body.note, now)) released++;
        }
      }
      return { status: 200, data: { released } };
    },
  });
}

// ---------- H4 ----------

/** 회수에서 "획득"으로 보는 아이템 사유: 집계 사유 + 우편 수령(경매 대금·구매품이 우편으로 들어온다) */
const CLAW_ITEM_REASONS = [...ITEM_REASONS, 'mail_claim'];

interface ClawSummary {
  gold_clawed: number;
  shortfall: number;
  items: { item_key: string; count: number }[];
  mails_voided: number;
}

/** exclude: 같은 회수에서 폐기하는 미수령 판매 대금 우편의 골드(지갑에 들어오지 않았고 우편 폐기로 이미 회수하므로 두 번 걷지 않는다) */
async function clawGold(client: PoolClient, characterId: number, from: Date, to: Date, ref: string, requestId: string, exclude: number): Promise<{ clawed: number; shortfall: number }> {
  const owed = Math.max(0, (await repo.goldGainedInWindow(client, characterId, from, to)) - exclude);
  const have = (await econRepo.lockCharacters(client, [characterId]))[0]?.gold ?? 0;
  const take = Math.min(have, owed);
  if (take > 0) {
    await econRepo.updateGold(client, characterId, have - take);
    await econRepo.insertGoldLedger(client, characterId, -take, have - take, 'admin_clawback', ref, requestId);
  }
  return { clawed: take, shortfall: owed - take };
}

async function clawItems(client: PoolClient, characterId: number, from: Date, to: Date, ref: string, requestId: string): Promise<{ item_key: string; count: number }[]> {
  const out: { item_key: string; count: number }[] = [];
  for (const g of await repo.itemsNetGainedInWindow(client, characterId, from, to, CLAW_ITEM_REASONS)) {
    let left = g.n;
    // 가방 먼저, 모자라면 창고. 착용 중인 것은 회수하지 않는다(기록만)
    for (const loc of ['bag', 'storage'] as const) {
      if (left <= 0) break;
      const have = await econRepo.stackCount(client, characterId, loc, g.item_key);
      const take = Math.min(have, left);
      if (take <= 0) continue;
      if ((await econRepo.decStack(client, characterId, loc, g.item_key, take)) === null) continue;
      await econRepo.insertItemLedger(client, characterId, g.item_key, -take, have - take, loc, 'admin_clawback', ref, requestId);
      left -= take;
    }
    if (g.n - left > 0) out.push({ item_key: g.item_key, count: g.n - left });
  }
  return out;
}

export function clawback(admin: AdminCtx, ip: string, uuid: string, body: ClawbackBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'economy.hold.clawback',
    targetType: 'hold',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { gold: body.gold, items: body.items, void_mail: body.void_mail, note_length: body.note.length },
    handler: async (client) => {
      const h = await holdsRepo.lockHoldByUuid(client, uuid);
      if (!h) throw NOT_FOUND();
      if (h.state === 'clawed_back') throw new AppError(409, '이미 회수한 정지입니다.', 'HOLD_ALREADY_CLAWED');
      // log_only(shadow) 정지는 기록일 뿐이라 회수로 이어지지 않는다
      if (h.state === 'shadow') throw new AppError(409, '기록 전용(shadow) 정지는 회수할 수 없습니다.', 'HOLD_SHADOW');
      if (h.state === 'released') throw new AppError(409, '이미 해제된 정지입니다.', 'HOLD_NOT_ACTIVE');
      const now = getNow();
      const from = h.window_start ?? new Date(now.getTime() - 24 * HOUR_MS);
      const to = h.window_end ?? now;
      // 대상: 캐릭터 단위 정지는 그 캐릭터, 계정 단위는 근거에 적힌 캐릭터(경매 위반) 또는 계정의 모든 캐릭터. 캐릭터 행은 id 순으로 잠근다
      const evChar = typeof h.evidence.character_id === 'number' ? (h.evidence.character_id as number) : null;
      const targets = h.character_id ? [h.character_id] : evChar ? [evChar] : await repo.aliveCharacterIds(client, h.account_id);
      await econRepo.lockCharacters(client, [...targets].sort((a, b) => a - b));
      const summary: ClawSummary = { gold_clawed: 0, shortfall: 0, items: [], mails_voided: 0 };
      for (const id of [...targets].sort((a, b) => a - b)) {
        // 우편 폐기를 먼저 하고, 폐기한 판매 대금은 지갑 회수에서 뺀다
        let voidedSold = 0;
        if (body.void_mail) {
          const v = await repo.voidMails(client, id, from, to, now);
          summary.mails_voided += v.count;
          voidedSold = v.soldGold;
        }
        if (body.gold) {
          const g = await clawGold(client, id, from, to, h.uuid, body.request_id, voidedSold);
          summary.gold_clawed += g.clawed;
          summary.shortfall += g.shortfall;
        }
        if (body.items) {
          for (const it of await clawItems(client, id, from, to, h.uuid, body.request_id)) {
            const found = summary.items.find((x) => x.item_key === it.item_key);
            if (found) found.count += it.count;
            else summary.items.push(it);
          }
        }
      }
      if (summary.gold_clawed === 0 && summary.items.length === 0 && summary.mails_voided === 0) {
        throw new AppError(422, '회수할 것이 없습니다.', 'NOTHING_TO_CLAW', { shortfall: summary.shortfall });
      }
      await repo.markClawedBack(client, h.id, admin.loginId, body.note, { ...summary }, now);
      return { status: 200, data: { ...summary } };
    },
  });
}

// ---------- H5 ----------

export async function manualHold(admin: AdminCtx, ip: string, body: ManualHoldBody): Promise<ActionResult> {
  try {
    return await runAdminAction({
      admin,
      ip,
      action: 'economy.hold.create',
      targetType: 'hold',
      requestId: body.request_id,
      params: { ...(body.character_id ? { character_id: body.character_id } : {}), ...(body.account_id ? { account_id: body.account_id } : {}), note_length: body.note.length },
      handler: async (client) => {
        let accountId: number;
        let characterId: number | null = null;
        if (body.character_id) {
          const c = await repo.characterByUuid(client, body.character_id);
          if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
          accountId = c.account_id;
          characterId = c.id;
        } else {
          const a = await repo.accountIdByUuid(client, body.account_id as string);
          if (a === null) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
          accountId = a;
        }
        const existing = await holdsRepo.blockingInScope(client, accountId, characterId);
        if (existing) return { status: 200, data: { id: existing.uuid, state: existing.state, created: false }, targetUuid: existing.uuid };
        const ins = await holdsRepo.insertHold(client, { accountId, characterId, kind: 'manual', state: 'active', evidence: { manual: true, by: admin.loginId }, createdAt: getNow(), note: body.note });
        return { status: 201, data: { id: ins.uuid, state: 'active', created: true }, targetUuid: ins.uuid };
      },
    });
  } catch (err) {
    if (isUniqueViolation(err, 'economy_holds_one_active')) throw new AppError(409, '이미 정지 중입니다.', 'HOLD_EXISTS');
    throw err;
  }
}

// ---------- H6 ----------

export async function accountLinks(admin: AdminCtx, ip: string, uuid: string, q: LinksQuery) {
  const db = getPool();
  const accountId = await repo.accountIdByUuid(db, uuid);
  if (accountId === null) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
  const full = q.full_ip === 'true';
  if (full && admin.role !== 'owner') throw new AppError(403, '권한이 없습니다.', 'FORBIDDEN_ROLE');
  // 원본 IP 열람은 owner 권한과 감사 기록
  await auditView(admin, ip, full ? 'account.links_full_ip' : 'account.links', 'account', uuid, { full_ip: full });
  const devices = await repo.deviceRows(db, accountId);
  const ips = await repo.ipRows(db, accountId);
  const linked = new Map<string, { via: Set<string>; last: Date | null }>();
  for (const l of await repo.linkedAccounts(db, accountId)) {
    const e = linked.get(l.account_uuid) ?? { via: new Set<string>(), last: null };
    e.via.add(l.via);
    if (l.last_seen_at && (!e.last || l.last_seen_at > e.last)) e.last = l.last_seen_at;
    linked.set(l.account_uuid, e);
  }
  return {
    devices: devices.map((d) => ({ device_label: d.device_hash.slice(0, 8), first_seen_at: d.first_seen_at.toISOString(), last_seen_at: d.last_seen_at.toISOString(), accounts_on_device: d.accounts })),
    ips: ips.map((i) => ({ ...(full ? { ip: i.ip } : {}), ip_group: ipGroup(i.ip), last_seen_at: i.last_seen_at.toISOString(), accounts_on_ip: i.accounts })),
    steam: await repo.steamKeyAndCount(db, accountId),
    linked_accounts: [...linked].map(([account_id, v]) => ({ account_id, via: [...v.via].sort(), last_seen_at: iso(v.last) })),
  };
}

// ---------- H7 ----------

export async function tradeFlags(q: TradeFlagsQuery) {
  const rows = await repo.listTradeFlags(getPool(), { flag: q.flag, since: q.since ? new Date(q.since) : undefined, minPrice: q.min_price, cursor: repo.decodeCursor(q.cursor), limit: q.limit });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return {
    items: page.map((t) => ({
      trade_id: t.trade_uuid,
      item_key: t.item_key,
      price: t.price,
      seller: { account_id: t.seller_account_uuid, character: { id: t.seller_character_uuid, name: t.seller_name } },
      buyer: { account_id: t.buyer_account_uuid, character: { id: t.buyer_character_uuid, name: t.buyer_name } },
      flags: t.flags,
      traded_at: t.traded_at.toISOString(),
    })),
    next_cursor: rows.length > q.limit && last ? repo.encodeCursor(last.trade_id) : null,
  };
}

// ---------- H8, H9 ----------

export function incomeCaps() {
  const cfg = getConfig().aa;
  const caps = getIncomeCaps();
  return {
    mode: cfg.hold.mode,
    mult: cfg.hold.mult,
    floors: { xp: cfg.hold.minXp, gold_eq: cfg.hold.minGoldEq, ore: cfg.hold.minOre, essence: cfg.hold.minEssence, unique_plus: cfg.hold.minUnique },
    kills_per_minute: { config: cfg.hold.killsPerMin, caps_file: caps?.killsPerMinute ?? null },
    caps,
  };
}

export async function characterVelocity(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const c = await repo.characterByUuid(db, uuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  await auditView(admin, ip, 'character.velocity', 'character', uuid);
  const now = getNow();
  const caps = getIncomeCaps();
  const recent = await repo.recentHoldsOfAccount(db, c.account_id, c.id, 10);
  return {
    character: { id: uuid, name: c.name, level: c.level },
    band: caps ? (({ minLevel, maxLevel }) => ({ min_level: minLevel, max_level: maxLevel }))(bandOf(caps, c.level)) : null,
    windows: await windowsOf(db, c.id, c.level, now, c.account_id),
    window_hours: WINDOW_HOURS,
    recent_holds: recent.map((h) => ({ id: h.uuid, state: h.state, kind: h.kind, created_at: h.created_at.toISOString() })),
  };
}

