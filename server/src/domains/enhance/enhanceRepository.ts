import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export async function getPity(db: Queryable, characterId: number, itemKey: string): Promise<number> {
  const r = await db.query<{ pity: number }>(
    'SELECT pity FROM character_enhance_pity WHERE character_id = $1 AND item_key = $2',
    [characterId, itemKey],
  );
  return r.rows[0]?.pity ?? 0;
}

export async function setPity(
  client: PoolClient,
  characterId: number,
  itemKey: string,
  pity: number,
  at: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO character_enhance_pity (character_id, item_key, pity, updated_at) VALUES ($1, $2, $3, $4)
     ON CONFLICT (character_id, item_key) DO UPDATE SET pity = EXCLUDED.pity, updated_at = EXCLUDED.updated_at`,
    [characterId, itemKey, pity, at],
  );
}

export async function clearPity(client: PoolClient, characterId: number, itemKey: string): Promise<void> {
  await client.query('DELETE FROM character_enhance_pity WHERE character_id = $1 AND item_key = $2', [
    characterId,
    itemKey,
  ]);
}

export interface NewEnhanceLog {
  uuid: string;
  characterId: number;
  requestId: string;
  fromKey: string;
  toKey: string | null;
  targetLocation: 'bag' | 'worn';
  targetSlot: number | null;
  outcome: string;
  roll: number;
  successPercent: number;
  pityBefore: number;
  pityAfter: number;
  gold: number;
  bone: number;
  ore: number;
  essence: number;
  ticketUsed: boolean;
  createdAt: Date;
}

export async function insertEnhanceLog(client: PoolClient, l: NewEnhanceLog): Promise<void> {
  await client.query(
    `INSERT INTO enhance_log (uuid, character_id, request_id, from_key, to_key, target_location, target_slot,
                              outcome, roll, success_percent, pity_before, pity_after, gold, bone, ore, essence,
                              ticket_used, created_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18)`,
    [
      l.uuid,
      l.characterId,
      l.requestId,
      l.fromKey,
      l.toKey,
      l.targetLocation,
      l.targetSlot,
      l.outcome,
      l.roll,
      l.successPercent,
      l.pityBefore,
      l.pityAfter,
      l.gold,
      l.bone,
      l.ore,
      l.essence,
      l.ticketUsed,
      l.createdAt,
    ],
  );
}
