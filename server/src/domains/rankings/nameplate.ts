// 이름표·채팅에 붙는 꾸미기: 길드 이름, 길드 이름 색(길드보스 랭킹), 캐릭터 이름 색과 칭호(장착한 랭킹 칭호)
import type { Queryable } from '../../db/pool';
import { titleOf } from '../achievements/achievementRepository';
import { titleName } from '../achievements/achievementService';
import { activeTitles } from '../bossrush/bossRushRepository';
import { isRankTitle, strongestColor } from '../bossrush/bossRushRules';
import { displayOf } from '../guilds/guildRepository';

export interface Nameplate {
  guild_name: string | null;
  guild_color: 'gold' | 'silver' | null;
  name_color: 'gold' | 'orange' | null;
  /** 장착한 칭호 id(업적 또는 지금 가진 랭킹 칭호). 머리 위 칭호 줄·랭킹 마크 표시용. 없으면 null */
  title_id: string | null;
  /** 그 칭호의 이름(서버 한국어 원문, 클라이언트가 번역표로 바꾼다) */
  title: string | null;
}

/**
 * 이름 색은 장착한 랭킹 칭호를 따른다(2026-10-30: 월드보스 칭호가 영구가 되며 "가진 것 전부"가 아니라 "장착한 것"의 색).
 * 기한이 지난 랭킹 칭호(길드보스 최강 길드)는 장착돼 있어도 색·칭호를 싣지 않는다(매일 작업이 뗀다)
 */
export async function nameplateOf(db: Queryable, characterId: number, now: Date): Promise<Nameplate> {
  const g = await displayOf(db, characterId, now);
  let titleId = await titleOf(db, characterId);
  let color: 'gold' | 'orange' | null = null;
  if (titleId && isRankTitle(titleId)) {
    const own = (await activeTitles(db, characterId, now)).filter((t) => t.title_id === titleId);
    if (own.length === 0) titleId = null;
    else color = strongestColor(own.map((t) => t.name_color));
  }
  return { guild_name: g.guild_name, guild_color: g.guild_color, name_color: color, title_id: titleId, title: titleName(titleId) };
}
