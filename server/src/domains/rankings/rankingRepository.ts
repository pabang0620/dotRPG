// 랭킹 조회 SQL(읽기 전용)
import type { Queryable } from '../../db/pool';

export interface CharDisplay {
  id: number;
  name: string;
  level: number;
  career: number;
  guild_name: string | null;
  name_color: 'gold' | 'orange' | null;
}

/** 표시 정보(이름·레벨·전직·길드·이름 색). 이름 색은 지금 유효한 랭킹 칭호 중 가장 강한 색 */
export async function displays(db: Queryable, ids: number[], now: Date): Promise<Map<number, CharDisplay>> {
  if (ids.length === 0) return new Map();
  const r = await db.query<{ id: string; name: string; level: number; career: number; guild_name: string | null; colors: (string | null)[] | null }>(
    `SELECT c.id, c.name, c.level, COALESCE(cc.career, 0) AS career,
            (SELECT g.name FROM guild_members m JOIN guilds g ON g.id = m.guild_id AND g.disbanded_at IS NULL WHERE m.character_id = c.id) AS guild_name,
            (SELECT array_agg(t.name_color) FROM rank_titles t WHERE t.character_id = c.id AND t.expires_at > $2) AS colors
       FROM characters c LEFT JOIN character_career cc ON cc.character_id = c.id
      WHERE c.id = ANY($1::bigint[])`,
    [ids, now],
  );
  const out = new Map<number, CharDisplay>();
  for (const x of r.rows) {
    const colors = x.colors ?? [];
    out.set(Number(x.id), {
      id: Number(x.id), name: x.name, level: x.level, career: x.career, guild_name: x.guild_name,
      name_color: colors.includes('gold') ? 'gold' : colors.includes('orange') ? 'orange' : null,
    });
  }
  return out;
}

export interface PowerRow { id: number; class: string; level: number; worn: string[]; bonus_hp: number }

/** 전투력 랭킹 후보: Lv20 이상 살아 있는 캐릭터와 착용 장비·퀘스트 체력 보너스 */
export async function powerCandidates(db: Queryable, minLevel: number): Promise<PowerRow[]> {
  const r = await db.query<{ id: string; class: string; level: number; worn: string[] | null; bonus_hp: string | null }>(
    `SELECT c.id, c.class, c.level,
            (SELECT array_agg(i.item_key) FROM character_items i WHERE i.character_id = c.id AND i.location = 'worn') AS worn,
            (SELECT sum((q.reward->>'max_health')::int) FROM quest_claims q WHERE q.character_id = c.id) AS bonus_hp
       FROM characters c WHERE c.deleted_at IS NULL AND c.level >= $1`,
    [minLevel],
  );
  return r.rows.map((x) => ({ id: Number(x.id), class: x.class, level: x.level, worn: x.worn ?? [], bonus_hp: Number(x.bonus_hp ?? 0) }));
}

export async function guildNames(db: Queryable, ids: number[]): Promise<Map<number, string>> {
  if (ids.length === 0) return new Map();
  const r = await db.query<{ id: string; name: string }>('SELECT id, name FROM guilds WHERE id = ANY($1::bigint[])', [ids]);
  return new Map(r.rows.map((x) => [Number(x.id), x.name]));
}
