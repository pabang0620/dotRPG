// 길드 SQL. 쓰기는 호출하는 Service가 행동하는 캐릭터 행 -> 길드 행 순서로 잠근 뒤에만 부른다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import type { GuildRole, JoinMode } from './guildRules';

export interface GuildRow {
  id: number;
  uuid: string;
  name: string;
  notice: string;
  join_mode: JoinMode;
  leader_character_id: number;
  member_count: number;
  honor_color: 'gold' | 'silver' | null;
  honor_until: Date | null;
  created_at: Date;
  disbanded_at: Date | null;
}
type RawGuild = Omit<GuildRow, 'id' | 'leader_character_id'> & { id: string; leader_character_id: string };
const toGuild = (r: RawGuild): GuildRow => ({ ...r, id: Number(r.id), leader_character_id: Number(r.leader_character_id) });

export interface Membership {
  guild_id: number;
  role: GuildRole;
  joined_at: Date;
}

export async function membershipOf(db: Queryable, characterId: number): Promise<Membership | null> {
  const r = await db.query<{ guild_id: string; role: GuildRole; joined_at: Date }>(
    'SELECT guild_id, role, joined_at FROM guild_members WHERE character_id = $1',
    [characterId],
  );
  const row = r.rows[0];
  return row ? { guild_id: Number(row.guild_id), role: row.role, joined_at: row.joined_at } : null;
}

export async function byId(db: Queryable, id: number): Promise<GuildRow | null> {
  const r = await db.query<RawGuild>('SELECT * FROM guilds WHERE id = $1', [id]);
  return r.rows[0] ? toGuild(r.rows[0]) : null;
}

export async function lockAlive(client: PoolClient, id: number): Promise<GuildRow | null> {
  const r = await client.query<RawGuild>('SELECT * FROM guilds WHERE id = $1 AND disbanded_at IS NULL FOR UPDATE', [id]);
  return r.rows[0] ? toGuild(r.rows[0]) : null;
}

export async function lockAliveByUuid(client: PoolClient, uuid: string): Promise<GuildRow | null> {
  const r = await client.query<RawGuild>('SELECT * FROM guilds WHERE uuid = $1 AND disbanded_at IS NULL FOR UPDATE', [uuid]);
  return r.rows[0] ? toGuild(r.rows[0]) : null;
}

/** 이름이 겹치면 UNIQUE(guilds_name_alive) 위반으로 던진다 */
export async function insertGuild(client: PoolClient, g: { name: string; notice: string; joinMode: JoinMode; leaderId: number; now: Date }): Promise<GuildRow> {
  const r = await client.query<RawGuild>(
    `INSERT INTO guilds (name, notice, join_mode, leader_character_id, member_count, created_at)
     VALUES ($1, $2, $3, $4, 1, $5) RETURNING *`,
    [g.name, g.notice, g.joinMode, g.leaderId, g.now],
  );
  return toGuild(r.rows[0] as RawGuild);
}

export async function insertMember(client: PoolClient, guildId: number, characterId: number, role: GuildRole, now: Date): Promise<void> {
  await client.query('INSERT INTO guild_members (character_id, guild_id, role, joined_at) VALUES ($1, $2, $3, $4)', [characterId, guildId, role, now]);
}

export async function deleteMember(client: PoolClient, characterId: number): Promise<void> {
  await client.query('DELETE FROM guild_members WHERE character_id = $1', [characterId]);
}

export async function setRole(client: PoolClient, characterId: number, role: GuildRole): Promise<void> {
  await client.query('UPDATE guild_members SET role = $2 WHERE character_id = $1', [characterId, role]);
}

export async function addCount(client: PoolClient, guildId: number, delta: number): Promise<void> {
  await client.query('UPDATE guilds SET member_count = member_count + $2 WHERE id = $1', [guildId, delta]);
}

export async function setLeader(client: PoolClient, guildId: number, characterId: number): Promise<void> {
  await client.query('UPDATE guilds SET leader_character_id = $2 WHERE id = $1', [guildId, characterId]);
}

export async function updateSettings(client: PoolClient, guildId: number, notice: string | undefined, joinMode: JoinMode | undefined): Promise<void> {
  await client.query(
    'UPDATE guilds SET notice = COALESCE($2, notice), join_mode = COALESCE($3, join_mode) WHERE id = $1',
    [guildId, notice ?? null, joinMode ?? null],
  );
}

export async function disband(client: PoolClient, guildId: number, now: Date): Promise<void> {
  await client.query('DELETE FROM guild_applications WHERE guild_id = $1', [guildId]);
  await client.query('UPDATE guilds SET disbanded_at = $2, member_count = 0 WHERE id = $1', [guildId, now]);
}

export async function officerCount(db: Queryable, guildId: number): Promise<number> {
  const r = await db.query<{ n: string }>("SELECT count(*) AS n FROM guild_members WHERE guild_id = $1 AND role = 'officer'", [guildId]);
  return Number(r.rows[0]?.n ?? 0);
}

export interface MemberView {
  character_id: number;
  uuid: string;
  name: string;
  level: number;
  career: number;
  role: GuildRole;
  joined_at: Date;
  last_login_at: Date | null;
}

export async function members(db: Queryable, guildId: number): Promise<MemberView[]> {
  const r = await db.query<Omit<MemberView, 'character_id'> & { character_id: string }>(
    `SELECT m.character_id, c.uuid, c.name, c.level, COALESCE(cc.career, 0) AS career, m.role, m.joined_at, a.last_login_at
       FROM guild_members m
       JOIN characters c ON c.id = m.character_id AND c.deleted_at IS NULL
       JOIN accounts a ON a.id = c.account_id
       LEFT JOIN character_career cc ON cc.character_id = c.id
      WHERE m.guild_id = $1
      ORDER BY CASE m.role WHEN 'leader' THEN 0 WHEN 'officer' THEN 1 ELSE 2 END, m.joined_at`,
    [guildId],
  );
  return r.rows.map((x) => ({ ...x, character_id: Number(x.character_id) }));
}

/** 같은 길드의 살아 있는 멤버 캐릭터 id(채팅 길드 채널 전달) */
export async function memberIds(db: Queryable, guildId: number): Promise<number[]> {
  const r = await db.query<{ character_id: string }>('SELECT character_id FROM guild_members WHERE guild_id = $1', [guildId]);
  return r.rows.map((x) => Number(x.character_id));
}

/** 길드장이 사라질 때 넘겨받을 사람: 가장 오래된 부길드장, 없으면 가장 오래된 길드원(살아 있는 캐릭터만) */
export async function successorOf(db: Queryable, guildId: number, exceptCharacterId: number): Promise<number | null> {
  const r = await db.query<{ character_id: string }>(
    `SELECT m.character_id FROM guild_members m JOIN characters c ON c.id = m.character_id AND c.deleted_at IS NULL
      WHERE m.guild_id = $1 AND m.character_id <> $2
      ORDER BY CASE m.role WHEN 'officer' THEN 0 ELSE 1 END, m.joined_at, m.character_id LIMIT 1`,
    [guildId, exceptCharacterId],
  );
  return r.rows[0] ? Number(r.rows[0].character_id) : null;
}

export async function memberByUuid(db: Queryable, guildId: number, characterUuid: string): Promise<{ id: number; role: GuildRole } | null> {
  const r = await db.query<{ id: string; role: GuildRole }>(
    `SELECT c.id, m.role FROM guild_members m JOIN characters c ON c.id = m.character_id
      WHERE m.guild_id = $1 AND c.uuid = $2`,
    [guildId, characterUuid],
  );
  const row = r.rows[0];
  return row ? { id: Number(row.id), role: row.role } : null;
}

// ---------- 신청 ----------

export interface ApplicationView {
  id: number;
  uuid: string;
  character_id: number;
  character_uuid: string;
  name: string;
  level: number;
  career: number;
  created_at: Date;
}

export async function applicationsOf(db: Queryable, guildId: number): Promise<ApplicationView[]> {
  const r = await db.query<Omit<ApplicationView, 'id' | 'character_id'> & { id: string; character_id: string }>(
    `SELECT g.id, g.uuid, g.character_id, c.uuid AS character_uuid, c.name, c.level, COALESCE(cc.career, 0) AS career, g.created_at
       FROM guild_applications g
       JOIN characters c ON c.id = g.character_id AND c.deleted_at IS NULL
       LEFT JOIN character_career cc ON cc.character_id = c.id
      WHERE g.guild_id = $1 ORDER BY g.created_at`,
    [guildId],
  );
  return r.rows.map((x) => ({ ...x, id: Number(x.id), character_id: Number(x.character_id) }));
}

export async function myApplications(db: Queryable, characterId: number): Promise<{ guild_uuid: string; guild_name: string; created_at: Date }[]> {
  const r = await db.query<{ guild_uuid: string; guild_name: string; created_at: Date }>(
    `SELECT g.uuid AS guild_uuid, g.name AS guild_name, a.created_at
       FROM guild_applications a JOIN guilds g ON g.id = a.guild_id
      WHERE a.character_id = $1 AND g.disbanded_at IS NULL ORDER BY a.created_at`,
    [characterId],
  );
  return r.rows;
}

export async function countPending(db: Queryable, characterId: number): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM guild_applications WHERE character_id = $1', [characterId]);
  return Number(r.rows[0]?.n ?? 0);
}

export async function insertApplication(client: PoolClient, guildId: number, characterId: number, now: Date): Promise<void> {
  await client.query(
    'INSERT INTO guild_applications (guild_id, character_id, created_at) VALUES ($1, $2, $3) ON CONFLICT (guild_id, character_id) DO NOTHING',
    [guildId, characterId, now],
  );
}

export async function lockApplication(client: PoolClient, guildId: number, uuid: string): Promise<{ id: number; character_id: number } | null> {
  const r = await client.query<{ id: string; character_id: string }>(
    'SELECT id, character_id FROM guild_applications WHERE guild_id = $1 AND uuid = $2 FOR UPDATE',
    [guildId, uuid],
  );
  const row = r.rows[0];
  return row ? { id: Number(row.id), character_id: Number(row.character_id) } : null;
}

export async function deleteApplication(client: PoolClient, id: number): Promise<void> {
  await client.query('DELETE FROM guild_applications WHERE id = $1', [id]);
}

export async function deleteApplicationsOf(client: PoolClient, characterId: number): Promise<void> {
  await client.query('DELETE FROM guild_applications WHERE character_id = $1', [characterId]);
}

// ---------- 쿨다운 ----------

export async function cooldownUntil(db: Queryable, characterId: number, now: Date): Promise<Date | null> {
  const r = await db.query<{ until: Date }>('SELECT until FROM guild_cooldowns WHERE character_id = $1 AND until > $2', [characterId, now]);
  return r.rows[0]?.until ?? null;
}

export async function setCooldown(client: PoolClient, characterId: number, until: Date): Promise<void> {
  await client.query(
    `INSERT INTO guild_cooldowns (character_id, until) VALUES ($1, $2)
     ON CONFLICT (character_id) DO UPDATE SET until = GREATEST(guild_cooldowns.until, EXCLUDED.until)`,
    [characterId, until],
  );
}

// ---------- 검색 ----------

export async function search(db: Queryable, query: string, page: number, size: number): Promise<{ rows: (GuildRow & { leader_name: string })[]; total: number }> {
  const like = `%${query.replace(/[\\%_]/g, (c) => `\\${c}`)}%`;
  const total = await db.query<{ n: string }>(
    "SELECT count(*) AS n FROM guilds WHERE disbanded_at IS NULL AND ($1 = '' OR name ILIKE $2)",
    [query, like],
  );
  const r = await db.query<RawGuild & { leader_name: string }>(
    `SELECT g.*, c.name AS leader_name FROM guilds g JOIN characters c ON c.id = g.leader_character_id
      WHERE g.disbanded_at IS NULL AND ($1 = '' OR g.name ILIKE $2)
      ORDER BY g.member_count DESC, g.created_at LIMIT $3 OFFSET $4`,
    [query, like, size, (page - 1) * size],
  );
  return { rows: r.rows.map((x) => ({ ...toGuild(x), leader_name: x.leader_name })), total: Number(total.rows[0]?.n ?? 0) };
}

/** 이름표·채팅용: 캐릭터의 길드 이름과 길드 꾸미기 색(유효할 때만) */
export async function displayOf(db: Queryable, characterId: number, now: Date): Promise<{ guild_name: string | null; guild_color: 'gold' | 'silver' | null }> {
  const r = await db.query<{ name: string; honor_color: 'gold' | 'silver' | null; honor_until: Date | null }>(
    `SELECT g.name, g.honor_color, g.honor_until FROM guild_members m JOIN guilds g ON g.id = m.guild_id
      WHERE m.character_id = $1 AND g.disbanded_at IS NULL`,
    [characterId],
  );
  const row = r.rows[0];
  if (!row) return { guild_name: null, guild_color: null };
  return { guild_name: row.name, guild_color: row.honor_until && row.honor_until > now ? row.honor_color : null };
}
