import type { PoolClient } from 'pg';

/** 캐릭터당 한 번: PK 충돌이면 false */
export async function insertChest(client: PoolClient, characterId: number, chestId: string, at: Date): Promise<boolean> {
  const r = await client.query(
    `INSERT INTO character_chests (character_id, chest_id, opened_at) VALUES ($1, $2, $3)
     ON CONFLICT (character_id, chest_id) DO NOTHING`,
    [characterId, chestId, at],
  );
  return (r.rowCount ?? 0) > 0;
}
