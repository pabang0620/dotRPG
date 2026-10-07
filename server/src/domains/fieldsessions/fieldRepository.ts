// 필드 세션 SQL: field_sessions, field_session_members. 쓰기는 Service가 세션 행 잠금 아래에서만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export type FieldMemberState = 'joined' | 'playing' | 'disconnected' | 'left';
export type FieldLeftReason = 'left' | 'map_move' | 'party_left' | 'kicked' | 'party_closed' | 'rejoin_timeout' | 'stale' | 'dungeon_start' | 'replaced';
export type FieldEndReason = 'empty' | 'party_closed' | 'stale';
export type TransportName = 'relay' | 'steam' | 'dev';

export interface SessionRow {
  id: number;
  uuid: string;
  party_id: number;
  map_id: string;
  state: 'active' | 'ended';
  host_character_id: number | null;
  host_epoch: number;
  host_since: Date | null;
  session_key: Buffer | null;
  transport: TransportName;
  transport_epoch: number;
  transport_switches: number;
  transport_order: TransportName[];
  version: number;
  last_observe_at: Date | null;
  last_active_at: Date;
  created_at: Date;
}
interface RawSession extends Omit<SessionRow, 'id' | 'party_id' | 'host_character_id'> {
  id: string;
  party_id: string;
  host_character_id: string | null;
}
const SESSION_COLS = `id, uuid, party_id, map_id, state, host_character_id, host_epoch, host_since, session_key, transport,
  transport_epoch, transport_switches, transport_order, version, last_observe_at, last_active_at, created_at`;
const toSession = (r: RawSession): SessionRow => ({
  ...r,
  id: Number(r.id),
  party_id: Number(r.party_id),
  host_character_id: r.host_character_id === null ? null : Number(r.host_character_id),
});

export interface FMemberRow {
  session_id: number;
  character_id: number;
  account_id: number;
  character_uuid: string;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
  seat: number;
  state: FieldMemberState;
  joined_at: Date;
  last_seen_at: Date | null;
  disconnected_at: Date | null;
  attack_cap: number;
  kills_accepted: number;
  kills_credited: number;
}
interface RawMember extends Omit<FMemberRow, 'session_id' | 'character_id' | 'account_id' | 'attack_cap'> {
  session_id: string;
  character_id: string;
  account_id: string;
  attack_cap: string;
}
const MEMBER_SQL = `SELECT m.session_id, m.character_id, m.account_id, c.uuid AS character_uuid, c.name, c.class, c.level, m.seat, m.state,
  m.joined_at, m.last_seen_at, m.disconnected_at, m.attack_cap, m.kills_accepted, m.kills_credited
  FROM field_session_members m JOIN characters c ON c.id = m.character_id`;
const toMember = (r: RawMember): FMemberRow => ({
  ...r,
  session_id: Number(r.session_id),
  character_id: Number(r.character_id),
  account_id: Number(r.account_id),
  attack_cap: Number(r.attack_cap),
});

export async function getByUuid(db: Queryable, uuid: string): Promise<SessionRow | null> {
  const r = await db.query<RawSession>(`SELECT ${SESSION_COLS} FROM field_sessions WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? toSession(r.rows[0]) : null;
}

export async function getById(db: Queryable, id: number): Promise<SessionRow | null> {
  const r = await db.query<RawSession>(`SELECT ${SESSION_COLS} FROM field_sessions WHERE id = $1`, [id]);
  return r.rows[0] ? toSession(r.rows[0]) : null;
}

export async function lockByUuid(client: PoolClient, uuid: string): Promise<SessionRow | null> {
  const r = await client.query<RawSession>(`SELECT ${SESSION_COLS} FROM field_sessions WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? toSession(r.rows[0]) : null;
}

export async function lockById(client: PoolClient, id: number): Promise<SessionRow | null> {
  const r = await client.query<RawSession>(`SELECT ${SESSION_COLS} FROM field_sessions WHERE id = $1 FOR UPDATE`, [id]);
  return r.rows[0] ? toSession(r.rows[0]) : null;
}

/** id 오름차순으로 한 번에 잠근다(세션 여러 개를 잠글 때의 유일한 순서) */
export async function lockMany(client: PoolClient, ids: number[]): Promise<SessionRow[]> {
  if (ids.length === 0) return [];
  const r = await client.query<RawSession>(`SELECT ${SESSION_COLS} FROM field_sessions WHERE id = ANY($1::bigint[]) ORDER BY id FOR UPDATE`, [ids]);
  return r.rows.map(toSession);
}

export async function activeSessionsOfParty(db: Queryable, partyId: number): Promise<number[]> {
  const r = await db.query<{ id: string }>("SELECT id FROM field_sessions WHERE party_id = $1 AND state = 'active' ORDER BY id", [partyId]);
  return r.rows.map((x) => Number(x.id));
}

/** 내가 활성 멤버(state <> 'left')인 세션 id */
export async function activeSessionIdOf(db: Queryable, characterId: number): Promise<number | null> {
  const r = await db.query<{ session_id: string }>(
    `SELECT m.session_id FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id
      WHERE m.character_id = $1 AND m.state <> 'left' AND s.state = 'active'`,
    [characterId],
  );
  return r.rows[0] ? Number(r.rows[0].session_id) : null;
}

export async function insertSession(
  client: PoolClient,
  p: { partyId: number; mapId: string; hostId: number; key: Buffer; transport: TransportName; order: TransportName[]; now: Date },
): Promise<SessionRow> {
  const r = await client.query<RawSession>(
    `INSERT INTO field_sessions (party_id, map_id, host_character_id, host_since, session_key, transport, transport_order, version, last_active_at, created_at)
     VALUES ($1, $2, $3, $7, $4, $5, $6::text[], 1, $7, $7) RETURNING ${SESSION_COLS}`,
    [p.partyId, p.mapId, p.hostId, p.key, p.transport, p.order, p.now],
  );
  return toSession(r.rows[0] as RawSession);
}

/** 활성 멤버(state <> 'left')를 좌석 순으로 */
export async function activeMembers(db: Queryable, sessionId: number): Promise<FMemberRow[]> {
  const r = await db.query<RawMember>(`${MEMBER_SQL} WHERE m.session_id = $1 AND m.state <> 'left' ORDER BY m.seat`, [sessionId]);
  return r.rows.map(toMember);
}

/** left 포함, 한 캐릭터의 행 */
export async function memberRow(db: Queryable, sessionId: number, characterId: number): Promise<FMemberRow | null> {
  const r = await db.query<RawMember>(`${MEMBER_SQL} WHERE m.session_id = $1 AND m.character_id = $2`, [sessionId, characterId]);
  return r.rows[0] ? toMember(r.rows[0]) : null;
}

export async function insertMember(client: PoolClient, sessionId: number, characterId: number, accountId: number, seat: number, cap: number, now: Date, debt = 0): Promise<void> {
  await client.query(
    `INSERT INTO field_session_members (session_id, character_id, account_id, seat, state, joined_at, last_seen_at, attack_cap, kills_accepted)
     VALUES ($1, $2, $3, $4, 'joined', $5, $5, $6, $7)`,
    [sessionId, characterId, accountId, seat, now, cap, debt],
  );
}

/**
 * 같은 (파티, 맵, 캐릭터)의 직전 세션이 minutes 분 안에 끝났다면 그 세션의 남은 기여 부채(accepted - credited)를 돌려준다.
 * 세션을 닫았다 다시 열어 부채를 지우는 우회를 막는다
 */
export async function carriedDebt(db: Queryable, partyId: number, mapId: string, characterId: number, now: Date, minutes: number): Promise<number> {
  const r = await db.query<{ debt: number }>(
    `SELECT GREATEST(0, m.kills_accepted - m.kills_credited) AS debt
       FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id
      WHERE s.party_id = $1 AND s.map_id = $2 AND m.character_id = $3 AND s.state = 'ended' AND s.ended_at > $4
      ORDER BY s.ended_at DESC LIMIT 1`,
    [partyId, mapId, characterId, new Date(now.getTime() - minutes * 60_000)],
  );
  return r.rows[0]?.debt ?? 0;
}

/** 이 캐릭터가 활성 멤버인 활성 세션 중 이 맵의 것(있으면 세션 없이 보고할 수 없다) */
export async function activeOnMap(db: Queryable, characterId: number, mapId: string): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id
      WHERE m.character_id = $1 AND m.state <> 'left' AND s.state = 'active' AND s.map_id = $2`,
    [characterId, mapId],
  );
  return r.rows.length > 0;
}

/** left 행을 되살린다(카운터 유지) */
export async function reviveMember(client: PoolClient, sessionId: number, characterId: number, seat: number, cap: number, now: Date): Promise<void> {
  await client.query(
    `UPDATE field_session_members SET state = 'joined', left_reason = NULL, left_at = NULL, seat = $3, joined_at = $5,
            last_seen_at = $5, disconnected_at = NULL, attack_cap = $4
      WHERE session_id = $1 AND character_id = $2`,
    [sessionId, characterId, seat, cap, now],
  );
}

export async function setMemberState(
  client: Queryable,
  sessionId: number,
  characterId: number,
  state: Exclude<FieldMemberState, 'left'>,
  now: Date,
): Promise<void> {
  await client.query(
    `UPDATE field_session_members
        SET state = $3::text, last_seen_at = CASE WHEN $3::text IN ('joined', 'playing') THEN $4::timestamptz ELSE last_seen_at END,
            disconnected_at = CASE WHEN $3::text = 'disconnected' THEN $4::timestamptz ELSE NULL END
      WHERE session_id = $1 AND character_id = $2 AND state <> 'left'`,
    [sessionId, characterId, state, now],
  );
}

export async function leaveMember(client: Queryable, sessionId: number, characterId: number, reason: FieldLeftReason, now: Date): Promise<void> {
  await client.query(
    `UPDATE field_session_members SET state = 'left', left_reason = $3, left_at = $4
      WHERE session_id = $1 AND character_id = $2 AND state <> 'left'`,
    [sessionId, characterId, reason, now],
  );
}

export async function touchMember(client: Queryable, sessionId: number, characterId: number, now: Date, cap?: number): Promise<void> {
  await client.query(
    `UPDATE field_session_members SET last_seen_at = $3, attack_cap = COALESCE($4, attack_cap)
      WHERE session_id = $1 AND character_id = $2 AND state <> 'left'`,
    [sessionId, characterId, now, cap ?? null],
  );
}

export async function setHost(client: Queryable, sessionId: number, hostId: number | null, now: Date): Promise<void> {
  await client.query('UPDATE field_sessions SET host_character_id = $2, host_epoch = host_epoch + 1, host_since = $3 WHERE id = $1', [sessionId, hostId, now]);
}

export async function bump(client: Queryable, sessionId: number, now: Date): Promise<void> {
  await client.query('UPDATE field_sessions SET version = version + 1, last_active_at = $2 WHERE id = $1', [sessionId, now]);
}

export async function touchSession(client: Queryable, sessionId: number, now: Date): Promise<void> {
  await client.query('UPDATE field_sessions SET last_active_at = $2 WHERE id = $1', [sessionId, now]);
}

export async function endSession(client: Queryable, sessionId: number, reason: FieldEndReason, now: Date): Promise<void> {
  await client.query(
    `UPDATE field_sessions SET state = 'ended', ended_at = $3, end_reason = $2, session_key = NULL, host_character_id = NULL,
            version = version + 1
      WHERE id = $1 AND state = 'active'`,
    [sessionId, reason, now],
  );
}

export async function setTransport(client: Queryable, sessionId: number, transport: TransportName, epoch: number, switches: number): Promise<void> {
  await client.query(
    'UPDATE field_sessions SET transport = $2, transport_epoch = $3, transport_switches = $4, version = version + 1 WHERE id = $1',
    [sessionId, transport, epoch, switches],
  );
}

export async function setObserved(client: Queryable, sessionId: number, now: Date): Promise<void> {
  await client.query('UPDATE field_sessions SET last_observe_at = $2, last_active_at = $2 WHERE id = $1', [sessionId, now]);
}

export async function addCredit(client: Queryable, sessionId: number, characterId: number, kills: number, surplus: number): Promise<void> {
  await client.query(
    `UPDATE field_session_members
        SET kills_credited = LEAST(kills_credited + $3, kills_accepted + $4)
      WHERE session_id = $1 AND character_id = $2 AND state <> 'left'`,
    [sessionId, characterId, kills, surplus],
  );
}

export async function addAccepted(client: Queryable, sessionId: number, characterId: number): Promise<void> {
  await client.query(
    `UPDATE field_session_members SET kills_accepted = kills_accepted + 1 WHERE session_id = $1 AND character_id = $2`,
    [sessionId, characterId],
  );
}

// ---------- 선출·파티 정보(읽기 전용, 다른 캐릭터를 잠그지 않는다) ----------

export async function partyOrder(db: Queryable, partyId: number): Promise<{ leaderId: number | null; order: Map<number, number> }> {
  const lead = await db.query<{ leader: string }>('SELECT leader_character_id AS leader FROM parties WHERE id = $1', [partyId]);
  const r = await db.query<{ character_id: string; joined_at: Date }>(
    'SELECT character_id, joined_at FROM party_members WHERE party_id = $1 AND left_at IS NULL',
    [partyId],
  );
  return {
    leaderId: lead.rows[0] ? Number(lead.rows[0].leader) : null,
    order: new Map(r.rows.map((x) => [Number(x.character_id), x.joined_at.getTime()] as const)),
  };
}

export async function wornKeysOf(db: Queryable, characterIds: number[]): Promise<Map<number, string[]>> {
  const out = new Map<number, string[]>();
  if (characterIds.length === 0) return out;
  const r = await db.query<{ character_id: string; item_key: string }>(
    "SELECT character_id, item_key FROM character_items WHERE location = 'worn' AND character_id = ANY($1::bigint[])",
    [characterIds],
  );
  for (const x of r.rows) {
    const id = Number(x.character_id);
    out.set(id, [...(out.get(id) ?? []), x.item_key]);
  }
  return out;
}

export async function isLeader(db: Queryable, partyId: number, characterId: number): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM parties WHERE id = $1 AND leader_character_id = $2', [partyId, characterId]);
  return r.rows.length > 0;
}

/** 방치 정리 후보(마지막 활동이 오래된 활성 세션) */
export async function staleSessionIds(db: Queryable, before: Date, limit: number): Promise<number[]> {
  const r = await db.query<{ id: string }>("SELECT id FROM field_sessions WHERE state = 'active' AND last_active_at < $1 ORDER BY id LIMIT $2", [before, limit]);
  return r.rows.map((x) => Number(x.id));
}

/** Steam ID(SteamID64) 조회. 계정당 하나 */
export async function steamIdsOf(db: Queryable, accountIds: number[]): Promise<Map<number, string>> {
  const r = await db.query<{ account_id: string; subject: string }>(
    "SELECT account_id, subject FROM auth_identities WHERE provider = 'steam' AND account_id = ANY($1::bigint[])",
    [accountIds],
  );
  return new Map(r.rows.map((x) => [Number(x.account_id), x.subject] as const));
}
