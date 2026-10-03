import type { Queryable } from '../../db/pool';

export interface WindowRow {
  id: number;
  uuid: string;
  state: 'scheduled' | 'cancelled' | 'ended';
  notice: string;
  block_login_at: Date;
  starts_at: Date;
  ends_at: Date;
  created_at: Date;
  closed_at: Date | null;
}

interface Raw extends Omit<WindowRow, 'id'> {
  id: string;
}
const COLS = 'id, uuid, state, notice, block_login_at, starts_at, ends_at, created_at, closed_at';
const to = (r: Raw): WindowRow => ({ ...r, id: Number(r.id) });

export async function openWindow(db: Queryable): Promise<WindowRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM maintenance_windows WHERE state = 'scheduled'`);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function recent(db: Queryable, limit: number): Promise<WindowRow[]> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM maintenance_windows ORDER BY id DESC LIMIT $1`, [limit]);
  return r.rows.map(to);
}

export async function lockByUuid(db: Queryable, uuid: string): Promise<WindowRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM maintenance_windows WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function insert(db: Queryable, v: { notice: string; blockLoginAt: Date; startsAt: Date; endsAt: Date; adminId: number }): Promise<WindowRow> {
  const r = await db.query<Raw>(
    `INSERT INTO maintenance_windows (notice, block_login_at, starts_at, ends_at, created_by) VALUES ($1, $2, $3, $4, $5) RETURNING ${COLS}`,
    [v.notice, v.blockLoginAt, v.startsAt, v.endsAt, v.adminId],
  );
  return to(r.rows[0] as Raw);
}

export async function close(db: Queryable, id: number, state: 'cancelled' | 'ended', adminId: number, at: Date): Promise<void> {
  await db.query('UPDATE maintenance_windows SET state = $2, closed_at = $4, closed_by = $3 WHERE id = $1', [id, state, adminId, at]);
}

export async function extend(db: Queryable, id: number, endsAt: Date): Promise<void> {
  await db.query('UPDATE maintenance_windows SET ends_at = $2 WHERE id = $1', [id, endsAt]);
}

export async function playingRuns(db: Queryable, staleBefore: Date): Promise<number> {
  const r = await db.query<{ n: string }>("SELECT count(*) AS n FROM dungeon_runs WHERE state = 'playing' AND started_at > $1", [staleBefore]);
  return Number((r.rows[0] as { n: string }).n);
}

export async function activeQueries(db: Queryable): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM pg_stat_activity WHERE datname = current_database() AND state = 'active' AND pid <> pg_backend_pid()`,
  );
  return Number((r.rows[0] as { n: string }).n);
}
