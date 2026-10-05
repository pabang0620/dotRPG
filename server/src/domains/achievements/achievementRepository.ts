import type { Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import type { AchievementStat } from './achievementDefs';

/** 업적 판정에 쓰는 값들. 모두 서버 기록에서 계산한다 */
export async function statsOf(db: Queryable, characterId: number): Promise<Record<AchievementStat, number>> {
  const raids = [...getGameData().economy.dungeons.byId.values()].filter((d) => d.isRaid).map((d) => d.id);
  const r = await db.query<Record<string, string | null>>(
    `SELECT
       (SELECT coalesce(max(CASE WHEN position('+' in to_key) > 0 THEN split_part(to_key, '+', 2)::int ELSE 0 END), 0)
          FROM enhance_log WHERE character_id = $1 AND outcome = 'success' AND to_key IS NOT NULL) AS enhance_best,
       (SELECT count(*) FROM enhance_log WHERE character_id = $1) AS enhance_tries,
       (SELECT coalesce(sum(gold), 0) FROM enhance_log WHERE character_id = $1) AS enhance_gold,
       (SELECT count(*) FROM enhance_log WHERE character_id = $1 AND outcome = 'destroyed') AS enhance_destroyed,
       (SELECT count(*) FROM kill_log WHERE character_id = $1) AS kills,
       (SELECT count(*) FROM kill_log WHERE character_id = $1 AND monster_id LIKE 'boss\\_%') AS boss_kills,
       (SELECT CASE WHEN coalesce((career->>'career')::int, 0) > 0 THEN 1 ELSE 0 END FROM character_state WHERE character_id = $1) AS promoted,
       (SELECT count(*) FROM gacha_pulls WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)) AS gacha_pulls,
       (SELECT count(*) FROM gacha_pulls WHERE account_id = (SELECT account_id FROM characters WHERE id = $1) AND rarity IN ('unique', 'legendary')) AS gacha_top,
       (SELECT count(*) FROM account_cosmetics WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)) AS cosmetics,
       (SELECT count(*) FROM account_cosmetics WHERE account_id = (SELECT account_id FROM characters WHERE id = $1) AND item_id LIKE 'skin\\_%') AS skins,
       (SELECT coalesce(-sum(delta), 0) FROM star_ledger WHERE account_id = (SELECT account_id FROM characters WHERE id = $1) AND reason IN ('gacha', 'exchange')) AS stars_spent,
       (SELECT count(*) FROM dungeon_runs WHERE character_id = $1 AND state = 'cleared') AS dungeon_clears,
       (SELECT count(*) FROM dungeon_runs WHERE character_id = $1 AND state = 'cleared' AND party_run_id IS NOT NULL) AS party_clears,
       (SELECT count(*) FROM dungeon_runs WHERE character_id = $1 AND state = 'cleared' AND dungeon_id = ANY($2::text[])) AS raid_clears,
       (SELECT level FROM characters WHERE id = $1) AS level,
       (SELECT count(*) FROM quest_claims WHERE character_id = $1) AS quests,
       (SELECT gold FROM characters WHERE id = $1) AS gold`,
    [characterId, raids],
  );
  const row = r.rows[0] ?? {};
  const n = (k: string): number => Number(row[k] ?? 0);
  return {
    enhance_best: n('enhance_best'),
    enhance_tries: n('enhance_tries'),
    enhance_gold: n('enhance_gold'),
    enhance_destroyed: n('enhance_destroyed'),
    kills: n('kills'),
    boss_kills: n('boss_kills'),
    promoted: n('promoted'),
    gacha_pulls: n('gacha_pulls'),
    gacha_top: n('gacha_top'),
    cosmetics: n('cosmetics'),
    skins: n('skins'),
    stars_spent: n('stars_spent'),
    dungeon_clears: n('dungeon_clears'),
    party_clears: n('party_clears'),
    raid_clears: n('raid_clears'),
    level: n('level'),
    quests: n('quests'),
    gold: n('gold'),
  };
}

export async function achievedOf(db: Queryable, characterId: number): Promise<Map<string, Date>> {
  const r = await db.query<{ achievement_id: string; achieved_at: Date }>(
    'SELECT achievement_id, achieved_at FROM character_achievements WHERE character_id = $1',
    [characterId],
  );
  return new Map(r.rows.map((x) => [x.achievement_id, x.achieved_at] as const));
}

export async function insertAchieved(db: Queryable, characterId: number, ids: string[], at: Date): Promise<void> {
  if (ids.length === 0) return;
  await db.query(
    `INSERT INTO character_achievements (character_id, achievement_id, achieved_at)
     SELECT $1, unnest($2::text[]), $3 ON CONFLICT DO NOTHING`,
    [characterId, ids, at],
  );
}

export async function titleOf(db: Queryable, characterId: number): Promise<string | null> {
  const r = await db.query<{ title_achievement: string | null }>('SELECT title_achievement FROM characters WHERE id = $1', [characterId]);
  return r.rows[0]?.title_achievement ?? null;
}

export async function setTitle(db: Queryable, characterId: number, achievementId: string | null): Promise<void> {
  await db.query('UPDATE characters SET title_achievement = $2 WHERE id = $1', [characterId, achievementId]);
}
