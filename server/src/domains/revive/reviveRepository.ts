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

/** 진행 중인 던전·요일던전·레이드 판(솔로 또는 파티)이 있는가: 있으면 부활은 클라이언트가 뭐라 보내든 던전 부활이다 */
export async function inPlayingRun(db: Queryable, characterId: number): Promise<boolean> {
  const r = await db.query<{ v: boolean }>(
    `SELECT EXISTS (SELECT 1 FROM dungeon_runs WHERE character_id = $1 AND state = 'playing')
         OR EXISTS (SELECT 1 FROM party_run_members m JOIN party_runs p ON p.id = m.party_run_id
                     WHERE m.character_id = $1 AND p.state = 'playing' AND m.state IN ('joined', 'playing', 'disconnected')) AS v`,
    [characterId],
  );
  return r.rows[0]?.v === true;
}

/** `since` 이후 던전 부활 횟수(정산 때 클라이언트 보고값의 하한) */
export async function countDungeonRevives(db: Queryable, characterId: number, since: Date): Promise<number> {
  const r = await db.query<{ n: number }>(
    `SELECT count(*)::int AS n FROM revive_log WHERE character_id = $1 AND context = 'dungeon' AND created_at >= $2`,
    [characterId, since],
  );
  return r.rows[0]?.n ?? 0;
}
