import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export async function getLastGathered(db: Queryable, characterId: number, nodeId: string): Promise<Date | null> {
  const r = await db.query<{ last_gathered_at: Date }>(
    'SELECT last_gathered_at FROM character_node_state WHERE character_id = $1 AND node_id = $2',
    [characterId, nodeId],
  );
  return r.rows[0]?.last_gathered_at ?? null;
}

/** since 이후에 채집한 노드 수(노드마다 마지막 채집만 남으므로 서로 다른 노드 수) */
export async function countGatheredSince(client: PoolClient, characterId: number, since: Date): Promise<number> {
  const r = await client.query<{ n: string }>(
    `SELECT count(*) AS n FROM character_node_state WHERE character_id = $1 AND last_gathered_at > $2`,
    [characterId, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function upsertNodeState(
  client: PoolClient,
  characterId: number,
  nodeId: string,
  mapId: string,
  at: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO character_node_state (character_id, node_id, map_id, last_gathered_at) VALUES ($1, $2, $3, $4)
     ON CONFLICT (character_id, node_id) DO UPDATE SET last_gathered_at = EXCLUDED.last_gathered_at`,
    [characterId, nodeId, mapId, at],
  );
}

export async function listRecentNodes(
  db: Queryable,
  characterId: number,
  mapId: string,
  since: Date,
): Promise<{ node_id: string; last_gathered_at: Date }[]> {
  const r = await db.query<{ node_id: string; last_gathered_at: Date }>(
    `SELECT node_id, last_gathered_at FROM character_node_state
      WHERE character_id = $1 AND map_id = $2 AND last_gathered_at > $3 ORDER BY last_gathered_at`,
    [characterId, mapId, since],
  );
  return r.rows;
}

/** 캐릭터당 한 번: PK 충돌이면 false */
export async function insertChest(client: PoolClient, characterId: number, chestId: string, at: Date): Promise<boolean> {
  const r = await client.query(
    `INSERT INTO character_chests (character_id, chest_id, opened_at) VALUES ($1, $2, $3)
     ON CONFLICT (character_id, chest_id) DO NOTHING`,
    [characterId, chestId, at],
  );
  return (r.rowCount ?? 0) > 0;
}
