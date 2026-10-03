import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface RunRow {
  id: number;
  uuid: string;
  dungeon_id: string;
  difficulty: number;
  party_size: number;
  state: 'playing' | 'reported' | 'cleared' | 'failed' | 'abandoned' | 'held';
  reset_day: Date;
  started_at: Date;
  ended_at: Date | null;
  room_index: number;
  room_kills: Record<string, number>;
  rank: number | null;
  cards: { item_key: string; count: number }[] | null;
  card_picked: number | null;
  character_id: number;
  party_run_id: number | null;
  slot: number | null;
  humans: number;
  ai_count: number;
  counts_entry: boolean;
  reward_locked: boolean;
  lock_reason: 'ALREADY_CLAIMED' | 'TOO_FEW_HUMANS' | 'KEYS_MISSING' | null;
  power_cap: number | null;
  reported_outcome: 'cleared' | 'failed' | null;
  reported_at: Date | null;
  stats: { elapsed_ms?: number; hits_taken?: number; max_combo?: number; revives_used?: number } | null;
  xp_granted: number | null;
  score: Record<string, number> | null;
}

interface RawRun extends Omit<RunRow, 'id' | 'character_id' | 'party_run_id' | 'power_cap'> {
  id: string;
  character_id: string;
  party_run_id: string | null;
  power_cap: string | null;
}
const COLS = `id, uuid, character_id, dungeon_id, difficulty, party_size, state, reset_day, started_at, ended_at,
              room_index, room_kills, rank, cards, card_picked, party_run_id, slot, humans, ai_count,
              counts_entry, reward_locked, lock_reason, power_cap, reported_outcome, reported_at, stats,
              xp_granted, score`;
const toRun = (r: RawRun): RunRow => ({
  ...r,
  id: Number(r.id),
  character_id: Number(r.character_id),
  party_run_id: r.party_run_id === null ? null : Number(r.party_run_id),
  power_cap: r.power_cap === null ? null : Number(r.power_cap),
});

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
    'SELECT count(*) AS n FROM dungeon_runs WHERE character_id = $1 AND reset_day = $2 AND counts_entry',
    [characterId, resetDay],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export interface NewRun {
  characterId: number;
  dungeonId: string;
  difficulty: number;
  resetDay: Date;
  startedAt: Date;
  humans: number;
  aiCount: number;
  countsEntry: boolean;
  rewardLocked: boolean;
  lockReason: 'ALREADY_CLAIMED' | 'TOO_FEW_HUMANS' | 'KEYS_MISSING' | null;
  powerCap: number | null;
  partyRunId?: number;
  slot?: number;
}

export async function insertRun(client: PoolClient, n: NewRun): Promise<RunRow> {
  const r = await client.query<RawRun>(
    `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, reset_day, started_at, humans, ai_count,
                               counts_entry, reward_locked, lock_reason, power_cap, party_run_id, slot)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14) RETURNING ${COLS}`,
    [
      n.characterId,
      n.dungeonId,
      n.difficulty,
      n.humans + n.aiCount,
      n.resetDay,
      n.startedAt,
      n.humans,
      n.aiCount,
      n.countsEntry,
      n.rewardLocked,
      n.lockReason,
      n.powerCap,
      n.partyRunId ?? null,
      n.slot ?? null,
    ],
  );
  return toRun(r.rows[0] as RawRun);
}

export async function abandonRun(client: PoolClient, runId: number, at: Date): Promise<void> {
  await client.query("UPDATE dungeon_runs SET state = 'abandoned', ended_at = $2 WHERE id = $1", [runId, at]);
  // 파티 판의 행이면 판 멤버 상태도 닫는다(남아 있으면 새 판에 참여하지 못한다)
  await client.query(
    `UPDATE party_run_members SET state = 'done', finished_at = $2
      WHERE dungeon_run_id = $1 AND state IN ('joined', 'playing', 'disconnected')`,
    [runId, at],
  );
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
  rewardLocked?: boolean;
  lockReason?: 'ALREADY_CLAIMED' | 'TOO_FEW_HUMANS' | 'KEYS_MISSING' | null;
}

export async function closeRun(client: PoolClient, runId: number, c: RunClose): Promise<void> {
  await client.query(
    `UPDATE dungeon_runs
        SET state = $2, ended_at = $3, stats = $4::jsonb, rank = $5, score = $6::jsonb,
            xp_granted = $7, hold_reason = $8, cards = $9::jsonb,
            reward_locked = COALESCE($10, reward_locked),
            lock_reason = CASE WHEN $10::boolean IS NULL THEN lock_reason ELSE $11 END
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
      c.rewardLocked ?? null,
      c.lockReason ?? null,
    ],
  );
}

/** 파티 판의 멤버가 결과를 보고했다: 대조 대기(reported) */
export async function markReported(
  client: PoolClient,
  runId: number,
  outcome: 'cleared' | 'failed',
  stats: Record<string, unknown>,
  at: Date,
): Promise<void> {
  await client.query(
    `UPDATE dungeon_runs SET state = 'reported', reported_outcome = $2, reported_at = $3, ended_at = $3, stats = $4::jsonb
      WHERE id = $1`,
    [runId, outcome, at, JSON.stringify(stats)],
  );
}

/** 한 판의 모든 멤버 행(결과 대조, 정산). 읽기만 한다 */
export async function runsOfPartyRun(db: Queryable, partyRunId: number): Promise<RunRow[]> {
  const r = await db.query<RawRun>(`SELECT ${COLS} FROM dungeon_runs WHERE party_run_id = $1 ORDER BY slot`, [
    partyRunId,
  ]);
  return r.rows.map(toRun);
}

export async function findRunById(db: Queryable, id: number): Promise<RunRow | null> {
  const r = await db.query<RawRun>(`SELECT ${COLS} FROM dungeon_runs WHERE id = $1`, [id]);
  return r.rows[0] ? toRun(r.rows[0]) : null;
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

/** 파티 판(초대·입장·진행·끊김)에 참여 중인가. 솔로 입장과 다른 파티 출발을 막는다 */
export async function inPartyRun(db: Queryable, characterId: number): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM party_run_members WHERE character_id = $1 AND state IN ('invited', 'joined', 'playing', 'disconnected')`,
    [characterId],
  );
  return r.rows.length > 0;
}

export async function partyRunUuid(db: Queryable, partyRunId: number): Promise<string | null> {
  const r = await db.query<{ uuid: string }>('SELECT uuid FROM party_runs WHERE id = $1', [partyRunId]);
  return r.rows[0]?.uuid ?? null;
}

/** 카드를 아직 고르지 않은 클리어 판(보류 해제로 늦게 확정된 판 포함). since 이후에 끝난 것만 */
export async function unpickedRuns(db: Queryable, characterId: number, since: Date): Promise<{ uuid: string; ended_at: Date }[]> {
  const r = await db.query<{ uuid: string; ended_at: Date }>(
    `SELECT uuid, ended_at FROM dungeon_runs
      WHERE character_id = $1 AND state = 'cleared' AND cards IS NOT NULL AND card_picked IS NULL AND ended_at > $2
      ORDER BY ended_at DESC LIMIT 20`,
    [characterId, since],
  );
  return r.rows;
}
