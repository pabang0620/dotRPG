// 양쪽이 확정했을 때의 교환 실행. 호출 쪽(Service)이 두 캐릭터 행(id 순서)과 거래 행을 이미 잠갔다.
// 순서: 정지 확인 -> 재검증(전부 읽기) -> 상한 확인 -> 이동. 재검증이 실패하면 아무것도 쓰기 전에 TradeAbort로 거래를 닫는다.
import { getConfig } from '../../config/env';
import { assertNoHold } from '../antiabuse/holds';
import { AppError } from '../../utils/AppError';
import type { Delta } from '../economy/economyContext';
import {
  decStack,
  insertGoldLedger,
  insertItemLedger,
  stackCount,
  updateGold,
  upsertStack,
  type LockedChar,
} from '../economy/economyRepository';
import * as repo from './tradeRepository';
import type { Side, TradeItemRow, TradeRow } from './tradeRepository';
import { TradeAbort, type TradeCtx } from './tradeRunner';

const INVALID = '제안한 내용이 달라져 교환이 취소되었습니다.';

interface Party {
  side: Side;
  char: LockedChar;
  gold: number;
  items: TradeItemRow[];
}

function abort(t: TradeRow, status: number, message: string, code: string, reason: string, extra?: Record<string, unknown>): TradeAbort {
  return new TradeAbort(status, message, code, { tradeId: t.id, state: 'failed', reason }, extra);
}

/** 교환을 실행하고 요청한 쪽(ctx.me)의 변동을 돌려준다 */
export async function executeTrade(ctx: TradeCtx, t: TradeRow, items: TradeItemRow[]): Promise<Delta> {
  const req = ctx.chars.get(t.requesterCharacterId) as LockedChar;
  const tgt = ctx.chars.get(t.targetCharacterId) as LockedChar;
  const parties: Party[] = [
    { side: 'requester', char: req, gold: t.requesterGold, items: items.filter((i) => i.side === 'requester') },
    { side: 'target', char: tgt, gold: t.targetGold, items: items.filter((i) => i.side === 'target') },
  ];
  const whose = (p: Party): 'me' | 'other' => (p.char.id === ctx.me.id ? 'me' : 'other');

  // 1. 경제 정지: 내 것은 그대로 알리고, 상대 것은 거래만 닫는다
  for (const p of parties) {
    try {
      await assertNoHold(ctx.client, p.char.accountId, p.char.id);
    } catch (err) {
      if (whose(p) === 'me' || !(err instanceof AppError)) throw err;
      throw abort(t, 409, INVALID, 'TRADE_INVALID', 'hold', { who: 'other' });
    }
  }
  // 2. 재검증: 그새 쓰거나 팔았거나 합쳤으면 실패하고 거래를 닫는다
  for (const p of parties) {
    if (p.gold > p.char.gold) throw abort(t, 409, INVALID, 'TRADE_INVALID', 'gold_changed', { who: whose(p) });
    for (const i of p.items) {
      if ((await repo.bagCountTradable(ctx.client, p.char.id, i.itemKey)) < i.count) {
        throw abort(t, 409, INVALID, 'TRADE_INVALID', 'items_changed', { who: whose(p), item_key: i.itemKey });
      }
    }
  }
  // 3. 골드 상한: 받는 쪽이 넘치면 클라이언트 int가 넘으므로 교환 자체를 거절한다(깎아서 지급하지 않는다)
  const cap = getConfig().auction.goldClientMax;
  const finalGold = (p: Party, other: Party): number => p.char.gold - p.gold + other.gold;
  for (const [p, other] of [[parties[0], parties[1]], [parties[1], parties[0]]] as [Party, Party][]) {
    if (finalGold(p, other) > cap) {
      throw abort(t, 422, '골드가 보유 상한을 넘어 받을 수 없습니다.', 'GOLD_CAP_EXCEEDED', 'gold_cap', { who: whose(p), cap });
    }
  }

  // 4. 이동: 원장 한 줄씩(준 쪽 trade_out, 받은 쪽 trade_in), ref = 거래 uuid
  const touched = new Map<string, number>();
  const balance = new Map<number, number>([[req.id, req.gold], [tgt.id, tgt.gold]]);
  const rid = ctx.requestId;
  for (const [from, to] of [[parties[0], parties[1]], [parties[1], parties[0]]] as [Party, Party][]) {
    if (from.gold > 0) {
      const f = (balance.get(from.char.id) as number) - from.gold;
      balance.set(from.char.id, f);
      await updateGold(ctx.client, from.char.id, f);
      await insertGoldLedger(ctx.client, from.char.id, -from.gold, f, 'trade_out', t.uuid, rid);
      const g = (balance.get(to.char.id) as number) + from.gold;
      balance.set(to.char.id, g);
      await updateGold(ctx.client, to.char.id, g);
      await insertGoldLedger(ctx.client, to.char.id, from.gold, g, 'trade_in', t.uuid, rid);
    }
    for (const i of from.items) {
      const taken = await decStack(ctx.client, from.char.id, 'bag', i.itemKey, i.count, 'none');
      if (taken === null) throw new Error(`trade ${t.uuid}: item missing after validation`);
      const left = await stackCount(ctx.client, from.char.id, 'bag', i.itemKey);
      await insertItemLedger(ctx.client, from.char.id, i.itemKey, -i.count, left, 'bag', 'trade_out', t.uuid, rid);
      await upsertStack(ctx.client, to.char.id, 'bag', i.itemKey, i.count, 'none');
      const have = await stackCount(ctx.client, to.char.id, 'bag', i.itemKey);
      await insertItemLedger(ctx.client, to.char.id, i.itemKey, i.count, have, 'bag', 'trade_in', t.uuid, rid);
      touched.set(i.itemKey, 0);
    }
  }
  await repo.closeTrade(ctx.client, t.id, 'completed', 'completed', ctx.now);

  const delta: Delta = {};
  const mine = balance.get(ctx.me.id) as number;
  if (mine !== ctx.me.gold) delta.gold = mine;
  if (touched.size > 0) {
    delta.stacks = [];
    for (const key of touched.keys()) {
      delta.stacks.push({ item_key: key, location: 'bag', bind: 'none', count: await repo.bagCountTradable(ctx.client, ctx.me.id, key) });
    }
  }
  return delta;
}
