// D6 GET /raids: 레이드 해금·보상·열쇠 상태. 레이드 보상 정산(raid_claims, 열쇠)은 dungeons/dungeonResult의 finalizeCleared가 한다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import * as charRepo from '../characters/characterRepository';
import { isOpenToday } from '../dungeons/dungeonRules';
import { RAID_HEROIC } from '../dungeons/dungeonRules';
import { heroicUnlocked, raidClaimed, raidPeriod, raidUnlocked } from '../dungeons/entryRules';
import { chapter3Open, isChapter3 } from '../../gamedata/chapterGate';
import { stackCount } from '../economy/economyRepository';

export async function listRaids(accountId: number, characterUuid: string) {
  const db = getPool();
  const c = await charRepo.findOwnedAlive(db, accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const eco = getGameData().economy;
  const now = getNow();
  const b = resetBoundaries(now);
  const have = await stackCount(db, c.id, 'bag', eco.dungeons.keyItem);
  const raids = [];
  for (const d of eco.dungeons.byId.values()) {
    if (!d.isRaid) continue;
    if (isChapter3('dungeon', d.id) && !chapter3Open()) continue; // 닫힌 3장 레이드는 목록에도 없다
    const period = raidPeriod(d, now);
    const open = isOpenToday(eco, d, now);
    const claimed = await raidClaimed(db, c.id, d, now);
    const clears = await db.query<{ n: string }>(
      'SELECT count(*) AS n FROM raid_claims WHERE character_id = $1 AND dungeon_id = $2 AND difficulty = 0 AND period_start >= $3',
      [c.id, d.id, new Date(b.weeklyStartAt)],
    );
    // 영웅 레이드: 그 레이드를 한 번 클리어하면 열리고, 보상은 주 1회(일반 보상과 따로)
    const h = eco.dungeons.raidHeroic;
    const heroic = h
      ? {
          name: h.name,
          unlocked: await heroicUnlocked(db, c.id, d),
          reward_available: open && !(await raidClaimed(db, c.id, d, now, RAID_HEROIC)) && (d.raidTier !== 'Final' || have >= d.keyCost),
          period_kind: 'weekly' as const,
          hp_mul: h.hpMul,
          damage_mul: h.damageMul,
          reward_mul: h.rewardMul,
        }
      : null;
    raids.push({
      id: d.id,
      tier: d.raidTier === 'Final' ? 'final' : 'mid',
      unlocked: await raidUnlocked(db, c.id, d),
      open_today: open,
      reward_available: open && !claimed && (d.raidTier !== 'Final' || have >= d.keyCost),
      period_kind: period.kind,
      clears_this_period: Number((clears.rows[0] as { n: string }).n),
      key: { item: eco.dungeons.keyItem, have, cost: d.keyCost, drop_min: d.keyMin, drop_max: d.keyMax },
      min_humans_for_reward: getConfig().policy.raidRewardMinHumans,
      heroic,
    });
  }
  return {
    reset: { daily_start_at: b.dailyStartAt, next_daily_at: b.nextDailyAt, weekly_start_at: b.weeklyStartAt, next_weekly_at: b.nextWeeklyAt },
    raids,
  };
}
