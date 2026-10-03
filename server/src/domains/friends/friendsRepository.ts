// 친구 요청·관계 SQL(계정 단위). 쓰기는 Service가 계정 행 잠금 아래에서 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface FriendshipRow {
  id: number;
  uuid: string;
  requester_account_id: number;
  target_account_id: number;
  state: 'pending' | 'accepted' | 'declined' | 'cancelled' | 'removed';
  created_at: Date;
  responded_at: Date | null;
  silent: boolean;
}
interface Raw extends Omit<FriendshipRow, 'id' | 'requester_account_id' | 'target_account_id'> {
  id: string;
  requester_account_id: string;
  target_account_id: string;
}
const COLS = 'id, uuid, requester_account_id, target_account_id, state, created_at, responded_at, silent';
const to = (r: Raw): FriendshipRow => ({
  ...r,
  id: Number(r.id),
  requester_account_id: Number(r.requester_account_id),
  target_account_id: Number(r.target_account_id),
});

/** 대기 요청이 FRIEND_REQUEST_DAYS를 넘으면 취소로 닫는다(지연 만료) */
export async function expireOld(db: Queryable, accountId: number, days: number): Promise<void> {
  await db.query(
    `UPDATE friendships SET state = 'cancelled', ended_at = now()
      WHERE state = 'pending' AND created_at < now() - ($2::int * interval '1 day')
        AND (requester_account_id = $1 OR target_account_id = $1)`,
    [accountId, days],
  );
}

export async function findLive(db: Queryable, a: number, b: number): Promise<FriendshipRow | null> {
  const r = await db.query<Raw>(
    `SELECT ${COLS} FROM friendships
      WHERE state IN ('pending', 'accepted')
        AND LEAST(requester_account_id, target_account_id) = LEAST($1::bigint, $2::bigint)
        AND GREATEST(requester_account_id, target_account_id) = GREATEST($1::bigint, $2::bigint)`,
    [a, b],
  );
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function findByUuid(db: Queryable, uuid: string): Promise<FriendshipRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM friendships WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function countFriends(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM friendships WHERE state = 'accepted' AND (requester_account_id = $1 OR target_account_id = $1)`,
    [accountId],
  );
  return Number((r.rows[0] as { n: string }).n);
}
export async function countOutgoing(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM friendships WHERE state = 'pending' AND requester_account_id = $1`,
    [accountId],
  );
  return Number((r.rows[0] as { n: string }).n);
}
export async function countIncoming(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM friendships WHERE state = 'pending' AND NOT silent AND target_account_id = $1`,
    [accountId],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 내가 같은 상대에게 보냈다가 거절·취소된 요청의 재요청 쿨다운 남은 초(없으면 0) */
export async function rerequestWaitSec(db: Queryable, requester: number, target: number, hours: number): Promise<number> {
  const r = await db.query<{ sec: string | null }>(
    `SELECT ceil(extract(epoch FROM (max(created_at) + ($3::int * interval '1 hour') - now()))) AS sec
       FROM friendships WHERE requester_account_id = $1 AND target_account_id = $2 AND state IN ('declined', 'cancelled')`,
    [requester, target, hours],
  );
  const sec = Number(r.rows[0]?.sec ?? 0);
  return sec > 0 ? sec : 0;
}

export async function insertRequest(
  client: PoolClient,
  requester: number,
  target: number,
  requesterChar: number,
  targetChar: number,
  silent = false,
): Promise<FriendshipRow> {
  const r = await client.query<Raw>(
    `INSERT INTO friendships (requester_account_id, target_account_id, requester_character_id, target_character_id, silent)
     VALUES ($1, $2, $3, $4, $5) RETURNING ${COLS}`,
    [requester, target, requesterChar, targetChar, silent],
  );
  return to(r.rows[0] as Raw);
}

export async function accept(db: Queryable, id: number): Promise<FriendshipRow | null> {
  const r = await db.query<Raw>(
    `UPDATE friendships SET state = 'accepted', responded_at = now() WHERE id = $1 AND state = 'pending' RETURNING ${COLS}`,
    [id],
  );
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function decline(db: Queryable, id: number, by: number): Promise<boolean> {
  const r = await db.query(
    `UPDATE friendships SET state = 'declined', responded_at = now(), ended_at = now(), ended_by_account_id = $2
      WHERE id = $1 AND state = 'pending'`,
    [id, by],
  );
  return (r.rowCount ?? 0) > 0;
}

export async function cancel(db: Queryable, id: number, by: number): Promise<boolean> {
  const r = await db.query(
    `UPDATE friendships SET state = 'cancelled', ended_at = now(), ended_by_account_id = $2 WHERE id = $1 AND state = 'pending'`,
    [id, by],
  );
  return (r.rowCount ?? 0) > 0;
}

export async function remove(db: Queryable, id: number, by: number): Promise<boolean> {
  const r = await db.query(
    `UPDATE friendships SET state = 'removed', ended_at = now(), ended_by_account_id = $2 WHERE id = $1 AND state = 'accepted'`,
    [id, by],
  );
  return (r.rowCount ?? 0) > 0;
}

export interface RequestView {
  uuid: string;
  created_at: Date;
  char_uuid: string;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
}

/** 받은 요청(요청자 캐릭터)과 보낸 요청(대상 캐릭터) */
export async function pendingViews(db: Queryable, accountId: number, dir: 'in' | 'out'): Promise<RequestView[]> {
  const mine = dir === 'in' ? 'target_account_id' : 'requester_account_id';
  const charCol = dir === 'in' ? 'requester_character_id' : 'target_character_id';
  const r = await db.query<RequestView>(
    `SELECT f.uuid, f.created_at, c.uuid AS char_uuid, c.name, c.class, c.level
       FROM friendships f JOIN characters c ON c.id = f.${charCol}
      WHERE f.state = 'pending' AND f.${mine} = $1 ${dir === 'in' ? 'AND NOT f.silent' : ''} ORDER BY f.created_at DESC`,
    [accountId],
  );
  return r.rows;
}

export async function blockedEitherWay(db: Queryable, blocker: number, blocked: number): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM blocks WHERE blocker_account_id = $1 AND blocked_account_id = $2 AND deleted_at IS NULL', [blocker, blocked]);
  return r.rows.length > 0;
}

export async function ownedAliveCharacter(db: Queryable, accountId: number, uuid: string): Promise<{ id: number; name: string } | null> {
  const r = await db.query<{ id: string; name: string }>(
    'SELECT id, name FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL',
    [uuid, accountId],
  );
  return r.rows[0] ? { id: Number(r.rows[0].id), name: r.rows[0].name } : null;
}
