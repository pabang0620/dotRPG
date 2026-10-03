import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface RunRow {
  id: number;
  uuid: string;
  dungeon_id: string;
  difficulty: number;
  party_size: number;
  state: 'playing' | 'cleared' | 'failed' | 'abandoned' | 'held';
  reset_day: Date;
  started_at: Date;
  ended_at: Date | null;
  room_index: number;
  room_kills: Record<string, number>;
  rank: number | null;
  cards: { item_key: string; count: number }[] | null;
  card_picked: number | null;
}

interface RawRun extends Omit<RunRow, 'id'> {
  id: string;
}
const COLS = `id, uuid, dungeon_id, difficulty, party_size, state, reset_day, started_at, ended_at,
              room_index, room_kills, rank, cards, card_picked`;
const toRun = (r: RawRun): RunRow => ({ ...r, id: Number(r.id) });

export async function findRunByUuid(db: Queryable, characterId: number, uuid: string): Promise<RunRow | null> {
  const r = await db.query<RawRun>(
    `SELECT ${COLS} FROM dungeon_runs WHERE character_id = $1 AND uuid = $2`,
    [characterId, uuid],
  );
  return r.rows[0] ? toRun(r.rows[0]) : null;
}

export async function findPlayingRun(db: Queryable, characterId: number): Promise<RunRow | null> {
  const r = await db.query<RawRun>(
    `SELECT ${COLS} FROM dungeon_runs WHERE character_id = $1 AND state = 'playing'`,
    [characterId],
  );
  return r.rows[0] ? toRun(r.rows[0]) : null;
}

export async function countEntries(db: Queryable, characterId: number, resetDay: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    'SELECT count(*) AS n FROM dungeon_runs WHERE character_id = $1 AND reset_day = $2',
    [characterId, resetDay],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function insertRun(
  client: PoolClient,
  characterId: number,
  dungeonId: string,
  difficulty: number,
  resetDay: Date,
  startedAt: Date,
): Promise<RunRow> {
  const r = await client.query<RawRun>(
    `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, reset_day, started_at)
     VALUES ($1, $2, $3, 1, $4, $5) RETURNING ${COLS}`,
    [characterId, dungeonId, difficulty, resetDay, startedAt],
  );
  return toRun(r.rows[0] as RawRun);
}

export async function abandonRun(client: PoolClient, runId: number, at: Date): Promise<void> {
  await client.query("UPDATE dungeon_runs SET state = 'abandoned', ended_at = $2 WHERE id = $1", [runId, at]);
}

export async function updateRunProgress(
  client: PoolClient,
  runId: number,
  roomIndex: number,
  roomKills: Record<string, number>,
): Promise<void> {
  await client.query('UPDATE dungeon_runs SET room_index = $2, room_kills = $3::jsonb WHERE id = $1', [
    runId,
    roomIndex,
    JSON.stringify(roomKills),
  ]);
}

export interface RunClose {
  state: 'cleared' | 'failed' | 'held';
  endedAt: Date;
  stats: Record<string, unknown>;
  rank: number | null;
  score: Record<string, unknown> | null;
  xpGranted: number | null;
  holdReason: string | null;
  cards: { item_key: string; count: number }[] | null;
}

export async function closeRun(client: PoolClient, runId: number, c: RunClose): Promise<void> {
  await client.query(
    `UPDATE dungeon_runs
        SET state = $2, ended_at = $3, stats = $4::jsonb, rank = $5, score = $6::jsonb,
            xp_granted = $7, hold_reason = $8, cards = $9::jsonb
      WHERE id = $1`,
    [
      runId,
      c.state,
      c.endedAt,
      JSON.stringify(c.stats),
      c.rank,
      c.score ? JSON.stringify(c.score) : null,
      c.xpGranted,
      c.holdReason,
      c.cards ? JSON.stringify(c.cards) : null,
    ],
  );
}

export async function setCardPicked(client: PoolClient, runId: number, index: number, at: Date): Promise<void> {
  await client.query('UPDATE dungeon_runs SET card_picked = $2, card_picked_at = $3 WHERE id = $1', [
    runId,
    index,
    at,
  ]);
}

export async function clearSummary(
  db: Queryable,
  characterId: number,
): Promise<{ dungeon_id: string; difficulty: number; best_rank: number }[]> {
  const r = await db.query<{ dungeon_id: string; difficulty: number; best_rank: number }>(
    `SELECT dungeon_id, difficulty, min(rank) AS best_rank FROM dungeon_runs
      WHERE character_id = $1 AND state = 'cleared' GROUP BY dungeon_id, difficulty`,
    [characterId],
  );
  return r.rows;
}

export async function clearCounts(
  db: Queryable,
  characterId: number,
): Promise<{ total: number; byDungeon: Map<string, number> }> {
  const r = await db.query<{ dungeon_id: string; n: string }>(
    `SELECT dungeon_id, count(*) AS n FROM dungeon_runs
      WHERE character_id = $1 AND state = 'cleared' GROUP BY dungeon_id`,
    [characterId],
  );
  const byDungeon = new Map<string, number>();
  let total = 0;
  for (const row of r.rows) {
    byDungeon.set(row.dungeon_id, Number(row.n));
    total += Number(row.n);
  }
  return { total, byDungeon };
}
