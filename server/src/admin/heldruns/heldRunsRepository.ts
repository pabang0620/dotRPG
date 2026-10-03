import type { Queryable } from '../../db/pool';

export async function pending(db: Queryable) {
  const r = await db.query<{
    uuid: string; dungeon_id: string; difficulty: number; character_name: string; hold_reason: string | null; ended_at: Date; party: boolean; wait_s: number;
  }>(
    `SELECT d.uuid, d.dungeon_id, d.difficulty, c.name AS character_name, d.hold_reason, d.ended_at, d.party_run_id IS NOT NULL AS party,
            extract(epoch FROM now() - d.ended_at)::int AS wait_s
       FROM dungeon_runs d JOIN characters c ON c.id = d.character_id
      WHERE d.state = 'held' AND NOT EXISTS (SELECT 1 FROM held_run_reviews h WHERE h.dungeon_run_id = d.id)
      ORDER BY d.ended_at, d.id LIMIT 50`,
  );
  return r.rows;
}

export interface HeldRun {
  id: number;
  uuid: string;
  character_id: number;
  character_uuid: string;
  character_name: string;
  account_id: number;
  state: string;
  hold_reason: string | null;
  party_run_id: number | null;
  party_run_uuid: string | null;
  started_at: Date;
  ended_at: Date | null;
  stats: Record<string, unknown> | null;
  room_kills: Record<string, number>;
  dungeon_id: string;
  difficulty: number;
  reviewed: string | null;
}

export async function findRun(db: Queryable, uuid: string): Promise<HeldRun | null> {
  const r = await db.query<Record<string, unknown>>(
    `SELECT d.id, d.uuid, d.character_id, c.uuid AS character_uuid, c.name AS character_name, c.account_id, d.state, d.hold_reason,
            d.party_run_id, pr.uuid AS party_run_uuid, d.started_at, d.ended_at, d.stats, d.room_kills, d.dungeon_id, d.difficulty,
            (SELECT decision FROM held_run_reviews h WHERE h.dungeon_run_id = d.id) AS reviewed
       FROM dungeon_runs d JOIN characters c ON c.id = d.character_id LEFT JOIN party_runs pr ON pr.id = d.party_run_id
      WHERE d.uuid = $1`,
    [uuid],
  );
  const x = r.rows[0];
  if (!x) return null;
  return {
    ...(x as unknown as HeldRun),
    id: Number(x.id),
    character_id: Number(x.character_id),
    account_id: Number(x.account_id),
    party_run_id: x.party_run_id === null ? null : Number(x.party_run_id),
  };
}

/** 판 행을 잠근다(같은 판의 동시 결정을 직렬화) */
export async function lockRun(db: Queryable, id: number): Promise<void> {
  await db.query('SELECT 1 FROM dungeon_runs WHERE id = $1 FOR UPDATE', [id]);
}

export async function reviewExists(db: Queryable, runId: number): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM held_run_reviews WHERE dungeon_run_id = $1', [runId]);
  return r.rows.length > 0;
}

export async function insertReview(
  db: Queryable,
  v: { runId: number; adminId: number; decision: 'released' | 'rejected'; note: string; prevEndedAt: Date; requestId: string },
): Promise<void> {
  await db.query(
    `INSERT INTO held_run_reviews (dungeon_run_id, admin_id, decision, note, prev_ended_at, request_id) VALUES ($1, $2, $3, $4, $5, $6)`,
    [v.runId, v.adminId, v.decision, v.note, v.prevEndedAt, v.requestId],
  );
}

export async function killSummary(db: Queryable, runId: number) {
  const r = await db.query<{ monster_id: string; n: string }>(
    'SELECT monster_id, count(*) AS n FROM kill_log WHERE run_id = $1 GROUP BY monster_id ORDER BY monster_id',
    [runId],
  );
  return r.rows;
}

export async function anomaliesOfRun(db: Queryable, runUuid: string) {
  const r = await db.query<{ kind: string; severity: number; detail: unknown; created_at: Date }>(
    `SELECT kind, severity, detail, created_at FROM anomaly_log
      WHERE kind IN ('dungeon_result', 'party_result') AND detail->>'run_id' = $1 ORDER BY id`,
    [runUuid],
  );
  return r.rows;
}

export async function peerRuns(db: Queryable, partyRunId: number, exceptId: number) {
  const r = await db.query<{ uuid: string; character_name: string; state: string; hold_reason: string | null }>(
    `SELECT d.uuid, c.name AS character_name, d.state, d.hold_reason FROM dungeon_runs d JOIN characters c ON c.id = d.character_id
      WHERE d.party_run_id = $1 AND d.id <> $2 ORDER BY d.slot`,
    [partyRunId, exceptId],
  );
  return r.rows;
}

export async function recentHeldOfAccount(db: Queryable, accountId: number) {
  const r = await db.query<{ uuid: string; hold_reason: string | null; ended_at: Date; decision: string | null }>(
    `SELECT d.uuid, d.hold_reason, d.ended_at, (SELECT decision FROM held_run_reviews h WHERE h.dungeon_run_id = d.id) AS decision
       FROM dungeon_runs d JOIN characters c ON c.id = d.character_id
      WHERE c.account_id = $1 AND (d.state = 'held' OR EXISTS (SELECT 1 FROM held_run_reviews h WHERE h.dungeon_run_id = d.id))
      ORDER BY d.id DESC LIMIT 10`,
    [accountId],
  );
  return r.rows;
}
