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

export async function getDeliveries(db: Queryable, characterId: number, siteId: string): Promise<Map<string, number>> {
  const r = await db.query<{ item_key: string; delivered: number }>(
    'SELECT item_key, delivered FROM site_deliveries WHERE character_id = $1 AND site_id = $2',
    [characterId, siteId],
  );
  return new Map(r.rows.map((x) => [x.item_key, x.delivered] as const));
}

export async function addDelivery(
  client: PoolClient,
  characterId: number,
  siteId: string,
  itemKey: string,
  add: number,
  at: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO site_deliveries (character_id, site_id, item_key, delivered, updated_at) VALUES ($1, $2, $3, $4, $5)
     ON CONFLICT (character_id, site_id, item_key)
     DO UPDATE SET delivered = site_deliveries.delivered + EXCLUDED.delivered, updated_at = EXCLUDED.updated_at`,
    [characterId, siteId, itemKey, add, at],
  );
}

export async function claimedAmong(db: Queryable, characterId: number, questIds: string[]): Promise<Set<string>> {
  if (questIds.length === 0) return new Set();
  const r = await db.query<{ quest_id: string }>(
    'SELECT quest_id FROM quest_claims WHERE character_id = $1 AND quest_id = ANY($2::text[])',
    [characterId, questIds],
  );
  return new Set(r.rows.map((x) => x.quest_id));
}
