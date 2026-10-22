/** 하루 이용 시간의 최댓값(티어 10·20·30). 하루 사용량(mini_dungeon_days.used_ms)은 모든 티어가 함께 쓴다 */
export const DAILY_LIMIT_MS = 1_800_000;
/** 챕터 3 티어 40은 하루 10분(Docs/ch3/LIVE_CONTENT.md 1절): 따라잡기가 거의 매일 켜지므로 시간을 짧게 둔다 */
export const DAILY_LIMIT_MS_TIER40 = 600_000;
export const KILL_INTERVAL_MS = 2_000;
export const CREDIT_CAP_MS = 6 * KILL_INTERVAL_MS;
// Match MiniDungeonDirector.SpawnAt: training monsters retain 45% of normal HP.
export const MINI_HP_MUL = 0.45;
export const MINI_TIERS = [10, 20, 30, 40] as const;
export type MiniTier = (typeof MINI_TIERS)[number];
/** 챕터 3 티어(잿빛 수련의 협곡, Lv40~49). 3장이 닫혀 있으면 입장 불가 */
export const CHAPTER3_TIER = 40;
export const miniMapId = (tier: MiniTier): string => `mini_dungeon_${tier}`;
/** 티어별 하루 상한(ms). 그날 이미 쓴 시간은 티어와 상관없이 함께 센다 */
export const dailyLimitMs = (tier: number): number => (tier === CHAPTER3_TIER ? DAILY_LIMIT_MS_TIER40 : DAILY_LIMIT_MS);
/** 티어별 수련 몬스터(클라 MiniDungeonCatalog.MonsterId와 같다) */
export const miniMonsterId = (tier: number): string => (tier === CHAPTER3_TIER ? 'void_hound' : 'skel_warrior');
export const isMiniMap = (id: string): boolean => MINI_TIERS.some((tier) => miniMapId(tier) === id);
// 입장 티어: 10단위 내림. 상한 레벨(levelCap)에서는 입장 불가, 표(MINI_TIERS)에 없는 티어도 불가. 3장이 닫혀 있으면 상한이 40이라 티어 40은 나오지 않는다
export const tierOf = (level: number, cap: number): MiniTier | null => {
  const t = Math.floor(level / 10) * 10;
  return level >= 10 && level < cap && (MINI_TIERS as readonly number[]).includes(t) ? (t as MiniTier) : null;
};
// 2026-10-18 따라잡기: 평소에는 처치 8,000마리 = 입장 레벨 막대 한 칸(Lv20 기준 사냥의 약 0.5배), 다음 메인 퀘스트가 레벨 때문에
// 막혔을 때만 800마리 = 한 칸(x10, Lv20 기준 사냥의 약 5배). 일반 사냥·던전·파티와 다른 점은 "막힌 레벨을 메우는 곳"이라는 것이다.
// 매일 30분을 상한 속도로 해도 Lv40이 42시간 아래로 내려가지 않는다(Tools/balance/theory_progress.py --measured --mini)
export const KILLS_PER_BAR = 8000;
export const KILLS_PER_BAR_STUCK = 800;
// 2026-10-21 처치 골드: 드롭·재료 없이 처치마다 고정 골드(사냥터 골드 더미 8~17의 3배). 미니 던전으로 사냥 시간을 대신하는 사람이
// 강화 골드가 모자라지 않게 한다(Tools/balance/theory_gold.py --mini: 두 경로 모두 Lv40에서 클리어 한 벌의 81~88%). 클라 MiniDungeonCatalog.GoldPerKill
export const MINI_GOLD_PER_KILL = 36;
export const xpPerKill = (xpToNext: readonly number[], entryLevel: number, stuck = false): number =>
  Math.max(1, Math.ceil((xpToNext[entryLevel - 1] ?? 0) / (stuck ? KILLS_PER_BAR_STUCK : KILLS_PER_BAR)));

type MainQuest = { kind: string; minLevel: number; requires: readonly string[]; requiresFlags: readonly string[]; objectives: { levelNeed: number } };

/**
 * 다음 메인 퀘스트가 레벨 때문에 막혔으면 그 레벨, 아니면 null.
 * 받을 수 있는 메인 퀘스트(선행·플래그 충족, 미완료)가 하나 이상 있고 그 전부가 지금 레벨보다 높은 레벨을 요구할 때만 막힌 것으로 본다.
 * 레벨 말고 다른 이유로 못 받는 메인 퀘스트는 세지 않는다.
 */
export function mainQuestLevelGate(quests: Iterable<[string, MainQuest]>, claimed: ReadonlySet<string>, flags: ReadonlySet<string>, level: number): number | null {
  let gate: number | null = null;
  for (const [id, q] of quests) {
    if (q.kind !== 'main' || claimed.has(id)) continue;
    if (q.requires.some((r) => !claimed.has(r)) || q.requiresFlags.some((f) => !flags.has(f))) continue;
    const need = Math.max(q.minLevel, q.objectives.levelNeed);
    if (need <= level) return null;
    gate = gate === null ? need : Math.min(gate, need);
  }
  return gate;
}

/** Server time only; repeated settlement and clock rollback cannot debit twice. */
export function timeCharge(chargedUntil: Date, endsAt: Date, now: Date): { ms: number; until: Date; expired: boolean } {
  const until = new Date(Math.max(chargedUntil.getTime(), Math.min(now.getTime(), endsAt.getTime())));
  return { ms: until.getTime() - chargedUntil.getTime(), until, expired: now >= endsAt };
}

/** Six spawn slots may die in a burst; old idle time never becomes unbounded kill credit. */
export function killCredit(creditsMs: number, creditsAt: Date, now: Date): number {
  return Math.min(CREDIT_CAP_MS, creditsMs + Math.max(0, now.getTime() - creditsAt.getTime()));
}
