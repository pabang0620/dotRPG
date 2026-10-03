import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface BlockRow {
  id: number;
  uuid: string;
  blocker_account_id: number;
  blocked_account_id: number;
  blocked_name: string;
  created_at: Date;
  deleted_at: Date | null;
}
interface Raw extends Omit<BlockRow, 'id' | 'blocker_account_id' | 'blocked_account_id'> {
  id: string;
  blocker_account_id: string;
  blocked_account_id: string;
}
const COLS = 'id, uuid, blocker_account_id, blocked_account_id, blocked_name, created_at, deleted_at';
const to = (r: Raw): BlockRow => ({
  ...r,
  id: Number(r.id),
  blocker_account_id: Number(r.blocker_account_id),
  blocked_account_id: Number(r.blocked_account_id),
});

export async function listLive(db: Queryable, accountId: number): Promise<BlockRow[]> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM blocks WHERE blocker_account_id = $1 AND deleted_at IS NULL ORDER BY created_at DESC`, [accountId]);
  return r.rows.map(to);
}

export async function findLive(db: Queryable, blocker: number, blocked: number): Promise<BlockRow | null> {
  const r = await db.query<Raw>(
    `SELECT ${COLS} FROM blocks WHERE blocker_account_id = $1 AND blocked_account_id = $2 AND deleted_at IS NULL`,
    [blocker, blocked],
  );
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function findByUuid(db: Queryable, uuid: string): Promise<BlockRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM blocks WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function countLive(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM blocks WHERE blocker_account_id = $1 AND deleted_at IS NULL', [accountId]);
  return Number((r.rows[0] as { n: string }).n);
}

export async function insertBlock(client: PoolClient, blocker: number, blocked: number, blockedChar: number, name: string): Promise<BlockRow> {
  const r = await client.query<Raw>(
    `INSERT INTO blocks (blocker_account_id, blocked_account_id, blocked_character_id, blocked_name)
     VALUES ($1, $2, $3, $4) RETURNING ${COLS}`,
    [blocker, blocked, blockedChar, name],
  );
  return to(r.rows[0] as Raw);
}

/** 두 사람 사이의 친구 관계는 삭제, 대기 요청은 내가 받은 것이면 거절, 보낸 것이면 취소 */
export async function closeFriendships(client: PoolClient, me: number, other: number): Promise<boolean> {
  const r = await client.query(
    `UPDATE friendships
        SET state = CASE WHEN state = 'accepted' THEN 'removed' WHEN requester_account_id = $1 THEN 'cancelled' ELSE 'declined' END,
            responded_at = CASE WHEN state = 'pending' AND requester_account_id <> $1 THEN now() ELSE responded_at END,
            ended_at = now(), ended_by_account_id = $1
      WHERE state IN ('pending', 'accepted')
        AND ((requester_account_id = $1 AND target_account_id = $2) OR (requester_account_id = $2 AND target_account_id = $1))`,
    [me, other],
  );
  return (r.rowCount ?? 0) > 0;
}

export async function cancelInvitesBetween(
  client: PoolClient,
  me: number,
  other: number,
): Promise<{ uuid: string; inviter: number; invitee: number; silent: boolean }[]> {
  const r = await client.query<{ uuid: string; inviter: string; invitee: string; silent: boolean }>(
    `UPDATE party_invites pi SET state = 'cancelled'
       FROM characters ic
      WHERE pi.state = 'pending' AND pi.inviter_character_id = ic.id
        AND ((pi.invitee_account_id = $2 AND ic.account_id = $1) OR (pi.invitee_account_id = $1 AND ic.account_id = $2))
      RETURNING pi.uuid, pi.inviter_character_id AS inviter, pi.invitee_character_id AS invitee, pi.silent`,
    [me, other],
  );
  return r.rows.map((x) => ({ uuid: x.uuid, inviter: Number(x.inviter), invitee: Number(x.invitee), silent: x.silent }));
}

export async function release(db: Queryable, id: number): Promise<boolean> {
  const r = await db.query('UPDATE blocks SET deleted_at = now() WHERE id = $1 AND deleted_at IS NULL', [id]);
  return (r.rowCount ?? 0) > 0;
}
