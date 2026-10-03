import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface KillRow {
  monster_id: string;
  monster_level: number;
}

export async function countKillsSince(db: Queryable, characterId: number, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    'SELECT count(*) AS n FROM kill_log WHERE character_id = $1 AND created_at > $2',
    [characterId, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function countFieldKillsSince(
  db: Queryable,
  characterId: number,
  mapId: string,
  monsterId: string,
  since: Date,
): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM kill_log
      WHERE character_id = $1 AND context = 'field' AND map_id = $2 AND monster_id = $3 AND created_at > $4`,
    [characterId, mapId, monsterId, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 던전이 아닌 처치(필드, 연출)의 최근 창 */
export async function killsInWindow(db: Queryable, characterId: number, since: Date): Promise<KillRow[]> {
  const r = await db.query<KillRow>(
    `SELECT monster_id, monster_level FROM kill_log
      WHERE character_id = $1 AND run_id IS NULL AND created_at > $2`,
    [characterId, since],
  );
  return r.rows;
}

export async function killsOfRun(db: Queryable, runId: number): Promise<KillRow[]> {
  const r = await db.query<KillRow>('SELECT monster_id, monster_level FROM kill_log WHERE run_id = $1', [runId]);
  return r.rows;
}

export async function killCount(db: Queryable, characterId: number, monsterId: string): Promise<number> {
  const r = await db.query<{ kills: string }>(
    'SELECT kills FROM kill_stats WHERE character_id = $1 AND monster_id = $2',
    [characterId, monsterId],
  );
  return Number(r.rows[0]?.kills ?? 0);
}

export interface NewKill {
  characterId: number;
  mapId: string;
  monsterId: string;
  monsterLevel: number;
  context: 'field' | 'scripted' | 'dungeon';
  hits: number;
  xpGranted: number;
  requestId: string;
  createdAt: Date;
  runId: number | null;
  roomIndex: number | null;
}

export async function insertKill(client: PoolClient, k: NewKill): Promise<number> {
  const r = await client.query<{ id: string }>(
    `INSERT INTO kill_log (character_id, map_id, monster_id, monster_level, context, hits, xp_granted,
                           request_id, created_at, run_id, room_index)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11) RETURNING id`,
    [
      k.characterId,
      k.mapId,
      k.monsterId,
      k.monsterLevel,
      k.context,
      k.hits,
      k.xpGranted,
      k.requestId,
      k.createdAt,
      k.runId,
      k.roomIndex,
    ],
  );
  return Number((r.rows[0] as { id: string }).id);
}

export async function bumpKillStats(
  client: PoolClient,
  characterId: number,
  monsterId: string,
  at: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO kill_stats (character_id, monster_id, kills, updated_at) VALUES ($1, $2, 1, $3)
     ON CONFLICT (character_id, monster_id) DO UPDATE SET kills = kill_stats.kills + 1, updated_at = EXCLUDED.updated_at`,
    [characterId, monsterId, at],
  );
}

export async function countOpenDrops(db: Queryable, characterId: number, now: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    'SELECT count(*) AS n FROM drops WHERE character_id = $1 AND claimed_at IS NULL AND expires_at > $2',
    [characterId, now],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export interface DropRow {
  uuid: string;
  item_key: string;
  count: number;
  expires_at: Date;
}

export async function insertDrop(
  client: PoolClient,
  characterId: number,
  killId: number,
  itemKey: string,
  count: number,
  expiresAt: Date,
  createdAt: Date,
): Promise<DropRow> {
  const r = await client.query<DropRow>(
    `INSERT INTO drops (character_id, kill_id, item_key, count, expires_at, created_at)
     VALUES ($1, $2, $3, $4, $5, $6) RETURNING uuid, item_key, count, expires_at`,
    [characterId, killId, itemKey, count, expiresAt, createdAt],
  );
  return r.rows[0] as DropRow;
}
