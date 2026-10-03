import type { Queryable } from '../../db/pool';

export async function storageKindCount(db: Queryable, characterId: number): Promise<number> {
  const r = await db.query<{ n: string }>(
    "SELECT count(*) AS n FROM character_items WHERE character_id = $1 AND location = 'storage'",
    [characterId],
  );
  return Number((r.rows[0] as { n: string }).n);
}
