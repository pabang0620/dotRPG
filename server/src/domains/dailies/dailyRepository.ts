import type { Queryable } from '../../db/pool';
import type { Period } from '../../gamedata/dailyQuests';

export interface DailyRow {
  day: string;
  slot: number;
  template_id: string;
  accepted_at: Date;
  claimed_at: Date | null;
}

const COLS = `to_char(game_day, 'YYYY-MM-DD') AS day, slot, template_id, accepted_at, claimed_at`;

/** 그 기간 종류에서 minDay 이후(포함) 날짜의 칸들 */
export async function rowsSince(db: Queryable, characterId: number, period: Period, minDay: string): Promise<DailyRow[]> {
  const r = await db.query<DailyRow>(
    `SELECT ${COLS} FROM daily_quests WHERE character_id = $1 AND period = $2 AND game_day >= $3::date ORDER BY game_day, slot`,
    [characterId, period, minDay],
  );
  return r.rows.map((x) => ({ ...x, slot: Number(x.slot) }));
}

export async function lockRow(db: Queryable, characterId: number, period: Period, day: string, slot: number): Promise<DailyRow | null> {
  const r = await db.query<DailyRow>(
    `SELECT ${COLS} FROM daily_quests WHERE character_id = $1 AND period = $2 AND game_day = $3::date AND slot = $4 FOR UPDATE`,
    [characterId, period, day, slot],
  );
  const row = r.rows[0];
  return row ? { ...row, slot: Number(row.slot) } : null;
}

/** 수락. 이미 있으면 아무 일도 없다(먼저 고정된 의뢰가 그대로) */
export async function insertRow(
  db: Queryable,
  characterId: number,
  period: Period,
  day: string,
  slot: number,
  templateId: string,
  now: Date,
): Promise<void> {
  await db.query(
    `INSERT INTO daily_quests (character_id, period, game_day, slot, template_id, accepted_at) VALUES ($1, $2, $3::date, $4, $5, $6)
     ON CONFLICT (character_id, period, game_day, slot) DO NOTHING`,
    [characterId, period, day, slot, templateId, now],
  );
}

export async function markClaimed(
  db: Queryable,
  characterId: number,
  period: Period,
  day: string,
  slot: number,
  reward: Record<string, number>,
  requestId: string,
  now: Date,
): Promise<void> {
  await db.query(
    `UPDATE daily_quests SET claimed_at = $5, reward = $6::jsonb, request_id = $7
      WHERE character_id = $1 AND period = $2 AND game_day = $3::date AND slot = $4 AND claimed_at IS NULL`,
    [characterId, period, day, slot, now, JSON.stringify(reward), requestId],
  );
}

export async function hasClaimedQuest(db: Queryable, characterId: number, questId: string): Promise<boolean> {
  const r = await db.query('SELECT 1 FROM quest_claims WHERE character_id = $1 AND quest_id = $2', [characterId, questId]);
  return r.rows.length > 0;
}

/** 수락 이후 그 맵에서 기록된 그 몬스터 처치 수(다른 레벨 맵의 같은 몬스터 id는 세지 않는다) */
export async function killsSince(db: Queryable, characterId: number, mapId: string, monsterId: string, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM kill_log WHERE character_id = $1 AND created_at >= $4 AND map_id = $2 AND monster_id = $3`,
    [characterId, mapId, monsterId, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 수락 이후 주어진 던전들의 클리어 수: 직접 클리어 + 소탕(레이드는 소탕이 없어 직접 클리어만 잡힌다) */
export async function clearsSince(db: Queryable, characterId: number, dungeonIds: string[], since: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT (SELECT count(*) FROM dungeon_runs
              WHERE character_id = $1 AND state = 'cleared' AND ended_at >= $3 AND dungeon_id = ANY($2::text[]))
          + (SELECT count(*) FROM dungeon_sweeps
              WHERE character_id = $1 AND created_at >= $3 AND dungeon_id = ANY($2::text[])) AS n`,
    [characterId, dungeonIds, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 수락 이후 청구한 일일 의뢰 수(주간 daily 형식) */
export async function dailyClaimsSince(db: Queryable, characterId: number, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM daily_quests WHERE character_id = $1 AND period = 'day' AND claimed_at >= $2`,
    [characterId, since],
  );
  return Number((r.rows[0] as { n: string }).n);
}
