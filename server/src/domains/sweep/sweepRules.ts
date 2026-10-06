// 소탕 순수 함수(설계 3절): 경험치, 카드 한 장, 클리어권 가격. 난수는 호출 쪽이 넘긴다.
import type { DungeonDef, EconomyData } from '../../gamedata/economyData';
import type { SweepData } from '../../gamedata/sweepData';
import { tierOfLevel } from '../../utils/gearTier';
import type { Rng } from '../../utils/rng';
import { applyXpBonus, clearXpBase, rollOneCard, type Card, type DiffNumbers } from '../dungeons/dungeonRules';

/** 소탕 경험치 = round(clearXpBase x (1 + sweep.xpBonusPercent/100)). 처치 경험치와 언더레벨 감쇠는 없다 */
export function sweepXp(d: DungeonDef, diff: Pick<DiffNumbers, 'rewardMul'>, difficulty: number, sweep: Pick<SweepData, 'xpBonusPercent'>): number {
  return applyXpBonus(clearXpBase(d, diff, difficulty), sweep.xpBonusPercent);
}

/** 소탕 카드 한 장: 직접 플레이와 같은 카드 풀, 장비는 gearKeepPercent 확률만 유지, 대박 없음 */
export function rollSweepCard(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng, sweep: Pick<SweepData, 'gearKeepPercent'>): Card {
  return rollOneCard(eco, d, diff, cls, rng, { sweepGearKeepPercent: sweep.gearKeepPercent });
}

/** 클리어권 1장 가격 = basePrice x (장비 단계(계정 최고 레벨) + 1) */
export function ticketUnitPrice(sweep: Pick<SweepData, 'shop'>, accountMaxLevel: number): number {
  return sweep.shop.basePrice * (tierOfLevel(accountMaxLevel) + 1);
}
