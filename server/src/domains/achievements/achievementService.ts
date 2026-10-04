import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as charRepo from '../characters/characterRepository';
import { ACHIEVEMENTS, ACHIEVEMENT_BY_ID } from './achievementDefs';
import * as repo from './achievementRepository';

async function myCharacter(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return c;
}

/** 칭호 이름(장착한 업적 id -> 업적 이름). 없으면 null */
export function titleName(achievementId: string | null): string | null {
  return achievementId ? (ACHIEVEMENT_BY_ID.get(achievementId)?.title ?? null) : null;
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
  for (const a of ACHIEVEMENTS) if (!done.has(a.id) && stats[a.stat] >= a.goal) newly.push(a.id);
  const now = getNow();
  if (newly.length > 0) {
    await repo.insertAchieved(db, c.id, newly, now);
    for (const id of newly) done.set(id, now);
  }
  const title = await repo.titleOf(db, c.id);
  return {
    title: title ? { id: title, name: titleName(title) } : null,
    newly,
    achievements: ACHIEVEMENTS.map((a) => ({
      id: a.id,
      title: a.title,
      description: a.description,
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
  if (achievementId !== null) {
    if (!ACHIEVEMENT_BY_ID.has(achievementId)) throw new AppError(404, '알 수 없는 업적입니다.', 'ACHIEVEMENT_UNKNOWN');
    const done = await repo.achievedOf(db, c.id);
    if (!done.has(achievementId)) throw new AppError(409, '아직 달성하지 않은 업적입니다.', 'ACHIEVEMENT_NOT_DONE');
  }
  await repo.setTitle(db, c.id, achievementId);
  return { title: achievementId ? { id: achievementId, name: titleName(achievementId) } : null };
}
