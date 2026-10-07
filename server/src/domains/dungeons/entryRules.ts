// 던전 입장 자격(checkEntry): 솔로 입장, 파티 생성·신청·대기·출발·시작이 모두 이 한 함수를 쓴다.
// C# 원본: DungeonDirector.CannotEnterReason, RaidLockReason, DungeonProgress.RaidRewardAvailable
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import type { DungeonDef } from '../../gamedata/economyData';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { stackCount } from '../economy/economyRepository';
import * as repo from './dungeonRepository';
import { diffOf, isOpenToday, type DiffNumbers } from './dungeonRules';

/** 퀘스트 상태 Active(2) 이상 = 수락 이상 */
const QUEST_ACTIVE = 2;

export interface EntryChar {
  id: number;
  level: number;
  class: string;
}

export interface EntryInfo {
  dungeon: DungeonDef;
  diff: DiffNumbers;
  resetDay: Date;
  countsEntry: boolean;
  used: number;
  limit: number;
}

/** 보상 기간: 중간 레이드 일일, 최종 레이드 주간(resetBoundaries 한 함수) */
export function raidPeriod(d: DungeonDef, now: Date): { kind: 'daily' | 'weekly'; start: Date } {
  const b = resetBoundaries(now);
  return d.raidTier === 'Final'
    ? { kind: 'weekly', start: new Date(b.weeklyStartAt) }
    : { kind: 'daily', start: new Date(b.dailyStartAt) };
}

/** 레이드 해금: 해금 퀘스트 청구, 또는 선행 퀘스트를 전부 청구하고 저장 상태에서 수락 이상(10.2) */
export async function raidUnlocked(db: Queryable, characterId: number, d: DungeonDef): Promise<boolean> {
  if (!d.unlockQuest) return true;
  const claims = await db.query<{ quest_id: string }>('SELECT quest_id FROM quest_claims WHERE character_id = $1', [
    characterId,
  ]);
  const claimed = new Set(claims.rows.map((r) => r.quest_id));
  if (claimed.has(d.unlockQuest)) return true;
  const rule = getGameData().economy.quests.get(d.unlockQuest);
  const requires = rule ? rule.requires : [];
  if (!requires.every((q) => claimed.has(q))) return false;
  const st = await db.query<{ quests: { id: string; status: number }[] }>(
    'SELECT quests FROM character_state WHERE character_id = $1',
    [characterId],
  );
  const saved = st.rows[0]?.quests ?? [];
  return saved.some((q) => q.id === d.unlockQuest && q.status >= QUEST_ACTIVE);
}

export async function raidClaimed(db: Queryable, characterId: number, d: DungeonDef, now: Date): Promise<boolean> {
  const period = raidPeriod(d, now);
  const r = await db.query(
    'SELECT 1 FROM raid_claims WHERE character_id = $1 AND dungeon_id = $2 AND period_start = $3',
    [characterId, d.id, period.start],
  );
  return r.rows.length > 0;
}

export async function checkEntry(
  db: Queryable,
  c: EntryChar,
  dungeonId: string,
  difficulty: number,
  now: Date,
): Promise<EntryInfo> {
  const eco = getGameData().economy;
  const slack = getConfig().policy.partyMinLevelSlack;
  const d = eco.dungeons.byId.get(dungeonId);
  if (!d) throw new AppError(422, '알 수 없는 던전입니다.', 'DUNGEON_UNKNOWN');
  if (!isOpenToday(eco, d, now)) throw new AppError(422, '오늘은 열리지 않는 던전입니다.', 'DUNGEON_CLOSED_TODAY');
  if (d.isRaid) {
    if (!(await raidUnlocked(db, c.id, d))) throw new AppError(422, '아직 열리지 않은 레이드입니다.', 'RAID_LOCKED');
    if (difficulty !== 0) throw new AppError(422, '아직 입장할 수 없는 난이도입니다.', 'DIFFICULTY_LOCKED');
  } else if (difficulty > 0) {
    const clears = await repo.clearSummary(db, c.id);
    if (!clears.some((r) => r.dungeon_id === d.id && r.difficulty === difficulty - 1)) {
      throw new AppError(422, '아직 입장할 수 없는 난이도입니다.', 'DIFFICULTY_LOCKED');
    }
  }
  const diff = diffOf(eco, d, difficulty);
  if (!diff) throw new AppError(422, '알 수 없는 난이도입니다.', 'DUNGEON_UNKNOWN');
  // 레이드는 권장 레벨이 곧 입장 레벨이다(해골왕 20, 골렘 30, 최종 레이드 40). 요일던전만 여유를 준다
  const need = d.isRaid ? diff.recommendedLevel : diff.recommendedLevel - slack;
  if (c.level < need) throw new AppError(422, '레벨이 부족합니다.', 'LEVEL_TOO_LOW', { need, have: c.level });

  const resetDay = new Date(resetBoundaries(now).dailyStartAt);
  const limit = eco.dungeons.dailyEntries;
  let used = 0;
  if (!d.isRaid) {
    used = await repo.countEntries(db, c.id, resetDay);
    if (used >= limit) throw new AppError(422, '오늘 입장 횟수를 모두 사용했습니다.', 'NO_ENTRIES_LEFT');
  }
  // 최종 레이드 열쇠가 모자라도 입장은 된다. 보상 없는 연습판(KEYS_MISSING)이 되어 혼자서도 스토리를 끝낼 수 있다
  return { dungeon: d, diff, resetDay, countsEntry: !d.isRaid, used, limit };
}

/** 레이드 보상 잠금(입장 시점): 이미 받았거나 사람이 모자라면 연습 입장 */
export async function lockAtEntry(
  db: Queryable,
  characterId: number,
  d: DungeonDef,
  humans: number,
  now: Date,
): Promise<{ locked: boolean; reason: 'ALREADY_CLAIMED' | 'TOO_FEW_HUMANS' | 'KEYS_MISSING' | null }> {
  if (!d.isRaid) return { locked: false, reason: null };
  if (await raidClaimed(db, characterId, d, now)) return { locked: true, reason: 'ALREADY_CLAIMED' };
  if (humans < getConfig().policy.raidRewardMinHumans) return { locked: true, reason: 'TOO_FEW_HUMANS' };
  if (d.keyCost > 0 && (await stackCount(db, characterId, 'bag', getGameData().economy.dungeons.keyItem)) < d.keyCost) {
    return { locked: true, reason: 'KEYS_MISSING' };
  }
  return { locked: false, reason: null };
}
