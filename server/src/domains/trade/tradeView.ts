// 교환 상태 응답: 클라이언트가 1초마다 폴링해 그대로 그린다.
import type { Queryable } from '../../db/pool';
import * as repo from './tradeRepository';
import type { Side, TradeItemRow, TradeRow } from './tradeRepository';

const itemView = (i: TradeItemRow) => ({ item_uuid: i.itemUuid, item_key: i.itemKey, count: i.count });

function party(t: TradeRow, side: Side, items: TradeItemRow[], name: string) {
  const r = side === 'requester';
  return {
    name,
    gold: r ? t.requesterGold : t.targetGold,
    items: items.filter((i) => i.side === side).map(itemView),
    locked: r ? t.requesterLocked : t.targetLocked,
    confirmed: r ? t.requesterConfirmed : t.targetConfirmed,
  };
}

export const sideOf = (t: TradeRow, characterId: number): Side => (t.requesterCharacterId === characterId ? 'requester' : 'target');

export async function buildView(db: Queryable, t: TradeRow, myCharacterId: number, now: Date) {
  const items = await repo.itemsOf(db, t.id);
  const names = await repo.namesOf(db, [t.requesterCharacterId, t.targetCharacterId]);
  const mine = sideOf(t, myCharacterId);
  const other: Side = mine === 'requester' ? 'target' : 'requester';
  const nameOf = (s: Side) => names.get(s === 'requester' ? t.requesterCharacterId : t.targetCharacterId) ?? '';
  return {
    id: t.uuid,
    state: t.state,
    close_reason: t.closeReason,
    role: mine,
    version: t.version,
    expires_at: t.expiresAt.toISOString(),
    closed_at: t.closedAt ? t.closedAt.toISOString() : null,
    server_time: now.toISOString(),
    me: party(t, mine, items, nameOf(mine)),
    other: party(t, other, items, nameOf(other)),
  };
}
