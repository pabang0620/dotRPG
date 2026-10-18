export const DAILY_LIMIT_MS = 1_800_000;
export const KILL_INTERVAL_MS = 2_000;
export const CREDIT_CAP_MS = 6 * KILL_INTERVAL_MS;
// Match MiniDungeonDirector.SpawnAt: training monsters retain 45% of normal HP.
export const MINI_HP_MUL = 0.45;
export const MINI_TIERS = [10, 20, 30] as const;
export type MiniTier = (typeof MINI_TIERS)[number];
export const miniMapId = (tier: MiniTier): string => `mini_dungeon_${tier}`;
export const isMiniMap = (id: string): boolean => MINI_TIERS.some((tier) => miniMapId(tier) === id);
export const tierOf = (level: number, maxLevel: number): MiniTier | null =>
  level >= 10 && level < maxLevel && level < 40 ? (Math.floor(level / 10) * 10) as MiniTier : null;
// 2026-10-18: 처치 2,000마리 = 입장 레벨 막대 한 칸(240에서 변경). 240이면 30분이 사냥의 약 9배라 Lv40이 30~36시간으로 줄었다
export const KILLS_PER_BAR = 2000;
export const xpPerKill = (xpToNext: readonly number[], entryLevel: number): number =>
  Math.max(1, Math.ceil((xpToNext[entryLevel - 1] ?? 0) / KILLS_PER_BAR));

/** Server time only; repeated settlement and clock rollback cannot debit twice. */
export function timeCharge(chargedUntil: Date, endsAt: Date, now: Date): { ms: number; until: Date; expired: boolean } {
  const until = new Date(Math.max(chargedUntil.getTime(), Math.min(now.getTime(), endsAt.getTime())));
  return { ms: until.getTime() - chargedUntil.getTime(), until, expired: now >= endsAt };
}

/** Six spawn slots may die in a burst; old idle time never becomes unbounded kill credit. */
export function killCredit(creditsMs: number, creditsAt: Date, now: Date): number {
  return Math.min(CREDIT_CAP_MS, creditsMs + Math.max(0, now.getTime() - creditsAt.getTime()));
}
