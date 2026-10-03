import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

export async function allClaimed(db: Queryable, characterId: number): Promise<Set<string>> {
  const r = await db.query<{ quest_id: string }>('SELECT quest_id FROM quest_claims WHERE character_id = $1', [
    characterId,
  ]);
  return new Set(r.rows.map((x) => x.quest_id));
}

export async function killTotals(db: Queryable, characterId: number): Promise<Map<string, number>> {
  const r = await db.query<{ monster_id: string; kills: string }>(
    'SELECT monster_id, kills FROM kill_stats WHERE character_id = $1',
    [characterId],
  );
  return new Map(r.rows.map((x) => [x.monster_id, Number(x.kills)] as const));
}

export async function storyFlags(db: Queryable, characterId: number): Promise<Set<string>> {
  const r = await db.query<{ story_flags: string[] }>('SELECT story_flags FROM character_state WHERE character_id = $1', [
    characterId,
  ]);
  return new Set(r.rows[0]?.story_flags ?? []);
}

export async function insertClaim(
  client: PoolClient,
  characterId: number,
  questId: string,
  reward: Record<string, unknown>,
  requestId: string,
  at: Date,
): Promise<void> {
  await client.query(
    `INSERT INTO quest_claims (character_id, quest_id, claimed_at, reward, request_id)
     VALUES ($1, $2, $3, $4::jsonb, $5)`,
    [characterId, questId, at, JSON.stringify(reward), requestId],
  );
}

export async function sumBonusMaxHealth(db: Queryable, characterId: number): Promise<number> {
  const r = await db.query<{ n: string | null }>(
    "SELECT sum((reward->>'max_health')::int) AS n FROM quest_claims WHERE character_id = $1",
    [characterId],
  );
  return Number(r.rows[0]?.n ?? 0);
}
