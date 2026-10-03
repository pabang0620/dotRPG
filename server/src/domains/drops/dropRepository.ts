import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export interface ClaimedDrop {
  id: string;
  uuid: string;
  item_key: string;
  count: number;
}

/** 한 번만 보장: claimed_at IS NULL 조건부 UPDATE 한 문장. 반환된 행만 지급한다 */
export async function claimOpen(
  client: PoolClient,
  characterId: number,
  uuids: string[],
  now: Date,
): Promise<ClaimedDrop[]> {
  const r = await client.query<ClaimedDrop>(
    `UPDATE drops SET claimed_at = $3
      WHERE uuid = ANY($1::uuid[]) AND character_id = $2 AND claimed_at IS NULL AND expires_at > $3
      RETURNING id, uuid, item_key, count`,
    [uuids, characterId, now],
  );
  return r.rows.map((x) => ({ ...x, id: String(x.id) })).sort((a, b) => Number(a.id) - Number(b.id));
}

export interface DropState {
  uuid: string;
  character_id: string;
  claimed_at: Date | null;
  expires_at: Date;
}

export async function findDrops(db: Queryable, uuids: string[]): Promise<DropState[]> {
  const r = await db.query<DropState>(
    'SELECT uuid, character_id, claimed_at, expires_at FROM drops WHERE uuid = ANY($1::uuid[])',
    [uuids],
  );
  return r.rows;
}
