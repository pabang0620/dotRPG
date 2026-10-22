// 장비 레벨 단계(C# GearCatalog.TierLevels와 같다): 0 = Lv.1, 1 = Lv.10 ... 7 = Lv.40, 8 = Lv.45, 9 = Lv.50(챕터 3)
export const TIER_LEVELS = [1, 10, 15, 20, 25, 30, 35, 40, 45, 50] as const;
/** 챕터 2까지의 단계 수(Lv.40까지). 별빛 상점 확률표처럼 공개된 표는 3장이 열릴 때까지 이 범위만 쓴다 */
export const CHAPTER2_TIER_COUNT = 8;

/** 그 레벨 이하에서 가장 높은 단계 번호 */
export function tierOfLevel(level: number): number {
  let t = 0;
  for (let i = 0; i < TIER_LEVELS.length; i++) if ((TIER_LEVELS[i] as number) <= level) t = i;
  return t;
}

/**
 * 드롭 단계: 지금 오를 수 있는 레벨(levelCap)을 넘지 않는다.
 * 3장이 닫혀 있으면 지하 Lv45+ 몬스터도 Lv.40 단계(7)까지만 떨어뜨린다.
 */
export function dropTierOfLevel(level: number, cap: number): number {
  return tierOfLevel(Math.min(level, cap));
}
