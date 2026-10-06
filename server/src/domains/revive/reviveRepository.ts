import type { Queryable } from '../../db/pool';

export interface ReviveChar {
  id: number;
  level: number;
  revive_coins: number;
  /** 'YYYY-MM-DD' 또는 null */
  revive_coin_day: string | null;
}

export interface ReviveLogRow {
  free: boolean;
  coins_after: number;
  created_at: Date;
}

/** 내 캐릭터 행을 잠그고 읽는다(남의 캐릭터·삭제된 캐릭터는 null) */
export async function lockOwned(db: Queryable, accountId: number, uuid: string): Promise<ReviveChar | null> {
  const r = await db.query<{ id: string; level: number; revive_coins: number; revive_coin_day: string | null }>(
    `SELECT id, level, revive_coins, revive_coin_day::text AS revive_coin_day
       FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL FOR UPDATE`,
    [uuid, accountId],
  );
  const row = r.rows[0];
  return row ? { id: Number(row.id), level: row.level, revive_coins: row.revive_coins, revive_coin_day: row.revive_coin_day } : null;
}

export async function setCoins(db: Queryable, characterId: number, coins: number, day: string): Promise<void> {
  await db.query('UPDATE characters SET revive_coins = $2, revive_coin_day = $3::date WHERE id = $1', [characterId, coins, day]);
}

export async function findLog(db: Queryable, characterId: number, requestId: string): Promise<ReviveLogRow | null> {
  const r = await db.query<ReviveLogRow>(
    'SELECT free, coins_after, created_at FROM revive_log WHERE character_id = $1 AND request_id = $2',
    [characterId, requestId],
  );
  return r.rows[0] ?? null;
}

export async function insertLog(
  db: Queryable,
  v: { characterId: number; requestId: string; context: string; free: boolean; coinsAfter: number; level: number; mapId: string | null; at: Date },
): Promise<void> {
  await db.query(
    `INSERT INTO revive_log (character_id, request_id, context, free, coins_after, level, map_id, created_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
    [v.characterId, v.requestId, v.context, v.free, v.coinsAfter, v.level, v.mapId, v.at],
  );
}
