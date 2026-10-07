// 파티 판 SQL: party_runs, party_run_members, party_run_host_reports
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import { RUN_COLS, toPartyRun, type PartyRunRow } from '../party/partyTx';
import { notifyPartyChanged, notifyRunChanged } from '../chat/partyNotify';

export type MemberState = 'invited' | 'joined' | 'playing' | 'disconnected' | 'done' | 'left' | 'no_show' | 'dropped';
export const ACTIVE_STATES: MemberState[] = ['invited', 'joined', 'playing', 'disconnected'];

export interface RunMemberRow {
  character_id: number;
  account_id: number;
  character_uuid: string;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
  slot: number;
  state: MemberState;
  dungeon_run_id: number | null;
  joined_at: Date | null;
  last_seen_at: Date | null;
  disconnected_at: Date | null;
}
interface RawMember extends Omit<RunMemberRow, 'character_id' | 'account_id' | 'dungeon_run_id'> {
  character_id: string;
  account_id: string;
  dungeon_run_id: string | null;
}
const toMember = (r: RawMember): RunMemberRow => ({
  ...r,
  character_id: Number(r.character_id),
  account_id: Number(r.account_id),
  dungeon_run_id: r.dungeon_run_id === null ? null : Number(r.dungeon_run_id),
});
const MEMBER_SQL = `SELECT m.character_id, m.account_id, c.uuid AS character_uuid, c.name, c.class, c.level, m.slot, m.state,
  m.dungeon_run_id, m.joined_at, m.last_seen_at, m.disconnected_at
  FROM party_run_members m JOIN characters c ON c.id = m.character_id`;

export async function runMembers(db: Queryable, runId: number): Promise<RunMemberRow[]> {
  const r = await db.query<RawMember>(`${MEMBER_SQL} WHERE m.party_run_id = $1 ORDER BY m.slot`, [runId]);
  return r.rows.map(toMember);
}

export async function getRunByUuid(db: Queryable, uuid: string): Promise<PartyRunRow | null> {
  const r = await db.query<Parameters<typeof toPartyRun>[0]>(`SELECT ${RUN_COLS} FROM party_runs WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? toPartyRun(r.rows[0]) : null;
}

export async function getRunById(db: Queryable, id: number): Promise<PartyRunRow | null> {
  const r = await db.query<Parameters<typeof toPartyRun>[0]>(`SELECT ${RUN_COLS} FROM party_runs WHERE id = $1`, [id]);
  return r.rows[0] ? toPartyRun(r.rows[0]) : null;
}

/** 파티의 진행 중인 판(모으는 중 또는 진행 중) */
export async function activeRunOfParty(db: Queryable, partyId: number): Promise<PartyRunRow | null> {
  const r = await db.query<Parameters<typeof toPartyRun>[0]>(
    `SELECT ${RUN_COLS} FROM party_runs WHERE party_id = $1 AND state IN ('gathering', 'playing')`,
    [partyId],
  );
  return r.rows[0] ? toPartyRun(r.rows[0]) : null;
}

export async function insertRun(
  client: PoolClient,
  p: {
    partyId: number;
    dungeonId: string;
    difficulty: number;
    hostId: number;
    humans: number;
    aiCount: number;
    runKey: Buffer;
    deadline: Date;
    now: Date;
    transport: 'relay' | 'steam' | 'dev';
    transportOrder: ('relay' | 'steam' | 'dev')[];
  },
): Promise<PartyRunRow> {
  const r = await client.query<Parameters<typeof toPartyRun>[0]>(
    `INSERT INTO party_runs (party_id, dungeon_id, difficulty, host_character_id, humans, ai_count, run_key, gather_deadline_at, created_at,
                             transport, transport_order)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11::text[]) RETURNING ${RUN_COLS}`,
    [p.partyId, p.dungeonId, p.difficulty, p.hostId, p.humans, p.aiCount, p.runKey, p.deadline, p.now, p.transport, p.transportOrder],
  );
  return toPartyRun(r.rows[0] as Parameters<typeof toPartyRun>[0]);
}

export async function insertRunMember(
  client: PoolClient,
  runId: number,
  characterId: number,
  accountId: number,
  slot: number,
  state: MemberState,
  now: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO party_run_members (party_run_id, character_id, account_id, slot, state, joined_at, last_seen_at)
     VALUES ($1, $2, $3, $4, $5, $6, $6)`,
    [runId, characterId, accountId, slot, state, state === 'joined' ? now : null],
  );
  notifyRunChanged(client, runId);
}

export async function setMemberState(
  client: Queryable,
  runId: number,
  characterId: number,
  state: MemberState,
  now: Date,
  extra: { leftReason?: 'left' | 'rejoin_timeout'; dungeonRunId?: number } = {},
): Promise<void> {
  await client.query(
    `UPDATE party_run_members
        SET state = $3, left_reason = $5, dungeon_run_id = COALESCE($6, dungeon_run_id),
            joined_at = CASE WHEN $3 = 'joined' THEN $4 ELSE joined_at END,
            last_seen_at = CASE WHEN $3 IN ('joined', 'playing') THEN $4 ELSE last_seen_at END,
            disconnected_at = CASE WHEN $3 = 'disconnected' THEN $4 WHEN $3 = 'playing' THEN NULL ELSE disconnected_at END,
            finished_at = CASE WHEN $3 IN ('done', 'left') THEN $4 ELSE finished_at END
      WHERE party_run_id = $1 AND character_id = $2`,
    [runId, characterId, state, now, extra.leftReason ?? null, extra.dungeonRunId ?? null],
  );
  notifyRunChanged(client, runId);
}

export async function touchMember(client: Queryable, runId: number, characterId: number, now: Date): Promise<void> {
  await client.query('UPDATE party_run_members SET last_seen_at = $3 WHERE party_run_id = $1 AND character_id = $2', [
    runId,
    characterId,
    now,
  ]);
}

export async function updateRunBegin(
  client: PoolClient,
  runId: number,
  humans: number,
  aiCount: number,
  powerCap: number,
  now: Date,
): Promise<void> {
  await client.query(
    "UPDATE party_runs SET state = 'playing', humans = $2, ai_count = $3, power_cap = $4, begun_at = $5 WHERE id = $1",
    [runId, humans, aiCount, powerCap, now],
  );
  notifyRunChanged(client, runId);
}

export async function cancelRun(
  client: PoolClient,
  runId: number,
  reason: 'timeout' | 'host_cancel' | 'nobody_joined' | 'ineligible',
  now: Date,
): Promise<void> {
  await client.query(
    "UPDATE party_runs SET state = 'cancelled', cancel_reason = $2, ended_at = $3, run_key = NULL WHERE id = $1",
    [runId, reason, now],
  );
  await client.query(
    `UPDATE party_run_members SET state = 'done', finished_at = $2
      WHERE party_run_id = $1 AND state IN ('invited', 'joined', 'playing', 'disconnected')`,
    [runId, now],
  );
  notifyRunChanged(client, runId);
}

export async function setPartyState(client: PoolClient, partyId: number, state: 'forming' | 'starting' | 'in_run'): Promise<void> {
  await client.query('UPDATE parties SET state = $2, version = version + 1 WHERE id = $1 AND state <> $3', [
    partyId,
    state,
    'closed',
  ]);
  notifyPartyChanged(client, partyId);
}

/** 모든 멤버 행이 playing을 벗어났으면 판을 닫고 파티를 forming으로 되돌린다(party_runs, parties는 호출 쪽이 이미 잠갔다) */
export async function endRunIfDone(client: PoolClient, run: PartyRunRow, now: Date): Promise<boolean> {
  if (run.state !== 'playing') return false;
  const r = await client.query(
    `UPDATE party_runs SET state = 'ended', ended_at = $2, run_key = NULL
      WHERE id = $1 AND state = 'playing'
        AND NOT EXISTS (SELECT 1 FROM dungeon_runs WHERE party_run_id = $1 AND state = 'playing')`,
    [run.id, now],
  );
  if ((r.rowCount ?? 0) === 0) return false;
  await client.query(
    "UPDATE parties SET state = 'forming', version = version + 1, last_active_at = $2 WHERE id = $1 AND state = 'in_run'",
    [run.party_id, now],
  );
  notifyRunChanged(client, run.id);
  notifyPartyChanged(client, run.party_id, now);
  return true;
}

export async function setHost(client: PoolClient, runId: number, hostId: number): Promise<void> {
  await client.query('UPDATE party_runs SET host_character_id = $2, host_epoch = host_epoch + 1 WHERE id = $1', [runId, hostId]);
  notifyRunChanged(client, runId);
}

export async function setFirstReport(client: Queryable, runId: number, at: Date): Promise<void> {
  await client.query('UPDATE party_runs SET first_report_at = COALESCE(first_report_at, $2) WHERE id = $1', [runId, at]);
}

// ---------- 방장 보고 ----------

export interface HostReportRow {
  host_epoch: number;
  host_character_id: number;
  outcome: 'cleared' | 'failed';
  elapsed_ms: number;
  rooms: { room_index: number; kills: { monster_id: string; count: number }[] }[];
  members: { character_id: string; hits_taken: number; max_combo: number; revives_used: number; damage_dealt: number; hits_landed?: number; card_mismatch?: boolean }[];
  ai: { slot: number; damage_dealt: number }[];
  created_at: Date;
}

export async function insertHostReport(
  client: PoolClient,
  runId: number,
  epoch: number,
  hostId: number,
  requestId: string,
  r: Pick<HostReportRow, 'outcome' | 'elapsed_ms' | 'rooms' | 'members' | 'ai'>,
  now: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO party_run_host_reports (party_run_id, host_epoch, host_character_id, request_id, outcome, elapsed_ms, rooms, members, ai, created_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, $8::jsonb, $9::jsonb, $10)`,
    [runId, epoch, hostId, requestId, r.outcome, r.elapsed_ms, JSON.stringify(r.rooms), JSON.stringify(r.members), JSON.stringify(r.ai), now],
  );
}

export async function hostReports(db: Queryable, runId: number): Promise<HostReportRow[]> {
  const r = await db.query<Omit<HostReportRow, 'host_character_id'> & { host_character_id: string }>(
    `SELECT host_epoch, host_character_id, outcome, elapsed_ms, rooms, members, ai, created_at
       FROM party_run_host_reports WHERE party_run_id = $1 ORDER BY host_epoch DESC`,
    [runId],
  );
  return r.rows.map((x) => ({ ...x, host_character_id: Number(x.host_character_id) }));
}

/** Steam ID(SteamID64) 조회. 계정당 하나 */
export async function steamIdsOf(db: Queryable, accountIds: number[]): Promise<Map<number, string>> {
  const r = await db.query<{ account_id: string; subject: string }>(
    "SELECT account_id, subject FROM auth_identities WHERE provider = 'steam' AND account_id = ANY($1::bigint[])",
    [accountIds],
  );
  return new Map(r.rows.map((x) => [Number(x.account_id), x.subject] as const));
}
