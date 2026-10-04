// 파티(로비·모집 글·신청) SQL. 쓰기는 Service가 잠금(partyTx) 아래에서만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import { notifyPartyChanged } from '../chat/partyNotify';
import { closeMembershipOf, closeSessionsOfParty } from '../fieldsessions/fieldCore';

export interface PartyRow {
  id: number;
  uuid: string;
  leader_character_id: number;
  dungeon_id: string;
  difficulty: number;
  max_members: number;
  min_power: number;
  message: string;
  source: 'board' | 'match';
  listed: boolean;
  listed_until: Date | null;
  state: 'forming' | 'starting' | 'in_run' | 'closed';
  version: number;
  start_by: Date | null;
  last_active_at: Date;
  created_at: Date;
}
interface RawParty extends Omit<PartyRow, 'id' | 'leader_character_id'> {
  id: string;
  leader_character_id: string;
}
const PARTY_COLS = `id, uuid, leader_character_id, dungeon_id, difficulty, max_members, min_power, message, source,
  listed, listed_until, state, version, start_by, last_active_at, created_at`;
const toParty = (r: RawParty): PartyRow => ({ ...r, id: Number(r.id), leader_character_id: Number(r.leader_character_id) });

export interface MemberRow {
  member_id: number;
  party_id: number;
  character_id: number;
  account_id: number;
  character_uuid: string;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
  ready: boolean;
  joined_at: Date;
}
interface RawMember extends Omit<MemberRow, 'member_id' | 'party_id' | 'character_id' | 'account_id'> {
  member_id: string;
  party_id: string;
  character_id: string;
  account_id: string;
}
const toMember = (r: RawMember): MemberRow => ({
  ...r,
  member_id: Number(r.member_id),
  party_id: Number(r.party_id),
  character_id: Number(r.character_id),
  account_id: Number(r.account_id),
});
const MEMBER_SQL = `SELECT m.id AS member_id, m.party_id, m.character_id, m.account_id, c.uuid AS character_uuid, c.name,
  c.class, c.level, m.ready, m.joined_at
  FROM party_members m JOIN characters c ON c.id = m.character_id`;

export async function findPartyIdOf(db: Queryable, characterId: number): Promise<number | null> {
  const r = await db.query<{ party_id: string }>(
    'SELECT party_id FROM party_members WHERE character_id = $1 AND left_at IS NULL',
    [characterId],
  );
  return r.rows[0] ? Number(r.rows[0].party_id) : null;
}

export async function getParty(db: Queryable, partyId: number): Promise<PartyRow | null> {
  const r = await db.query<RawParty>(`SELECT ${PARTY_COLS} FROM parties WHERE id = $1`, [partyId]);
  return r.rows[0] ? toParty(r.rows[0]) : null;
}

export async function lockParty(client: PoolClient, partyId: number): Promise<PartyRow | null> {
  const r = await client.query<RawParty>(`SELECT ${PARTY_COLS} FROM parties WHERE id = $1 FOR UPDATE`, [partyId]);
  return r.rows[0] ? toParty(r.rows[0]) : null;
}

export async function getPartyByUuid(db: Queryable, uuid: string): Promise<PartyRow | null> {
  const r = await db.query<RawParty>(`SELECT ${PARTY_COLS} FROM parties WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? toParty(r.rows[0]) : null;
}

export async function activeMembers(db: Queryable, partyId: number): Promise<MemberRow[]> {
  const r = await db.query<RawMember>(`${MEMBER_SQL} WHERE m.party_id = $1 AND m.left_at IS NULL ORDER BY m.joined_at, m.id`, [
    partyId,
  ]);
  return r.rows.map(toMember);
}

export async function insertParty(
  client: PoolClient,
  p: {
    leaderId: number;
    dungeonId: string;
    difficulty: number;
    maxMembers: number;
    minPower: number;
    message: string;
    source: 'board' | 'match';
    listed: boolean;
    listedUntil: Date | null;
    startBy: Date | null;
    now: Date;
  },
): Promise<PartyRow> {
  const r = await client.query<RawParty>(
    `INSERT INTO parties (leader_character_id, dungeon_id, difficulty, max_members, min_power, message, source,
                          listed, listed_until, start_by, last_active_at, created_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $11) RETURNING ${PARTY_COLS}`,
    [p.leaderId, p.dungeonId, p.difficulty, p.maxMembers, p.minPower, p.message, p.source, p.listed, p.listedUntil, p.startBy, p.now],
  );
  return toParty(r.rows[0] as RawParty);
}

export async function insertMember(
  client: PoolClient,
  partyId: number,
  characterId: number,
  accountId: number,
  ready: boolean,
  now: Date,
): Promise<void> {
  await client.query(
    'INSERT INTO party_members (party_id, character_id, account_id, ready, joined_at) VALUES ($1, $2, $3, $4, $5)',
    [partyId, characterId, accountId, ready, now],
  );
}

/** 구성·상태가 바뀔 때마다 version +1 (같은 트랜잭션) */
export async function bump(client: PoolClient, partyId: number, now: Date): Promise<void> {
  await client.query('UPDATE parties SET version = version + 1, last_active_at = $2 WHERE id = $1', [partyId, now]);
  notifyPartyChanged(client, partyId, now);
}

export async function setReady(client: PoolClient, partyId: number, characterId: number, ready: boolean): Promise<void> {
  await client.query('UPDATE party_members SET ready = $3 WHERE party_id = $1 AND character_id = $2 AND left_at IS NULL', [
    partyId,
    characterId,
    ready,
  ]);
}

/** 구성이 바뀌면 방장 외 멤버의 준비를 푼다 */
export async function resetReady(client: PoolClient, partyId: number, leaderId: number): Promise<void> {
  await client.query(
    'UPDATE party_members SET ready = (character_id = $2) WHERE party_id = $1 AND left_at IS NULL',
    [partyId, leaderId],
  );
}

export async function leaveMember(
  client: PoolClient,
  partyId: number,
  characterId: number,
  reason: 'left' | 'kicked' | 'disbanded' | 'idle_timeout' | 'start_timeout' | 'matched_other',
  now: Date,
): Promise<void> {
  await client.query(
    'UPDATE party_members SET left_at = $3, left_reason = $4 WHERE party_id = $1 AND character_id = $2 AND left_at IS NULL',
    [partyId, characterId, now, reason],
  );
  // 8단계: 파티를 나가거나 강퇴되면 같은 트랜잭션에서 필드 세션 멤버십도 닫는다(호스트면 인계)
  await closeMembershipOf(client, characterId, reason === 'kicked' ? 'kicked' : 'party_left', now);
}

export async function setLeader(client: PoolClient, partyId: number, leaderId: number): Promise<void> {
  await client.query('UPDATE parties SET leader_character_id = $2 WHERE id = $1', [partyId, leaderId]);
}

/** 해산: 남은 멤버를 모두 내보내고 파티를 닫는다. 대기 신청은 거절 처리 */
export async function closeParty(
  client: PoolClient,
  partyId: number,
  reason: 'disbanded' | 'idle_timeout' | 'start_timeout',
  now: Date,
): Promise<void> {
  const memberReason = reason === 'disbanded' ? 'disbanded' : reason;
  notifyPartyChanged(client, partyId, now);
  await closeSessionsOfParty(client, partyId, now);
  await client.query(
    'UPDATE party_members SET left_at = $2, left_reason = $3 WHERE party_id = $1 AND left_at IS NULL',
    [partyId, now, memberReason],
  );
  await client.query(
    "UPDATE party_applications SET state = 'rejected', responded_at = $2 WHERE party_id = $1 AND state = 'pending'",
    [partyId, now],
  );
  await client.query(
    "UPDATE parties SET state = 'closed', closed_at = $2, close_reason = $3, listed = false, version = version + 1 WHERE id = $1",
    [partyId, now, reason],
  );
}

export async function patchParty(
  client: PoolClient,
  partyId: number,
  p: { listed?: boolean; listedUntil?: Date | null; message?: string; minPower?: number; maxMembers?: number },
): Promise<void> {
  await client.query(
    `UPDATE parties SET listed = COALESCE($2, listed),
                        listed_until = CASE WHEN $3::boolean THEN $4 ELSE listed_until END,
                        message = COALESCE($5, message), min_power = COALESCE($6, min_power),
                        max_members = COALESCE($7, max_members)
      WHERE id = $1`,
    [partyId, p.listed ?? null, p.listedUntil !== undefined, p.listedUntil ?? null, p.message ?? null, p.minPower ?? null, p.maxMembers ?? null],
  );
}

// ---------- 게시판 ----------

export interface PostRow {
  uuid: string;
  dungeon_id: string;
  difficulty: number;
  max_members: number;
  min_power: number;
  message: string;
  listed_until: Date;
  created_at: Date;
  leader_character_id: number;
  members: number;
  leader_name: string;
  leader_class: 'warrior' | 'mage';
  leader_level: number;
  applied: boolean;
}

export async function listBoard(
  db: Queryable,
  meCharacterId: number,
  now: Date,
  dungeonId: string | undefined,
  difficulty: number | undefined,
): Promise<PostRow[]> {
  const r = await db.query<Omit<PostRow, 'leader_character_id' | 'members'> & { leader_character_id: string; members: string }>(
    `SELECT p.uuid, p.dungeon_id, p.difficulty, p.max_members, p.min_power, p.message, p.listed_until, p.created_at,
            p.leader_character_id, c.name AS leader_name, c.class AS leader_class, c.level AS leader_level,
            (SELECT count(*) FROM party_members m WHERE m.party_id = p.id AND m.left_at IS NULL) AS members,
            EXISTS (SELECT 1 FROM party_applications a WHERE a.party_id = p.id AND a.character_id = $2
                     AND a.state = 'pending' AND a.expires_at > $1) AS applied
       FROM parties p JOIN characters c ON c.id = p.leader_character_id
      WHERE p.listed AND p.state = 'forming' AND p.listed_until > $1
        AND ($3::text IS NULL OR p.dungeon_id = $3) AND ($4::int IS NULL OR p.difficulty = $4)
      ORDER BY p.created_at DESC`,
    [now, meCharacterId, dungeonId ?? null, difficulty ?? null],
  );
  return r.rows.map((x) => ({ ...x, leader_character_id: Number(x.leader_character_id), members: Number(x.members) }));
}

// ---------- 신청 ----------

export interface ApplicationRow {
  id: number;
  uuid: string;
  party_id: number;
  party_uuid: string;
  character_id: number;
  account_id: number;
  state: 'pending' | 'accepted' | 'rejected' | 'expired' | 'cancelled';
  power_estimate: number;
  expires_at: Date;
}
interface RawApp extends Omit<ApplicationRow, 'id' | 'party_id' | 'character_id' | 'account_id'> {
  id: string;
  party_id: string;
  character_id: string;
  account_id: string;
}
const APP_SQL = `SELECT a.id, a.uuid, a.party_id, p.uuid AS party_uuid, a.character_id, a.account_id, a.state, a.power_estimate, a.expires_at
  FROM party_applications a JOIN parties p ON p.id = a.party_id`;
const toApp = (r: RawApp): ApplicationRow => ({
  ...r,
  id: Number(r.id),
  party_id: Number(r.party_id),
  character_id: Number(r.character_id),
  account_id: Number(r.account_id),
});

export async function findApplication(db: Queryable, uuid: string): Promise<ApplicationRow | null> {
  const r = await db.query<RawApp>(`${APP_SQL} WHERE a.uuid = $1`, [uuid]);
  return r.rows[0] ? toApp(r.rows[0]) : null;
}

export async function insertApplication(
  client: PoolClient,
  partyId: number,
  characterId: number,
  accountId: number,
  power: number,
  now: Date,
  expiresAt: Date,
): Promise<ApplicationRow> {
  const r = await client.query<RawApp>(
    `WITH ins AS (
       INSERT INTO party_applications (party_id, character_id, account_id, power_estimate, created_at, expires_at)
       VALUES ($1, $2, $3, $4, $5, $6) RETURNING *)
     SELECT a.id, a.uuid, a.party_id, p.uuid AS party_uuid, a.character_id, a.account_id, a.state, a.power_estimate, a.expires_at
       FROM ins a JOIN parties p ON p.id = a.party_id`,
    [partyId, characterId, accountId, power, now, expiresAt],
  );
  return toApp(r.rows[0] as RawApp);
}

export async function pendingApplication(db: Queryable, partyId: number, characterId: number): Promise<ApplicationRow | null> {
  const r = await db.query<RawApp>(`${APP_SQL} WHERE a.party_id = $1 AND a.character_id = $2 AND a.state = 'pending'`, [
    partyId,
    characterId,
  ]);
  return r.rows[0] ? toApp(r.rows[0]) : null;
}

export async function setApplicationState(
  client: Queryable,
  id: number,
  state: 'accepted' | 'rejected' | 'expired' | 'cancelled',
  now: Date,
): Promise<void> {
  await client.query('UPDATE party_applications SET state = $2, responded_at = $3 WHERE id = $1', [id, state, now]);
}

/** 지연 만료: 내 신청과 내 파티의 대기 신청 */
export async function expireApplications(client: Queryable, characterId: number, partyId: number | null, now: Date): Promise<void> {
  await client.query(
    `UPDATE party_applications SET state = 'expired', responded_at = $3
      WHERE state = 'pending' AND expires_at <= $3 AND (character_id = $1 OR party_id = $2)`,
    [characterId, partyId, now],
  );
}

export async function cancelOtherApplications(client: PoolClient, characterId: number, exceptId: number, now: Date): Promise<void> {
  await client.query(
    "UPDATE party_applications SET state = 'cancelled', responded_at = $3 WHERE character_id = $1 AND state = 'pending' AND id <> $2",
    [characterId, exceptId, now],
  );
}

export async function countPending(db: Queryable, characterId: number, now: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    "SELECT count(*) AS n FROM party_applications WHERE character_id = $1 AND state = 'pending' AND expires_at > $2",
    [characterId, now],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export interface PendingApplicantRow {
  uuid: string;
  expires_at: Date;
  power_estimate: number;
  char_uuid: string;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
}
export async function pendingForParty(db: Queryable, partyId: number, now: Date): Promise<PendingApplicantRow[]> {
  const r = await db.query<PendingApplicantRow>(
    `SELECT a.uuid, a.expires_at, a.power_estimate, c.uuid AS char_uuid, c.name, c.class, c.level
       FROM party_applications a JOIN characters c ON c.id = a.character_id
      WHERE a.party_id = $1 AND a.state = 'pending' AND a.expires_at > $2 ORDER BY a.created_at`,
    [partyId, now],
  );
  return r.rows;
}

export async function recentApplicationsOf(
  db: Queryable,
  characterId: number,
): Promise<{ uuid: string; party_uuid: string; state: string; expires_at: Date }[]> {
  const r = await db.query<{ uuid: string; party_uuid: string; state: string; expires_at: Date }>(
    `SELECT a.uuid, p.uuid AS party_uuid, a.state, a.expires_at FROM party_applications a JOIN parties p ON p.id = a.party_id
      WHERE a.character_id = $1 ORDER BY a.created_at DESC, a.id DESC LIMIT 3`,
    [characterId],
  );
  return r.rows;
}

/** 최근 2분 안에 나간 기록(해산·강퇴 알림) */
export async function recentNotice(
  db: Queryable,
  characterId: number,
  since: Date,
): Promise<{ left_reason: string; left_at: Date } | null> {
  const r = await db.query<{ left_reason: string; left_at: Date }>(
    `SELECT left_reason, left_at FROM party_members
      WHERE character_id = $1 AND left_at IS NOT NULL AND left_at > $2 AND left_reason IN ('kicked', 'disbanded', 'idle_timeout', 'start_timeout')
      ORDER BY left_at DESC LIMIT 1`,
    [characterId, since],
  );
  return r.rows[0] ?? null;
}

/** 방치 파티(마지막 활동 후 N분, 판 없음) */
export async function idleOrTimedOut(db: Queryable, partyId: number, now: Date, idleMinutes: number): Promise<'idle_timeout' | 'start_timeout' | null> {
  const r = await db.query<{ idle: boolean; late: boolean }>(
    `SELECT (state = 'forming' AND last_active_at < $2::timestamptz - ($3::int * interval '1 minute')) AS idle,
            (state = 'forming' AND start_by IS NOT NULL AND start_by <= $2) AS late
       FROM parties WHERE id = $1`,
    [partyId, now, idleMinutes],
  );
  const row = r.rows[0];
  if (!row) return null;
  if (row.late) return 'start_timeout';
  return row.idle ? 'idle_timeout' : null;
}
