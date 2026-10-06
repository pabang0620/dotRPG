// 소탕 SQL. 재화가 움직이는 쓰기는 호출하는 Service가 캐릭터 행(과 계정 행) 잠금 아래에서만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';

/** 직접 클리어 기록의 던전·난이도별 최고 등급(번호가 작을수록 좋다). 보상이 잠긴 판은 기록으로 치지 않는다(설계 0.1) */
export async function bestRanks(db: Queryable, characterId: number): Promise<Map<string, number>> {
  const r = await db.query<{ dungeon_id: string; difficulty: number; best: number }>(
    `SELECT dungeon_id, difficulty, min(rank) AS best FROM dungeon_runs
      WHERE character_id = $1 AND state = 'cleared' AND NOT reward_locked AND rank IS NOT NULL
      GROUP BY dungeon_id, difficulty`,
    [characterId],
  );
  return new Map(r.rows.map((x) => [`${x.dungeon_id}:${x.difficulty}`, x.best] as const));
}

/** 계정 최고 레벨: 삭제한 캐릭터 포함(낮은 가격을 노린 삭제를 막는다, D5) */
export async function accountMaxLevel(db: Queryable, accountId: number): Promise<number> {
  const r = await db.query<{ lv: number | null }>('SELECT max(level) AS lv FROM characters WHERE account_id = $1', [accountId]);
  return r.rows[0]?.lv ?? 1;
}

export interface NewSweep {
  uuid: string;
  characterId: number;
  dungeonId: string;
  difficulty: number;
  resetDay: Date;
  lotId: number;
  xpGranted: number;
  card: { item_key: string; count: number };
  requestId: string;
}

export async function insertSweep(client: PoolClient, n: NewSweep): Promise<void> {
  await client.query(
    `INSERT INTO dungeon_sweeps (uuid, character_id, dungeon_id, difficulty, reset_day, lot_id, xp_granted, card, request_id)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8::jsonb, $9)`,
    [n.uuid, n.characterId, n.dungeonId, n.difficulty, n.resetDay, n.lotId, n.xpGranted, JSON.stringify(n.card), n.requestId],
  );
}
