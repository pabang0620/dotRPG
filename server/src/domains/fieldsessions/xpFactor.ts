// 파티 세션의 레벨 격차 경험치 감쇠(순수 함수, phase8_api.md 6.7).
// xp_factor = clamp(1 - STEP x max(0, (몬스터 레벨 - SLACK) - 멤버 레벨), MIN, 1)
export interface CarryPolicy {
  carrySlack: number;
  carryStep: number;
  carryMin: number;
}

export function xpFactor(monsterLevel: number, memberLevel: number, p: CarryPolicy): number {
  const gap = Math.max(0, monsterLevel - p.carrySlack - memberLevel);
  const f = 1 - p.carryStep * gap;
  const v = Math.min(1, Math.max(p.carryMin, f));
  // NUMERIC(4,3) 로 저장되므로 소수 셋째 자리까지만 쓴다
  return Math.round(v * 1000) / 1000;
}
