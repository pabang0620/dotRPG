// 장비 레벨 단계(C# GearCatalog.TierLevels와 같다): 0 = Lv.1, 1 = Lv.10 ... 7 = Lv.40
export const TIER_LEVELS = [1, 10, 15, 20, 25, 30, 35, 40] as const;

/** 그 레벨 이하에서 가장 높은 단계 번호 */
export function tierOfLevel(level: number): number {
  let t = 0;
  for (let i = 0; i < TIER_LEVELS.length; i++) if ((TIER_LEVELS[i] as number) <= level) t = i;
  return t;
}
