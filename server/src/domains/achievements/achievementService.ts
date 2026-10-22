import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as charRepo from '../characters/characterRepository';
import { ACHIEVEMENTS, ACHIEVEMENT_BY_ID } from './achievementDefs';
import { chapter3Open } from '../../gamedata/chapterGate';
import * as repo from './achievementRepository';
import { activeTitles } from '../bossrush/bossRushRepository';
import { RANK_TITLE_NAMES, isRankTitle } from '../bossrush/bossRushRules';

async function myCharacter(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

/** 칭호 이름(장착한 업적 id -> 업적 이름). 없으면 null */
export function titleName(achievementId: string | null): string | null {
  if (!achievementId) return null;
  // 0047: 주간 랭킹 칭호(rank_*)도 같은 자리에 장착한다. 만료되면 매일 작업과 업적 조회가 떼어 낸다
  if (isRankTitle(achievementId)) return RANK_TITLE_NAMES[achievementId];
  return ACHIEVEMENT_BY_ID.get(achievementId)?.title ?? null;
}

/**
 * 업적 목록과 진행도. 부를 때마다 기록으로 다시 판정하고, 새로 달성한 것은 저장한 뒤 newly에 담아 돌려준다
 * (클라이언트는 강화·레벨 업·클리어 뒤에 불러 달성 알림을 띄운다).
 */
export async function listAchievements(accountId: number, characterUuid: string) {
  const c = await myCharacter(accountId, characterUuid);
  const db = getPool();
  const stats = await repo.statsOf(db, c.id);
  const done = await repo.achievedOf(db, c.id);
  const newly: string[] = [];
  // 챕터 3 업적은 3장이 열릴 때까지 목록·판정에서 뺀다
  const visible = ACHIEVEMENTS.filter((a) => (a.chapter ?? 0) < 3 || chapter3Open());
  for (const a of visible) if (!done.has(a.id) && stats[a.stat] >= a.goal) newly.push(a.id);
  const now = getNow();
  if (newly.length > 0) {
    await repo.insertAchieved(db, c.id, newly, now);
    for (const id of newly) done.set(id, now);
  }
  let title = await repo.titleOf(db, c.id);
  const ranks = await activeTitles(db, c.id, now);
  // 만료된 랭킹 칭호가 아직 장착돼 있으면(매일 작업이 떼기 전) 빈 것으로 보여 주고 바로 뗀다
  if (title && isRankTitle(title) && !ranks.some((t) => t.title_id === title)) {
    await repo.setTitle(db, c.id, null);
    title = null;
  }
  return {
    title: title ? { id: title, name: titleName(title) } : null,
    // 지금 장착할 수 있는 랭킹 칭호(이름 색은 장착과 무관하게 유효 기간 동안 적용된다)
    rank_titles: ranks.map((t) => ({ id: t.title_id, title: titleName(t.title_id), name_color: t.name_color, expires_at: t.expires_at.toISOString() })),
    newly,
    achievements: visible.map((a) => ({
      id: a.id,
      title: a.title,
      description: a.description,
      category: a.category,
      goal: a.goal,
      progress: Math.min(stats[a.stat], a.goal),
      achieved_at: done.get(a.id)?.toISOString() ?? null,
    })),
  };
}

/** 칭호 장착(달성한 업적만) 또는 해제(null) */
export async function equipTitle(accountId: number, characterUuid: string, achievementId: string | null) {
  const c = await myCharacter(accountId, characterUuid);
  const db = getPool();
  if (achievementId !== null && isRankTitle(achievementId)) {
    const ranks = await activeTitles(db, c.id, getNow());
    if (!ranks.some((t) => t.title_id === achievementId)) throw new AppError(409, '지금 가진 랭킹 칭호가 아닙니다.', 'ACHIEVEMENT_NOT_DONE');
  } else if (achievementId !== null) {
    if (!ACHIEVEMENT_BY_ID.has(achievementId)) throw new AppError(404, '알 수 없는 업적입니다.', 'ACHIEVEMENT_UNKNOWN');
    const done = await repo.achievedOf(db, c.id);
    if (!done.has(achievementId)) throw new AppError(409, '아직 달성하지 않은 업적입니다.', 'ACHIEVEMENT_NOT_DONE');
  }
  await repo.setTitle(db, c.id, achievementId);
  return { title: achievementId ? { id: achievementId, name: titleName(achievementId) } : null };
}
