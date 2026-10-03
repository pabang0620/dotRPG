// 파티 초대 SQL. 쓰기는 Service가 캐릭터 잠금(partyTx) 아래에서 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface InviteRow {
  id: number;
  uuid: string;
  party_id: number;
  inviter_character_id: number;
  invitee_character_id: number;
  state: 'pending' | 'accepted' | 'declined' | 'expired' | 'cancelled';
  expires_at: Date;
  silent: boolean;
}
interface Raw extends Omit<InviteRow, 'id' | 'party_id' | 'inviter_character_id' | 'invitee_character_id'> {
  id: string;
  party_id: string;
  inviter_character_id: string;
  invitee_character_id: string;
}
const COLS = 'id, uuid, party_id, inviter_character_id, invitee_character_id, state, expires_at, silent';
const to = (r: Raw): InviteRow => ({
  ...r,
  id: Number(r.id),
  party_id: Number(r.party_id),
  inviter_character_id: Number(r.inviter_character_id),
  invitee_character_id: Number(r.invitee_character_id),
});

export async function findByUuid(db: Queryable, uuid: string): Promise<InviteRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM party_invites WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

/** 지연 만료: 시간이 지난 대기 초대를 expired로 닫는다(같은 파티·대상의 새 초대가 유니크 인덱스에 막히지 않게) */
export async function expireDue(db: Queryable, now: Date, scope: { partyId?: number; inviteeCharacterId?: number }): Promise<void> {
  await db.query(
    `UPDATE party_invites SET state = 'expired'
      WHERE state = 'pending' AND expires_at <= $1
        AND ($2::bigint IS NULL OR party_id = $2) AND ($3::bigint IS NULL OR invitee_character_id = $3)`,
    [now, scope.partyId ?? null, scope.inviteeCharacterId ?? null],
  );
}

export async function pendingFor(db: Queryable, partyId: number, inviteeCharacterId: number, now: Date): Promise<InviteRow | null> {
  const r = await db.query<Raw>(
    `SELECT ${COLS} FROM party_invites WHERE party_id = $1 AND invitee_character_id = $2 AND state = 'pending' AND expires_at > $3`,
    [partyId, inviteeCharacterId, now],
  );
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function countPendingOfParty(db: Queryable, partyId: number, now: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    "SELECT count(*) AS n FROM party_invites WHERE party_id = $1 AND state = 'pending' AND expires_at > $2",
    [partyId, now],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 쿨다운: 방장이 최근 windowSec 안에 보낸 초대 수(target을 주면 그 대상에게만) */
export async function countRecentBy(db: Queryable, inviterCharacterId: number, since: Date, target?: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM party_invites WHERE inviter_character_id = $1 AND created_at > $2
        AND ($3::bigint IS NULL OR invitee_character_id = $3)`,
    [inviterCharacterId, since, target ?? null],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function insertInvite(
  client: PoolClient,
  partyId: number,
  inviter: number,
  invitee: number,
  inviteeAccount: number,
  now: Date,
  expiresAt: Date,
  silent = false,
): Promise<InviteRow> {
  const r = await client.query<Raw>(
    `INSERT INTO party_invites (party_id, inviter_character_id, invitee_character_id, invitee_account_id, created_at, expires_at, silent)
     VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING ${COLS}`,
    [partyId, inviter, invitee, inviteeAccount, now, expiresAt, silent],
  );
  return to(r.rows[0] as Raw);
}

export async function setState(db: Queryable, id: number, state: 'accepted' | 'declined' | 'expired' | 'cancelled', now: Date): Promise<boolean> {
  const r = await db.query(
    `UPDATE party_invites SET state = $2, responded_at = CASE WHEN $2 IN ('accepted', 'declined') THEN $3::timestamptz ELSE NULL END
      WHERE id = $1 AND state = 'pending'`,
    [id, state, now],
  );
  return (r.rowCount ?? 0) > 0;
}

/** 수락한 사람의 다른 대기 초대를 취소한다 */
export async function cancelOthersOf(client: PoolClient, inviteeCharacterId: number, exceptId: number): Promise<{ uuid: string; inviter: number }[]> {
  const r = await client.query<{ uuid: string; inviter: string }>(
    `UPDATE party_invites SET state = 'cancelled' WHERE invitee_character_id = $1 AND state = 'pending' AND NOT silent AND id <> $2
      RETURNING uuid, inviter_character_id AS inviter`,
    [inviteeCharacterId, exceptId],
  );
  return r.rows.map((x) => ({ uuid: x.uuid, inviter: Number(x.inviter) }));
}

export interface IncomingView {
  uuid: string;
  expires_at: Date;
  inviter_uuid: string;
  inviter_name: string;
  inviter_class: 'warrior' | 'mage';
  inviter_level: number;
  dungeon_id: string;
  difficulty: number;
  max_members: number;
  min_power: number;
  message: string;
  members: number;
}

/** 받은 대기 초대(만료 전, 모집 중인 파티) */
export async function incomingFor(db: Queryable, inviteeCharacterId: number, now: Date): Promise<IncomingView[]> {
  const r = await db.query<Omit<IncomingView, 'members'> & { members: string }>(
    `SELECT pi.uuid, pi.expires_at, ic.uuid AS inviter_uuid, ic.name AS inviter_name, ic.class AS inviter_class, ic.level AS inviter_level,
            p.dungeon_id, p.difficulty, p.max_members, p.min_power, p.message,
            (SELECT count(*) FROM party_members m WHERE m.party_id = p.id AND m.left_at IS NULL) AS members
       FROM party_invites pi
       JOIN parties p ON p.id = pi.party_id
       JOIN characters ic ON ic.id = pi.inviter_character_id
      WHERE pi.invitee_character_id = $1 AND pi.state = 'pending' AND NOT pi.silent AND pi.expires_at > $2 AND p.state = 'forming'
      ORDER BY pi.created_at`,
    [inviteeCharacterId, now],
  );
  return r.rows.map((x) => ({ ...x, members: Number(x.members) }));
}
