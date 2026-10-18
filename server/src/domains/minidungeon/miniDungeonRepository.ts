import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { DAILY_LIMIT_MS, type MiniTier } from './miniDungeonRules';

export type MiniState = 'playing' | 'left' | 'death' | 'disconnected' | 'expired' | 'level_cap';
export interface MiniRun {
  id: number; uuid: string; character_id: number; reset_day: Date; tier: MiniTier; entry_level: number;
  xp_per_kill: number; state: MiniState; started_at: Date; ends_at: Date; charged_until: Date;
  ended_at: Date | null; kill_credits_ms: number; credits_at: Date; kills: number; xp_granted: number;
}
type RawRun = Omit<MiniRun, 'id' | 'character_id' | 'xp_granted'> & { id: string; character_id: string; xp_granted: string };
const convert = (r: RawRun): MiniRun => ({ ...r, id: Number(r.id), character_id: Number(r.character_id), xp_granted: Number(r.xp_granted) });

export async function active(db: Queryable, characterId: number): Promise<MiniRun | null> {
  const r = await db.query<RawRun>("SELECT * FROM mini_dungeon_runs WHERE character_id = $1 AND state = 'playing'", [characterId]);
  return r.rows[0] ? convert(r.rows[0]) : null;
}
export async function byUuid(db: Queryable, characterId: number, uuid: string): Promise<MiniRun | null> {
  const r = await db.query<RawRun>('SELECT * FROM mini_dungeon_runs WHERE character_id = $1 AND uuid = $2', [characterId, uuid]);
  return r.rows[0] ? convert(r.rows[0]) : null;
}
/** Caller already holds the character lock. Also used by normal kill/dungeon/party entry. */
export async function assertNotActive(db: Queryable, characterId: number, now: Date): Promise<void> {
  const r = await active(db, characterId);
  if (r && now < r.ends_at) throw new AppError(409, '미니던전에서 나온 뒤 이용해 주세요.', 'MINI_DUNGEON_ACTIVE');
}
export async function day(db: Queryable, characterId: number, resetDay: Date): Promise<number> {
  await db.query('INSERT INTO mini_dungeon_days(character_id, reset_day) VALUES ($1, $2) ON CONFLICT DO NOTHING', [characterId, resetDay]);
  const r = await db.query<{ used_ms: number }>('SELECT used_ms FROM mini_dungeon_days WHERE character_id = $1 AND reset_day = $2', [characterId, resetDay]);
  return r.rows[0]?.used_ms ?? 0;
}
export async function charge(client: PoolClient, run: MiniRun, ms: number, until: Date): Promise<void> {
  if (ms > 0) await client.query('UPDATE mini_dungeon_days SET used_ms = LEAST($3, used_ms + $4) WHERE character_id = $1 AND reset_day = $2', [run.character_id, run.reset_day, DAILY_LIMIT_MS, ms]);
  await client.query('UPDATE mini_dungeon_runs SET charged_until = $2 WHERE id = $1', [run.id, until]);
  run.charged_until = until;
}
export async function close(client: PoolClient, run: MiniRun, state: Exclude<MiniState, 'playing'>, at: Date): Promise<void> {
  await client.query('UPDATE mini_dungeon_runs SET state = $2, ended_at = $3 WHERE id = $1', [run.id, state, at]);
  run.state = state; run.ended_at = at;
}
export async function insert(client: PoolClient, data: { characterId: number; resetDay: Date; tier: MiniTier; entryLevel: number; xp: number; now: Date; endsAt: Date }): Promise<MiniRun> {
  const r = await client.query<RawRun>(`INSERT INTO mini_dungeon_runs(character_id, reset_day, tier, entry_level, xp_per_kill, started_at, ends_at, charged_until, credits_at)
    VALUES ($1,$2,$3,$4,$5,$6,$7,$6,$6) RETURNING *`, [data.characterId, data.resetDay, data.tier, data.entryLevel, data.xp, data.now, data.endsAt]);
  return convert(r.rows[0] as RawRun);
}
export async function hasKill(db: Queryable, runId: number, monsterRef: string): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM mini_dungeon_kills WHERE run_id = $1 AND monster_ref = $2', [runId, monsterRef]);
  return r.rows.length > 0;
}
export async function killsSince(db: Queryable, runId: number, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM mini_dungeon_kills WHERE run_id = $1 AND created_at > $2', [runId, since]);
  return Number(r.rows[0]?.n ?? 0);
}
export async function recordKill(client: PoolClient, run: MiniRun, ref: string, requestId: string, xp: number, credits: number, now: Date): Promise<void> {
  await client.query('INSERT INTO mini_dungeon_kills(run_id, monster_ref, request_id, granted_xp, created_at) VALUES ($1,$2,$3,$4,$5)', [run.id, ref, requestId, xp, now]);
  await client.query('UPDATE mini_dungeon_runs SET kills = kills + 1, xp_granted = xp_granted + $2, kill_credits_ms = $3, credits_at = $4 WHERE id = $1', [run.id, xp, credits, now]);
  run.kills++; run.xp_granted += xp; run.kill_credits_ms = credits; run.credits_at = now;
}
