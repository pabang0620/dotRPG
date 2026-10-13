import type { EconomyData } from '../../gamedata/economyData';

/**
 * 몬스터 체력 인원 보정. AI 동료는 사람 aiHpWeight명(레이드는 raidAiHpWeight명)으로 세고, 표 사이 값은 선형 보간한다.
 * 클라이언트 DungeonDatabase.PartyScale(humans, ai, raid)와 같은 식이다.
 */
export function partyHpScaleOf(eco: Pick<EconomyData, 'dungeons'>, humans: number, ai: number, raid = false): number {
  const table = eco.dungeons.partyHpScale;
  const w = raid ? eco.dungeons.raidAiHpWeight : eco.dungeons.aiHpWeight;
  const n = Math.min(Math.max(Math.max(1, humans) + w * Math.max(0, ai), 1), table.length);
  const lo = Math.floor(n);
  const at = (i: number): number => table[i] ?? 1;
  if (lo >= table.length) return at(table.length - 1);
  return at(lo - 1) + (at(lo) - at(lo - 1)) * (n - lo);
}
