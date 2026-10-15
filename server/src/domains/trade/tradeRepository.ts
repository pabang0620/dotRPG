// 교환 SQL. 변경 문장은 호출하는 Service가 두 캐릭터 행(id 순서)과 그 거래 행을 잠근 뒤에만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export type TradeState = 'pending' | 'active' | 'completed' | 'declined' | 'cancelled' | 'expired' | 'failed';
export type Side = 'requester' | 'target';
export const OPEN_STATES: TradeState[] = ['pending', 'active'];

export interface TradeRow {
  id: number;
  uuid: string;
  requesterCharacterId: number;
  targetCharacterId: number;
  requesterAccountId: number;
  targetAccountId: number;
  state: TradeState;
  closeReason: string | null;
  version: number;
  requesterGold: number;
  targetGold: number;
  requesterLocked: boolean;
  targetLocked: boolean;
  requesterConfirmed: boolean;
  targetConfirmed: boolean;
  expiresAt: Date;
  createdAt: Date;
  acceptedAt: Date | null;
  closedAt: Date | null;
}

interface Raw {
  id: string;
  uuid: string;
  requester_character_id: string;
  target_character_id: string;
  requester_account_id: string;
  target_account_id: string;
  state: TradeState;
  close_reason: string | null;
  version: number;
  requester_gold: string;
  target_gold: string;
  requester_locked: boolean;
  target_locked: boolean;
  requester_confirmed: boolean;
  target_confirmed: boolean;
  expires_at: Date;
  created_at: Date;
  accepted_at: Date | null;
  closed_at: Date | null;
}

const COLS = `id, uuid, requester_character_id, target_character_id, requester_account_id, target_account_id, state, close_reason, version,
  requester_gold, target_gold, requester_locked, target_locked, requester_confirmed, target_confirmed, expires_at, created_at, accepted_at, closed_at`;

const to = (r: Raw): TradeRow => ({
  id: Number(r.id),
  uuid: r.uuid,
  requesterCharacterId: Number(r.requester_character_id),
  targetCharacterId: Number(r.target_character_id),
  requesterAccountId: Number(r.requester_account_id),
  targetAccountId: Number(r.target_account_id),
  state: r.state,
  closeReason: r.close_reason,
  version: r.version,
  requesterGold: Number(r.requester_gold),
  targetGold: Number(r.target_gold),
  requesterLocked: r.requester_locked,
  targetLocked: r.target_locked,
  requesterConfirmed: r.requester_confirmed,
  targetConfirmed: r.target_confirmed,
  expiresAt: r.expires_at,
  createdAt: r.created_at,
  acceptedAt: r.accepted_at,
  closedAt: r.closed_at,
});

export interface TradeItemRow {
  tradeId: number;
  side: Side;
  itemKey: string;
  count: number;
  itemUuid: string;
}

export async function findByUuid(db: Queryable, uuid: string): Promise<TradeRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM trades WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function lockByUuid(client: PoolClient, uuid: string): Promise<TradeRow | null> {
  const r = await client.query<Raw>(`SELECT ${COLS} FROM trades WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

/** 캐릭터가 낀 열린 거래(요청 받은 것 포함). 캐릭터 행이 잠겨 있으면 정확하다 */
export async function openOfCharacter(db: Queryable, characterId: number): Promise<TradeRow | null> {
  const r = await db.query<Raw>(
    `SELECT ${COLS} FROM trades WHERE state IN ('pending', 'active') AND (requester_character_id = $1 OR target_character_id = $1) ORDER BY id DESC LIMIT 1`,
    [characterId],
  );
  return r.rows[0] ? to(r.rows[0]) : null;
}

/** 가장 최근에 닫힌 거래(since 이후). 상대가 확정해 끝난 결과를 폴링으로 알려 주는 용도 */
export async function latestClosedOf(db: Queryable, characterId: number, since: Date): Promise<TradeRow | null> {
  const r = await db.query<Raw>(
    `SELECT ${COLS} FROM trades WHERE closed_at >= $2 AND (requester_character_id = $1 OR target_character_id = $1) ORDER BY closed_at DESC, id DESC LIMIT 1`,
    [characterId, since],
  );
  return r.rows[0] ? to(r.rows[0]) : null;
}

/** 시간이 지난 열린 거래를 닫는다(pending 60초, active 마지막 변경 뒤 일정 시간) */
export async function expireDue(db: Queryable, characterIds: number[], now: Date): Promise<void> {
  await db.query(
    `UPDATE trades SET state = 'expired', close_reason = 'timeout', closed_at = $2
      WHERE state IN ('pending', 'active') AND expires_at <= $2
        AND (requester_character_id = ANY($1::bigint[]) OR target_character_id = ANY($1::bigint[]))`,
    [characterIds, now],
  );
}

export async function insertTrade(
  client: PoolClient,
  requester: { characterId: number; accountId: number },
  target: { characterId: number; accountId: number },
  expiresAt: Date,
): Promise<TradeRow> {
  const r = await client.query<Raw>(
    `INSERT INTO trades (requester_character_id, target_character_id, requester_account_id, target_account_id, expires_at)
     VALUES ($1, $2, $3, $4, $5) RETURNING ${COLS}`,
    [requester.characterId, target.characterId, requester.accountId, target.accountId, expiresAt],
  );
  return to(r.rows[0] as Raw);
}

export async function setActive(client: PoolClient, id: number, now: Date, expiresAt: Date): Promise<void> {
  await client.query("UPDATE trades SET state = 'active', accepted_at = $2, expires_at = $3 WHERE id = $1", [id, now, expiresAt]);
}

export async function closeTrade(db: Queryable, id: number, state: TradeState, reason: string, now: Date): Promise<void> {
  await db.query('UPDATE trades SET state = $2, close_reason = $3, closed_at = $4 WHERE id = $1', [id, state, reason, now]);
}

/** 내 제안을 바꾼다: version +1, 양쪽 잠금·확정 해제, 열린 시간 연장 */
export async function setOffer(client: PoolClient, id: number, side: Side, gold: number, expiresAt: Date): Promise<number> {
  const col = side === 'requester' ? 'requester_gold' : 'target_gold';
  const r = await client.query<{ version: number }>(
    `UPDATE trades SET ${col} = $2, version = version + 1, requester_locked = false, target_locked = false,
            requester_confirmed = false, target_confirmed = false, expires_at = $3
      WHERE id = $1 RETURNING version`,
    [id, gold, expiresAt],
  );
  return (r.rows[0] as { version: number }).version;
}

export async function replaceItems(
  client: PoolClient,
  tradeId: number,
  side: Side,
  items: { itemKey: string; count: number; itemUuid: string }[],
): Promise<void> {
  await client.query('DELETE FROM trade_items WHERE trade_id = $1 AND side = $2', [tradeId, side]);
  for (const i of items) {
    await client.query('INSERT INTO trade_items (trade_id, side, item_key, count, item_uuid) VALUES ($1, $2, $3, $4, $5)', [
      tradeId,
      side,
      i.itemKey,
      i.count,
      i.itemUuid,
    ]);
  }
}

export async function setLocked(client: PoolClient, id: number, side: Side): Promise<void> {
  const col = side === 'requester' ? 'requester_locked' : 'target_locked';
  await client.query(`UPDATE trades SET ${col} = true WHERE id = $1`, [id]);
}

export async function setConfirmed(client: PoolClient, id: number, side: Side): Promise<void> {
  const col = side === 'requester' ? 'requester_confirmed' : 'target_confirmed';
  await client.query(`UPDATE trades SET ${col} = true WHERE id = $1`, [id]);
}

export async function itemsOf(db: Queryable, tradeId: number): Promise<TradeItemRow[]> {
  const r = await db.query<{ trade_id: string; side: Side; item_key: string; count: number; item_uuid: string }>(
    'SELECT trade_id, side, item_key, count, item_uuid FROM trade_items WHERE trade_id = $1 ORDER BY id',
    [tradeId],
  );
  return r.rows.map((x) => ({ tradeId: Number(x.trade_id), side: x.side, itemKey: x.item_key, count: x.count, itemUuid: x.item_uuid }));
}

export async function namesOf(db: Queryable, characterIds: number[]): Promise<Map<number, string>> {
  const r = await db.query<{ id: string; name: string }>('SELECT id, name FROM characters WHERE id = ANY($1::bigint[])', [characterIds]);
  return new Map(r.rows.map((x) => [Number(x.id), x.name]));
}

// ---------- 상대 찾기·검사 ----------

export interface TargetChar {
  id: number;
  accountId: number;
}

export async function findAliveByName(db: Queryable, name: string): Promise<TargetChar | null> {
  const r = await db.query<{ id: string; account_id: string }>(
    'SELECT id, account_id FROM characters WHERE lower(name) = lower($1) AND deleted_at IS NULL LIMIT 1',
    [name],
  );
  return r.rows[0] ? { id: Number(r.rows[0].id), accountId: Number(r.rows[0].account_id) } : null;
}

export async function isBlocked(db: Queryable, blockerAccountId: number, blockedAccountId: number): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM blocks WHERE blocker_account_id = $1 AND blocked_account_id = $2 AND deleted_at IS NULL', [
    blockerAccountId,
    blockedAccountId,
  ]);
  return r.rows.length > 0;
}

/** 온라인 = 세션이 끝나지 않았고 onlineSince 이후에 신호가 있었다(online_sessions 주석) */
export async function isOnline(db: Queryable, characterId: number, onlineSince: Date): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM online_sessions WHERE character_id = $1 AND ended_at IS NULL AND last_seen_at > $2', [characterId, onlineSince]);
  return r.rows.length > 0;
}

// ---------- 가방 ----------

export interface BagStack {
  uuid: string;
  itemKey: string;
  count: number;
  bind: 'none' | 'account' | 'character';
}

/** 내 가방 스택을 uuid로 찾는다(남의 것·가방 밖은 null) */
export async function bagStackByUuid(db: Queryable, characterId: number, uuid: string): Promise<BagStack | null> {
  const r = await db.query<{ uuid: string; item_key: string; count: number; bind: BagStack['bind'] }>(
    "SELECT uuid, item_key, count, bind FROM character_items WHERE uuid = $1 AND character_id = $2 AND location = 'bag'",
    [uuid, characterId],
  );
  const x = r.rows[0];
  return x ? { uuid: x.uuid, itemKey: x.item_key, count: x.count, bind: x.bind } : null;
}

/** 귀속 없는 가방 수량 */
export async function bagCountTradable(db: Queryable, characterId: number, itemKey: string): Promise<number> {
  const r = await db.query<{ n: string }>(
    "SELECT coalesce(sum(count), 0) AS n FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2 AND bind = 'none'",
    [characterId, itemKey],
  );
  return Number((r.rows[0] as { n: string }).n);
}

// ---------- 관리자 조회(API는 이번 범위 밖) ----------

export interface AdminTradeRow extends TradeRow {
  requesterName: string;
  targetName: string;
}

export async function listForAdmin(db: Queryable, o: { accountId?: number; characterId?: number; limit: number; offset: number }): Promise<AdminTradeRow[]> {
  const r = await db.query<Raw & { requester_name: string; target_name: string }>(
    `SELECT t.id, t.uuid, t.requester_character_id, t.target_character_id, t.requester_account_id, t.target_account_id, t.state, t.close_reason,
            t.version, t.requester_gold, t.target_gold, t.requester_locked, t.target_locked, t.requester_confirmed, t.target_confirmed,
            t.expires_at, t.created_at, t.accepted_at, t.closed_at, rc.name AS requester_name, tc.name AS target_name
       FROM trades t
       JOIN characters rc ON rc.id = t.requester_character_id
       JOIN characters tc ON tc.id = t.target_character_id
      WHERE ($1::bigint IS NULL OR t.requester_account_id = $1 OR t.target_account_id = $1)
        AND ($2::bigint IS NULL OR t.requester_character_id = $2 OR t.target_character_id = $2)
      ORDER BY t.id DESC LIMIT $3 OFFSET $4`,
    [o.accountId ?? null, o.characterId ?? null, o.limit, o.offset],
  );
  return r.rows.map((x) => ({ ...to(x), requesterName: x.requester_name, targetName: x.target_name }));
}

export async function itemsForAdmin(db: Queryable, tradeIds: number[]): Promise<TradeItemRow[]> {
  const r = await db.query<{ trade_id: string; side: Side; item_key: string; count: number; item_uuid: string }>(
    'SELECT trade_id, side, item_key, count, item_uuid FROM trade_items WHERE trade_id = ANY($1::bigint[]) ORDER BY trade_id, id',
    [tradeIds],
  );
  return r.rows.map((x) => ({ tradeId: Number(x.trade_id), side: x.side, itemKey: x.item_key, count: x.count, itemUuid: x.item_uuid }));
}
