// 월드보스·길드보스 SQL. 도전 쓰기는 캐릭터 행 잠금(runEconomy) 아래에서, 공유 체력은 원자적 UPDATE로 깎는다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import type { BossKind } from './bossRushRules';

export interface Season {
  id: number;
  uuid: string;
  kind: BossKind;
  week_start: Date;
  week_end: Date;
  boss_id: string;
  max_hp: number;
  hp_left: number;
  defeated_at: Date | null;
  settled_at: Date | null;
}
type RawSeason = Omit<Season, 'id' | 'max_hp' | 'hp_left'> & { id: string; max_hp: string; hp_left: string };
const toSeason = (r: RawSeason): Season => ({ ...r, id: Number(r.id), max_hp: Number(r.max_hp), hp_left: Number(r.hp_left) });

/** 그 주의 시즌(없으면 만든다). 동시에 만들어도 UNIQUE(kind, week_start)로 하나만 남는다 */
export async function ensureSeason(db: Queryable, kind: BossKind, weekStart: Date, weekEnd: Date, bossId: string, hp: number): Promise<Season> {
  await db.query(
    `INSERT INTO boss_seasons (kind, week_start, week_end, boss_id, max_hp, hp_left)
     VALUES ($1, $2, $3, $4, $5, $5) ON CONFLICT (kind, week_start) DO NOTHING`,
    [kind, weekStart, weekEnd, bossId, hp],
  );
  const r = await db.query<RawSeason>('SELECT * FROM boss_seasons WHERE kind = $1 AND week_start = $2', [kind, weekStart]);
  return toSeason(r.rows[0] as RawSeason);
}

export async function seasonAt(db: Queryable, kind: BossKind, weekStart: Date): Promise<Season | null> {
  const r = await db.query<RawSeason>('SELECT * FROM boss_seasons WHERE kind = $1 AND week_start = $2', [kind, weekStart]);
  return r.rows[0] ? toSeason(r.rows[0]) : null;
}

export async function seasonById(db: Queryable, id: number): Promise<Season | null> {
  const r = await db.query<RawSeason>('SELECT * FROM boss_seasons WHERE id = $1', [id]);
  return r.rows[0] ? toSeason(r.rows[0]) : null;
}

/** 월드보스 공유 체력에서 깎는다. 처음 0이 되는 순간 격파 시각을 남긴다. 정산이 끝난 시즌은 건드리지 않고 그대로 돌려준다 */
export async function hitWorld(client: PoolClient, seasonId: number, damage: number, now: Date): Promise<Season> {
  const r = await client.query<RawSeason>(
    `UPDATE boss_seasons SET hp_left = GREATEST(0, hp_left - $2),
            defeated_at = CASE WHEN defeated_at IS NULL AND hp_left - $2 <= 0 THEN $3 ELSE defeated_at END
      WHERE id = $1 AND settled_at IS NULL RETURNING *`,
    [seasonId, damage, now],
  );
  if (r.rows[0]) return toSeason(r.rows[0]);
  return await seasonById(client, seasonId) as Season;
}

// ---------- 길드보스 점수 ----------

export interface GuildScore {
  season_id: number;
  guild_id: number;
  score: number;
  scored_at: Date;
}
type RawScore = { season_id: string; guild_id: string; score: string; scored_at: Date };
const toScore = (r: RawScore): GuildScore => ({ season_id: Number(r.season_id), guild_id: Number(r.guild_id), score: Number(r.score), scored_at: r.scored_at });

/** 길드 점수에 더한다(원자적 UPSERT). 0보다 클 때만 부른다 */
export async function addGuildScore(client: PoolClient, seasonId: number, guildId: number, add: number, now: Date): Promise<GuildScore> {
  const r = await client.query<RawScore>(
    `INSERT INTO guild_boss_scores (season_id, guild_id, score, scored_at) VALUES ($1, $2, $3, $4)
     ON CONFLICT (season_id, guild_id) DO UPDATE SET score = guild_boss_scores.score + EXCLUDED.score, scored_at = EXCLUDED.scored_at
     RETURNING *`,
    [seasonId, guildId, add, now],
  );
  return toScore(r.rows[0] as RawScore);
}

export async function guildScore(db: Queryable, seasonId: number, guildId: number): Promise<number> {
  const r = await db.query<{ score: string }>('SELECT score FROM guild_boss_scores WHERE season_id = $1 AND guild_id = $2', [seasonId, guildId]);
  return r.rows[0] ? Number(r.rows[0].score) : 0;
}

export async function scoresOf(db: Queryable, seasonId: number): Promise<GuildScore[]> {
  const r = await db.query<RawScore>('SELECT * FROM guild_boss_scores WHERE season_id = $1', [seasonId]);
  return r.rows.map(toScore);
}

/** 이번 주 이 캐릭터가 이 길드 점수에 보탠 인정 피해 합 */
export async function contributionOf(db: Queryable, seasonId: number, characterId: number, guildId: number): Promise<number> {
  const r = await db.query<{ total: string | null }>(
    `SELECT sum(damage_accepted) AS total FROM boss_attempts WHERE season_id = $1 AND character_id = $2 AND guild_id = $3 AND state = 'finished'`,
    [seasonId, characterId, guildId],
  );
  return Number(r.rows[0]?.total ?? 0);
}

// ---------- 도전 ----------

export interface Attempt {
  id: number;
  uuid: string;
  character_id: number;
  season_id: number;
  kind: BossKind;
  guild_id: number | null;
  reset_day: Date;
  state: 'open' | 'finished' | 'expired';
  started_at: Date;
  ends_at: Date;
  finished_at: Date | null;
  damage_reported: number;
  damage_accepted: number;
  capped: boolean;
}
type RawAttempt = Omit<Attempt, 'id' | 'character_id' | 'season_id' | 'guild_id' | 'damage_reported' | 'damage_accepted'> & {
  id: string; character_id: string; season_id: string; guild_id: string | null; damage_reported: string; damage_accepted: string;
};
const toAttempt = (r: RawAttempt): Attempt => ({
  ...r, id: Number(r.id), character_id: Number(r.character_id), season_id: Number(r.season_id),
  guild_id: r.guild_id === null ? null : Number(r.guild_id), damage_reported: Number(r.damage_reported), damage_accepted: Number(r.damage_accepted),
});

/** 끝 보고 기한이 지난 열린 도전을 닫는다(피해 0, 횟수는 이미 썼다) */
export async function expireStale(client: PoolClient, characterId: number, before: Date, now: Date): Promise<void> {
  await client.query(
    "UPDATE boss_attempts SET state = 'expired', finished_at = $3 WHERE character_id = $1 AND state = 'open' AND ends_at < $2",
    [characterId, before, now],
  );
}

export async function openAttempt(db: Queryable, characterId: number, kind: BossKind): Promise<Attempt | null> {
  const r = await db.query<RawAttempt>("SELECT * FROM boss_attempts WHERE character_id = $1 AND kind = $2 AND state = 'open'", [characterId, kind]);
  return r.rows[0] ? toAttempt(r.rows[0]) : null;
}

export async function countToday(db: Queryable, characterId: number, kind: BossKind, resetDay: Date): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM boss_attempts WHERE character_id = $1 AND kind = $2 AND reset_day = $3', [characterId, kind, resetDay]);
  return Number(r.rows[0]?.n ?? 0);
}

export async function insertAttempt(client: PoolClient, a: {
  characterId: number; seasonId: number; kind: BossKind; guildId: number | null; resetDay: Date; partySize: number; now: Date; endsAt: Date;
}): Promise<Attempt> {
  const r = await client.query<RawAttempt>(
    `INSERT INTO boss_attempts (character_id, season_id, kind, guild_id, reset_day, party_size, started_at, ends_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8) RETURNING *`,
    [a.characterId, a.seasonId, a.kind, a.guildId, a.resetDay, a.partySize, a.now, a.endsAt],
  );
  return toAttempt(r.rows[0] as RawAttempt);
}

export async function lockAttempt(client: PoolClient, characterId: number, kind: BossKind, uuid: string): Promise<Attempt | null> {
  const r = await client.query<RawAttempt>('SELECT * FROM boss_attempts WHERE character_id = $1 AND kind = $2 AND uuid = $3 FOR UPDATE', [characterId, kind, uuid]);
  return r.rows[0] ? toAttempt(r.rows[0]) : null;
}

/** 시작 뒤 길드를 떠난 도전: 어느 길드 점수에도 들어가지 않게 길드를 뗀다(기록·참가 보상은 남는다) */
export async function detachAttemptGuild(client: PoolClient, id: number): Promise<void> {
  await client.query('UPDATE boss_attempts SET guild_id = NULL WHERE id = $1', [id]);
}

export async function closeAttempt(client: PoolClient, id: number, state: 'finished' | 'expired', reported: number, accepted: number, capped: boolean, now: Date): Promise<void> {
  await client.query(
    'UPDATE boss_attempts SET state = $2, damage_reported = $3, damage_accepted = $4, capped = $5, finished_at = $6 WHERE id = $1',
    [id, state, reported, accepted, capped, now],
  );
}

/** 이번 시즌 내 가장 높은 1회 피해 */
export async function bestOf(db: Queryable, seasonId: number, characterId: number): Promise<number> {
  const r = await db.query<{ n: string | null }>("SELECT max(damage_accepted) AS n FROM boss_attempts WHERE season_id = $1 AND character_id = $2 AND state = 'finished'", [seasonId, characterId]);
  return Number(r.rows[0]?.n ?? 0);
}

/** 월드보스 순위: 1회 최고 피해 내림차순, 같으면 그 기록을 먼저 낸 사람 */
export interface WorldEntry { character_id: number; best: number; at: Date }
export async function worldBoard(db: Queryable, seasonId: number): Promise<WorldEntry[]> {
  const r = await db.query<{ character_id: string; best: string; at: Date }>(
    `SELECT DISTINCT ON (a.character_id) a.character_id, a.damage_accepted AS best, a.finished_at AS at
       FROM boss_attempts a JOIN characters c ON c.id = a.character_id AND c.deleted_at IS NULL
      WHERE a.season_id = $1 AND a.state = 'finished' AND a.damage_accepted > 0
      ORDER BY a.character_id, a.damage_accepted DESC, a.finished_at`,
    [seasonId],
  );
  return r.rows
    .map((x) => ({ character_id: Number(x.character_id), best: Number(x.best), at: x.at }))
    .sort((a, b) => b.best - a.best || a.at.getTime() - b.at.getTime() || a.character_id - b.character_id);
}

/** 도전을 1번 이상 끝낸 캐릭터(정산 참가 보상) */
export async function participants(db: Queryable, seasonId: number): Promise<{ character_id: number; guild_id: number | null }[]> {
  const r = await db.query<{ character_id: string; guild_id: string | null }>(
    `SELECT DISTINCT a.character_id, a.guild_id FROM boss_attempts a JOIN characters c ON c.id = a.character_id AND c.deleted_at IS NULL
      WHERE a.season_id = $1 AND a.state = 'finished'`,
    [seasonId],
  );
  return r.rows.map((x) => ({ character_id: Number(x.character_id), guild_id: x.guild_id === null ? null : Number(x.guild_id) }));
}

/** 정산할 시즌(주가 끝났고 아직 정산 안 함)을 잠근다. 다른 프로세스가 잡은 행은 건너뛴다 */
export async function lockDueSeason(client: PoolClient, now: Date): Promise<Season | null> {
  const r = await client.query<RawSeason>(
    'SELECT * FROM boss_seasons WHERE week_end <= $1 AND settled_at IS NULL ORDER BY week_end, id LIMIT 1 FOR UPDATE SKIP LOCKED',
    [now],
  );
  return r.rows[0] ? toSeason(r.rows[0]) : null;
}

export async function markSettled(client: PoolClient, id: number, now: Date): Promise<void> {
  await client.query('UPDATE boss_seasons SET settled_at = $2 WHERE id = $1', [id, now]);
}

// ---------- 랭킹 칭호 ----------

export async function grantTitle(client: PoolClient, characterId: number, seasonId: number, titleId: string, color: string | null, expiresAt: Date): Promise<void> {
  await client.query(
    `INSERT INTO rank_titles (character_id, season_id, title_id, name_color, expires_at) VALUES ($1, $2, $3, $4, $5)
     ON CONFLICT (character_id, season_id, title_id) DO NOTHING`,
    [characterId, seasonId, titleId, color, expiresAt],
  );
}

export async function activeTitles(db: Queryable, characterId: number, now: Date): Promise<{ title_id: string; name_color: string | null; expires_at: Date }[]> {
  const r = await db.query<{ title_id: string; name_color: string | null; expires_at: Date }>(
    'SELECT title_id, name_color, expires_at FROM rank_titles WHERE character_id = $1 AND expires_at > $2 ORDER BY expires_at DESC',
    [characterId, now],
  );
  return r.rows;
}

/** 만료된 랭킹 칭호를 장착한 캐릭터는 칭호를 뗀다 */
export async function unequipExpiredTitles(client: PoolClient, now: Date): Promise<number> {
  const r = await client.query(
    `UPDATE characters c SET title_achievement = NULL
      WHERE c.title_achievement LIKE 'rank\\_%'
        AND NOT EXISTS (SELECT 1 FROM rank_titles t WHERE t.character_id = c.id AND t.title_id = c.title_achievement AND t.expires_at > $1)`,
    [now],
  );
  return r.rowCount ?? 0;
}

export async function setGuildHonor(client: PoolClient, guildId: number, color: 'gold' | 'silver', until: Date): Promise<void> {
  await client.query('UPDATE guilds SET honor_color = $2, honor_until = $3 WHERE id = $1', [guildId, color, until]);
}
