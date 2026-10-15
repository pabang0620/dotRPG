// 1:1 교환: 요청, 수락·거절·취소, 제안 교체, 잠금, 확정. 폴링용 현재 상태 조회.
// 락 순서: 두 캐릭터 행(id 오름차순) -> 거래 행. 제안(골드·품목)이 바뀌면 양쪽 잠금·확정이 풀린다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { parseItemKey } from '../../utils/itemKey';
import { ownedChar } from '../auction/auctionSearchRepository';
import { assertNotActive as assertNotInMiniDungeon } from '../minidungeon/miniDungeonRepository';
import type { EconResult, StoredResult } from '../economy/economyService';
import { executeTrade } from './tradeExecute';
import * as repo from './tradeRepository';
import type { TradeRow } from './tradeRepository';
import { runTrade, TradeAbort, type TradeCtx } from './tradeRunner';
import { buildView, sideOf } from './tradeView';
import type { ConfirmBody, CreateBody, LockBody, OfferBody, PlainBody } from './tradeValidation';

/** 요청(pending)은 60초 뒤 만료. 진행 중(active)은 마지막 변경 뒤 10분 동안 아무 일이 없으면 만료 */
export const REQUEST_SECONDS = 60;
export const ACTIVE_SECONDS = 600;
export const MAX_OFFER_ITEMS = 8;
/** 끝난 거래를 current 응답에 함께 알려 주는 시간 */
const CLOSED_VISIBLE_SECONDS = 120;

const NOT_FOUND = () => new AppError(404, '교환을 찾을 수 없습니다.', 'TRADE_NOT_FOUND');
const CLOSED = (state: string) => new AppError(409, '이미 끝난 교환입니다.', 'TRADE_CLOSED', { state });
const MISMATCH = () => new AppError(409, '교환 내용이 바뀌었습니다. 다시 확인하세요.', 'TRADE_VERSION_MISMATCH');
const NOT_ACTIVE = () => new AppError(409, '상대가 아직 수락하지 않았습니다.', 'TRADE_NOT_ACTIVE');

const plus = (now: Date, seconds: number): Date => new Date(now.getTime() + seconds * 1000);

async function mustOwn(accountId: number, characterUuid: string) {
  const c = await ownedChar(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

// ---------- 폴링 ----------

/** T2 내가 낀 열린 거래(요청 받은 것 포함). 없으면 막 끝난 거래를 last_closed로 알린다 */
export async function currentTrade(accountId: number, characterUuid: string) {
  const me = await mustOwn(accountId, characterUuid);
  const now = getNow();
  await repo.expireDue(getPool(), [me.id], now);
  const open = await repo.openOfCharacter(getPool(), me.id);
  const closed = open ? null : await repo.latestClosedOf(getPool(), me.id, plus(now, -CLOSED_VISIBLE_SECONDS));
  return {
    trade: open ? await buildView(getPool(), open, me.id, now) : null,
    last_closed: closed ? await buildView(getPool(), closed, me.id, now) : null,
    server_time: now.toISOString(),
  };
}

// ---------- 공통 ----------

interface ByTrade {
  accountId: number;
  characterUuid: string;
  tradeUuid: string;
  endpoint: string;
  requestId?: string;
  payload: unknown;
  handler: (ctx: TradeCtx, t: TradeRow) => Promise<EconResult>;
}

/** 거래 uuid로 하는 요청: 미리 읽어 두 캐릭터를 알아낸 뒤 id 순서로 잠그고, 잠긴 상태에서 거래 행을 다시 읽는다 */
function runByTrade(o: ByTrade): Promise<StoredResult> {
  return (async () => {
    const me = await mustOwn(o.accountId, o.characterUuid);
    const pre = await repo.findByUuid(getPool(), o.tradeUuid);
    if (!pre || (pre.requesterCharacterId !== me.id && pre.targetCharacterId !== me.id)) throw NOT_FOUND();
    return runTrade({
      accountId: o.accountId,
      characterUuid: o.characterUuid,
      endpoint: o.endpoint,
      ...(o.requestId ? { requestId: o.requestId } : {}),
      payload: { trade: o.tradeUuid, ...(o.payload as object) },
      lockIds: [pre.requesterCharacterId, pre.targetCharacterId],
      handler: async (ctx) => {
        const t = await repo.lockByUuid(ctx.client, o.tradeUuid);
        if (!t) throw NOT_FOUND();
        if ((t.state === 'pending' || t.state === 'active') && t.expiresAt.getTime() <= ctx.now.getTime()) {
          throw new TradeAbort(410, '교환 시간이 지났습니다.', 'TRADE_EXPIRED', { tradeId: t.id, state: 'expired', reason: 'timeout' });
        }
        return o.handler(ctx, t);
      },
    });
  })();
}

const viewOf = async (ctx: TradeCtx, t: TradeRow, status = 200, extra: Record<string, unknown> = {}): Promise<EconResult> => {
  const fresh = (await repo.findByUuid(ctx.client, t.uuid)) as TradeRow;
  return { status, data: { trade: await buildView(ctx.client, fresh, ctx.me.id, ctx.now), ...extra } };
};

// ---------- T1 요청 ----------

export async function createTrade(accountId: number, characterUuid: string, body: CreateBody): Promise<StoredResult> {
  const me = await mustOwn(accountId, characterUuid);
  const target = await repo.findAliveByName(getPool(), body.target_name);
  if (!target) throw new AppError(404, '모험가를 찾을 수 없습니다.', 'PLAYER_NOT_FOUND');
  if (target.accountId === accountId) throw new AppError(422, '자기 자신에게는 할 수 없습니다.', 'CANNOT_TARGET_SELF');
  return runTrade({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/trades',
    requestId: body.request_id,
    payload: { target: target.id },
    lockIds: [me.id, target.id],
    handler: async (ctx) => {
      const c = ctx.client;
      await assertNotInMiniDungeon(c, me.id, ctx.now);
      await assertNotInMiniDungeon(c, target.id, ctx.now);
      await repo.expireDue(c, [me.id, target.id], ctx.now);
      const mine = await repo.openOfCharacter(c, me.id);
      if (mine) {
        // 같은 상대에게 이미 보낸 요청이면 그대로 돌려준다
        if (mine.state === 'pending' && mine.requesterCharacterId === me.id && mine.targetCharacterId === target.id) return viewOf(ctx, mine);
        throw new AppError(409, '이미 진행 중인 교환이 있습니다.', 'TRADE_ALREADY_OPEN');
      }
      if (await repo.isBlocked(c, accountId, target.accountId)) {
        throw new AppError(409, '차단한 사람입니다. 먼저 차단을 해제하세요.', 'YOU_BLOCKED_TARGET');
      }
      // 상대가 나를 차단했으면 오프라인과 같은 응답을 준다(차단 사실을 드러내지 않는다)
      const onlineSince = plus(ctx.now, -getConfig().aa.presence.onlineSeconds);
      if ((await repo.isBlocked(c, target.accountId, accountId)) || !(await repo.isOnline(c, target.id, onlineSince))) {
        throw new AppError(422, '접속 중이 아닌 모험가입니다.', 'TARGET_OFFLINE');
      }
      if (await repo.openOfCharacter(c, target.id)) throw new AppError(409, '상대가 다른 교환 중입니다.', 'TARGET_BUSY');
      const t = await repo.insertTrade(c, { characterId: me.id, accountId }, { characterId: target.id, accountId: target.accountId }, plus(ctx.now, REQUEST_SECONDS));
      return viewOf(ctx, t, 201);
    },
  });
}

// ---------- T3 수락·거절·취소 ----------

export function acceptTrade(accountId: number, characterUuid: string, tradeUuid: string, body: PlainBody): Promise<StoredResult> {
  return runByTrade({
    accountId,
    characterUuid,
    tradeUuid,
    endpoint: 'POST /characters/:uuid/trades/:trade_id/accept',
    ...(body.request_id ? { requestId: body.request_id } : {}),
    payload: {},
    handler: async (ctx, t) => {
      if (t.targetCharacterId !== ctx.me.id) throw new AppError(403, '요청을 받은 사람만 할 수 있습니다.', 'NOT_TARGET');
      if (t.state === 'active') return viewOf(ctx, t);
      if (t.state !== 'pending') throw CLOSED(t.state);
      await assertNotInMiniDungeon(ctx.client, t.requesterCharacterId, ctx.now);
      await assertNotInMiniDungeon(ctx.client, t.targetCharacterId, ctx.now);
      await repo.setActive(ctx.client, t.id, ctx.now, plus(ctx.now, ACTIVE_SECONDS));
      return viewOf(ctx, t);
    },
  });
}

export function declineTrade(accountId: number, characterUuid: string, tradeUuid: string, body: PlainBody): Promise<StoredResult> {
  return runByTrade({
    accountId,
    characterUuid,
    tradeUuid,
    endpoint: 'POST /characters/:uuid/trades/:trade_id/decline',
    ...(body.request_id ? { requestId: body.request_id } : {}),
    payload: {},
    handler: async (ctx, t) => {
      if (t.targetCharacterId !== ctx.me.id) throw new AppError(403, '요청을 받은 사람만 할 수 있습니다.', 'NOT_TARGET');
      if (t.state === 'declined') return viewOf(ctx, t);
      if (t.state !== 'pending') throw CLOSED(t.state);
      await repo.closeTrade(ctx.client, t.id, 'declined', 'declined', ctx.now);
      return viewOf(ctx, t);
    },
  });
}

export function cancelTrade(accountId: number, characterUuid: string, tradeUuid: string, body: PlainBody): Promise<StoredResult> {
  return runByTrade({
    accountId,
    characterUuid,
    tradeUuid,
    endpoint: 'POST /characters/:uuid/trades/:trade_id/cancel',
    ...(body.request_id ? { requestId: body.request_id } : {}),
    payload: {},
    handler: async (ctx, t) => {
      if (t.state === 'cancelled') return viewOf(ctx, t);
      if (t.state !== 'pending' && t.state !== 'active') throw CLOSED(t.state);
      await repo.closeTrade(ctx.client, t.id, 'cancelled', `${sideOf(t, ctx.me.id)}_cancel`, ctx.now);
      return viewOf(ctx, t);
    },
  });
}

// ---------- T4 제안 교체 ----------

export function putOffer(accountId: number, characterUuid: string, tradeUuid: string, body: OfferBody): Promise<StoredResult> {
  return runByTrade({
    accountId,
    characterUuid,
    tradeUuid,
    endpoint: 'PUT /characters/:uuid/trades/:trade_id/offer',
    requestId: body.request_id,
    payload: { version: body.version, gold: body.gold, items: body.items },
    handler: async (ctx, t) => {
      if (t.state === 'pending') throw NOT_ACTIVE();
      if (t.state !== 'active') throw CLOSED(t.state);
      if (t.version !== body.version) throw MISMATCH();
      if (body.gold > ctx.me.gold) throw new AppError(422, '골드가 모자랍니다.', 'NOT_ENOUGH_GOLD', { need: body.gold, have: ctx.me.gold });
      if (new Set(body.items.map((i) => i.item_uuid)).size !== body.items.length) {
        throw new AppError(422, '같은 아이템을 두 번 올릴 수 없습니다.', 'DUPLICATE_ITEM');
      }
      const eco = getGameData().economy;
      const picked: { itemKey: string; count: number; itemUuid: string }[] = [];
      for (const it of body.items) {
        const stack = await repo.bagStackByUuid(ctx.client, ctx.me.id, it.item_uuid);
        if (!stack) throw new AppError(404, '아이템을 찾을 수 없습니다.', 'ITEM_NOT_FOUND');
        const base = eco.items.get(parseItemKey(stack.itemKey)?.base ?? stack.itemKey);
        if (!base || base.kind === 'currency') throw new AppError(422, '거래할 수 없는 아이템입니다.', 'NOT_TRADABLE');
        if (stack.bind !== 'none' || base.bind !== 'none') throw new AppError(422, '귀속된 아이템은 교환할 수 없습니다.', 'ITEM_BOUND');
        if (it.count > stack.count) {
          throw new AppError(422, '가방에 아이템이 부족합니다.', 'NOT_ENOUGH_ITEMS', { need: it.count, have: stack.count });
        }
        picked.push({ itemKey: stack.itemKey, count: it.count, itemUuid: stack.uuid });
      }
      const side = sideOf(t, ctx.me.id);
      await repo.replaceItems(ctx.client, t.id, side, picked);
      await repo.setOffer(ctx.client, t.id, side, body.gold, plus(ctx.now, ACTIVE_SECONDS));
      return viewOf(ctx, t);
    },
  });
}

// ---------- T5 잠금 ----------

export function lockTrade(accountId: number, characterUuid: string, tradeUuid: string, body: LockBody): Promise<StoredResult> {
  return runByTrade({
    accountId,
    characterUuid,
    tradeUuid,
    endpoint: 'POST /characters/:uuid/trades/:trade_id/lock',
    ...(body.request_id ? { requestId: body.request_id } : {}),
    payload: { version: body.version },
    handler: async (ctx, t) => {
      if (t.state === 'pending') throw NOT_ACTIVE();
      if (t.state !== 'active') throw CLOSED(t.state);
      if (t.version !== body.version) throw MISMATCH();
      const items = await repo.itemsOf(ctx.client, t.id);
      if (t.requesterGold + t.targetGold === 0 && items.length === 0) {
        throw new AppError(422, '교환할 골드나 아이템을 올려 주세요.', 'TRADE_EMPTY');
      }
      await repo.setLocked(ctx.client, t.id, sideOf(t, ctx.me.id));
      return viewOf(ctx, t);
    },
  });
}

// ---------- T6 확정(양쪽이 확정하면 이 요청 안에서 실행) ----------

export function confirmTrade(accountId: number, characterUuid: string, tradeUuid: string, body: ConfirmBody): Promise<StoredResult> {
  return runByTrade({
    accountId,
    characterUuid,
    tradeUuid,
    endpoint: 'POST /characters/:uuid/trades/:trade_id/confirm',
    requestId: body.request_id,
    payload: { version: body.version },
    handler: async (ctx, t) => {
      const side = sideOf(t, ctx.me.id);
      const mineConfirmed = side === 'requester' ? t.requesterConfirmed : t.targetConfirmed;
      // 다른 request_id로 다시 보낸 확정: 이미 끝났으면 결과만 알려 준다
      if (t.state === 'completed' && mineConfirmed) return viewOf(ctx, t);
      if (t.state === 'pending') throw NOT_ACTIVE();
      if (t.state !== 'active') throw CLOSED(t.state);
      if (t.version !== body.version) throw MISMATCH();
      if (!(t.requesterLocked && t.targetLocked)) throw new AppError(409, '양쪽이 잠근 뒤에 확정할 수 있습니다.', 'TRADE_NOT_LOCKED');
      if (!mineConfirmed) await repo.setConfirmed(ctx.client, t.id, side);
      const otherConfirmed = side === 'requester' ? t.targetConfirmed : t.requesterConfirmed;
      if (!otherConfirmed) return viewOf(ctx, t);
      const delta = await executeTrade(ctx, t, await repo.itemsOf(ctx.client, t.id));
      return viewOf(ctx, t, 200, { delta });
    },
  });
}
