// 이름표·채팅에 붙는 꾸미기: 길드 이름, 길드 이름 색(길드보스 랭킹), 캐릭터 이름 색(월드보스 랭킹 칭호)
import type { Queryable } from '../../db/pool';
import { activeTitles } from '../bossrush/bossRushRepository';
import { strongestColor } from '../bossrush/bossRushRules';
import { displayOf } from '../guilds/guildRepository';

export interface Nameplate {
  guild_name: string | null;
  guild_color: 'gold' | 'silver' | null;
  name_color: 'gold' | 'orange' | null;
}

export async function nameplateOf(db: Queryable, characterId: number, now: Date): Promise<Nameplate> {
  const g = await displayOf(db, characterId, now);
  const titles = await activeTitles(db, characterId, now);
  return { guild_name: g.guild_name, guild_color: g.guild_color, name_color: strongestColor(titles.map((t) => t.name_color)) };
}
