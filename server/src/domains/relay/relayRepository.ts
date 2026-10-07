// 중계가 DB에서 읽는 방 상태(멤버십의 권한은 DB다)와 방 사용 요약. SQL만.
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import type { RoomKind } from './relayTicket';
import { getNow } from '../../utils/clock';

export type TransportName = 'relay' | 'steam' | 'dev';

export interface SnapshotMember {
  seat: number;
  characterId: number;
  characterUuid: string;
  accountId: number;
  state: string;
  /** 입장 자격이 있는 멤버(끊긴 지 60초가 넘은 멤버는 아니다) */
  active: boolean;
}

export interface RoomSnapshot {
  kind: RoomKind;
  uuid: string;
  closed: boolean;
  transport: TransportName;
  transportEpoch: number;
  hostSeat: number | null;
  hostEpoch: number;
  members: SnapshotMember[];
}

interface RawMember {
  seat: number;
  character_id: string;
  character_uuid: string;
  account_id: string;
  state: string;
  disconnected_at: Date | null;
}

const rejoinOk = (state: string, at: Date | null, nowMs: number): boolean =>
  state !== 'disconnected' || at === null || nowMs - at.getTime() <= getConfig().policy.partyRejoinSeconds * 1000;

export async function snapshotRun(db: Queryable, uuid: string, nowMs = getNow().getTime()): Promise<RoomSnapshot | null> {
  const r = await db.query<{ id: string; state: string; host_character_id: string; host_epoch: number; transport: TransportName; transport_epoch: number }>(
    'SELECT id, state, host_character_id, host_epoch, transport, transport_epoch FROM party_runs WHERE uuid = $1',
    [uuid],
  );
  const run = r.rows[0];
  if (!run) return null;
  const m = await db.query<RawMember>(
    `SELECT m.slot AS seat, m.character_id, c.uuid AS character_uuid, m.account_id, m.state, m.disconnected_at
       FROM party_run_members m JOIN characters c ON c.id = m.character_id WHERE m.party_run_id = $1 ORDER BY m.slot`,
    [run.id],
  );
  const members: SnapshotMember[] = m.rows.map((x) => ({
    seat: x.seat,
    characterId: Number(x.character_id),
    characterUuid: x.character_uuid,
    accountId: Number(x.account_id),
    state: x.state,
    active: ['invited', 'joined', 'playing', 'disconnected'].includes(x.state) && rejoinOk(x.state, x.disconnected_at, nowMs),
  }));
  const host = members.find((x) => x.characterId === Number(run.host_character_id));
  return {
    kind: 'run',
    uuid,
    closed: run.state === 'ended' || run.state === 'cancelled',
    transport: run.transport,
    transportEpoch: run.transport_epoch,
    hostSeat: host ? host.seat : null,
    hostEpoch: run.host_epoch,
    members,
  };
}

export async function snapshotField(db: Queryable, uuid: string, nowMs = getNow().getTime()): Promise<RoomSnapshot | null> {
  const r = await db.query<{ id: string; state: string; host_character_id: string | null; host_epoch: number; transport: TransportName; transport_epoch: number }>(
    'SELECT id, state, host_character_id, host_epoch, transport, transport_epoch FROM field_sessions WHERE uuid = $1',
    [uuid],
  );
  const s = r.rows[0];
  if (!s) return null;
  const m = await db.query<RawMember>(
    `SELECT m.seat, m.character_id, c.uuid AS character_uuid, m.account_id, m.state, m.disconnected_at
       FROM field_session_members m JOIN characters c ON c.id = m.character_id WHERE m.session_id = $1 ORDER BY (m.state = 'left'), m.seat`,
    [s.id],
  );
  const members: SnapshotMember[] = m.rows.map((x) => ({
    seat: x.seat,
    characterId: Number(x.character_id),
    characterUuid: x.character_uuid,
    accountId: Number(x.account_id),
    state: x.state,
    active: x.state !== 'left' && rejoinOk(x.state, x.disconnected_at, nowMs),
  }));
  const host = s.host_character_id === null ? undefined : members.find((x) => x.characterId === Number(s.host_character_id));
  return {
    kind: 'field',
    uuid,
    closed: s.state === 'ended',
    transport: s.transport,
    transportEpoch: s.transport_epoch,
    hostSeat: host ? host.seat : null,
    hostEpoch: s.host_epoch,
    members,
  };
}

export const snapshotOf = (db: Queryable, kind: RoomKind, uuid: string): Promise<RoomSnapshot | null> =>
  kind === 'run' ? snapshotRun(db, uuid) : snapshotField(db, uuid);

/** 이 계정(uuid)의 정지 상태와 내부 id */
export async function accountByUuid(db: Queryable, uuid: string): Promise<{ id: number; banned_until: Date | null } | null> {
  const r = await db.query<{ id: string; banned_until: Date | null }>('SELECT id, banned_until FROM accounts WHERE uuid = $1', [uuid]);
  return r.rows[0] ? { id: Number(r.rows[0].id), banned_until: r.rows[0].banned_until } : null;
}

export async function characterOf(db: Queryable, accountId: number, uuid: string): Promise<{ id: number } | null> {
  const r = await db.query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL', [uuid, accountId]);
  return r.rows[0] ? { id: Number(r.rows[0].id) } : null;
}

export interface RoomStats {
  kind: RoomKind;
  ref: string;
  startedAt: Date;
  endedAt: Date;
  peakPeers: number;
  bytesIn: number;
  bytesOut: number;
  framesIn: number;
  framesOut: number;
  droppedUnreliable: number;
  reconnects: number;
  rttP50: number | null;
  rttP95: number | null;
  closeCodes: Record<string, number>;
}

export async function insertRoomStats(db: Queryable, s: RoomStats): Promise<void> {
  await db.query(
    `INSERT INTO relay_room_stats (room_kind, room_ref, started_at, ended_at, peak_peers, bytes_in, bytes_out, frames_in, frames_out,
                                   dropped_unreliable, reconnects, rtt_p50_ms, rtt_p95_ms, close_codes)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14::jsonb)`,
    [s.kind, s.ref, s.startedAt, s.endedAt, Math.min(4, Math.max(1, s.peakPeers)), s.bytesIn, s.bytesOut, s.framesIn, s.framesOut, s.droppedUnreliable, s.reconnects, s.rttP50, s.rttP95, JSON.stringify(s.closeCodes)],
  );
}
