// 파티 세션의 레벨 격차 경험치 감쇠(순수 함수, phase8_api.md 6.7).
// xp_factor = clamp(1 - STEP x max(0, (몬스터 레벨 - SLACK) - 멤버 레벨), MIN, 1). 기본 SLACK 5, STEP 0.15, MIN 0.02(9단계)
export interface CarryPolicy {
  carrySlack: number;
  carryStep: number;
  carryMin: number;
  /** 9단계: 몬스터 레벨 - 멤버 레벨이 이 값 이상이면 경험치는 정확히 1, 재료·장비 드롭 배율은 carryHardDropMul */
  carryHardGap?: number;
}

/** Post-cap cave levels express enemy difficulty. Carry checks compare against an attainable player level. */
export function carryReferenceLevel(monsterLevel: number, worldLayer: 'surface' | 'underground' | undefined, playerCap: number): number {
  return worldLayer === 'underground' ? Math.min(monsterLevel, playerCap) : monsterLevel;
}

export function xpFactor(monsterLevel: number, memberLevel: number, p: CarryPolicy): number {
  const gap = Math.max(0, monsterLevel - p.carrySlack - memberLevel);
  const f = 1 - p.carryStep * gap;
  const v = Math.min(1, Math.max(p.carryMin, f));
  // NUMERIC(4,3) 로 저장되므로 소수 셋째 자리까지만 쓴다
  return Math.round(v * 1000) / 1000;
}

/** 9단계 하드 격차: d = 몬스터 레벨 - 멤버 레벨 >= FIELD_CARRY_HARD_GAP 이면 true(경험치 1, 드롭 배율 FIELD_CARRY_HARD_DROP_MUL) */
export function isHardGap(monsterLevel: number, memberLevel: number, p: CarryPolicy): boolean {
  return p.carryHardGap !== undefined && monsterLevel - memberLevel >= p.carryHardGap;
}
