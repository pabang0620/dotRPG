// 완료 상자 SQL만. 호출 쪽이 트랜잭션을 연다(청구는 runEconomy 안, 캐릭터 행이 잠겨 있다).
import type { Queryable } from '../../db/pool';
import type { Period } from '../../gamedata/dailyQuests';

export interface ChestRow {
  day: string;
  streak: number;
  step: number;
}

/** 그 기간 종류에서 minDay 이후(포함) 상자 기록 */
export async function chestsSince(db: Queryable, characterId: number, period: Period, minDay: string): Promise<ChestRow[]> {
  const r = await db.query<{ day: string; streak: number; step: number }>(
    `SELECT to_char(game_day, 'YYYY-MM-DD') AS day, streak, step FROM daily_chests
      WHERE character_id = $1 AND period = $2 AND game_day >= $3::date ORDER BY game_day`,
    [characterId, period, minDay],
  );
  return r.rows.map((x) => ({ day: x.day, streak: Number(x.streak), step: Number(x.step) }));
}

/** 그 기간 날짜에 청구한 칸 수 */
export async function claimedSlots(db: Queryable, characterId: number, period: Period, day: string): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM daily_quests WHERE character_id = $1 AND period = $2 AND game_day = $3::date AND claimed_at IS NOT NULL`,
    [characterId, period, day],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 기간 날짜별 청구한 칸 수(minDay 이후) */
export async function claimedSlotsSince(db: Queryable, characterId: number, period: Period, minDay: string): Promise<Map<string, number>> {
  const r = await db.query<{ day: string; n: string }>(
    `SELECT to_char(game_day, 'YYYY-MM-DD') AS day, count(*) AS n FROM daily_quests
      WHERE character_id = $1 AND period = $2 AND game_day >= $3::date AND claimed_at IS NOT NULL GROUP BY game_day`,
    [characterId, period, minDay],
  );
  return new Map(r.rows.map((x) => [x.day, Number(x.n)]));
}

/** 계정의 다른 캐릭터를 포함해 그 주 상자로 스킨 게이지를 이미 채웠는지(호출 쪽이 계정 행을 잠근 뒤 부른다) */
export async function gaugeGrantedThisWeek(db: Queryable, accountId: number, weekStart: string): Promise<boolean> {
  const r = await db.query(`SELECT 1 FROM daily_chests WHERE account_id = $1 AND period = 'week' AND game_day = $2::date AND skin_gauge > 0 LIMIT 1`, [
    accountId,
    weekStart,
  ]);
  return r.rows.length > 0;
}

export async function insertChest(
  db: Queryable,
  o: {
    characterId: number;
    accountId: number;
    period: Period;
    day: string;
    streak: number;
    step: number;
    level: number;
    rewards: unknown[];
    skinGauge: number;
    ratesVersion: string;
    requestId: string;
    now: Date;
  },
): Promise<void> {
  await db.query(
    `INSERT INTO daily_chests (character_id, account_id, period, game_day, streak, step, level, rewards, skin_gauge, rates_version, request_id, claimed_at)
     VALUES ($1, $2, $3, $4::date, $5, $6, $7, $8::jsonb, $9, $10, $11, $12)`,
    [o.characterId, o.accountId, o.period, o.day, o.streak, o.step, o.level, JSON.stringify(o.rewards), o.skinGauge, o.ratesVersion, o.requestId, o.now],
  );
}

/** 전날 상자를 늦게 연 경우: 그다음 날 상자의 연속 일수만 이어 붙인다(이미 준 보상은 그대로) */
export async function setStreak(db: Queryable, characterId: number, day: string, streak: number): Promise<boolean> {
  const r = await db.query(`UPDATE daily_chests SET streak = $3 WHERE character_id = $1 AND period = 'day' AND game_day = $2::date`, [characterId, day, streak]);
  return (r.rowCount ?? 0) > 0;
}
