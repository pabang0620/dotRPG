// 일일(06:00 KST)·주간(목요일 06:00 KST) 초기화 경계. 서버 어디서든 이 함수만 쓴다.
const KST_OFFSET_MS = 9 * 60 * 60 * 1000;
const DAY_MS = 24 * 60 * 60 * 1000;
const RESET_HOUR_MS = 6 * 60 * 60 * 1000;
const THURSDAY = 4;

export interface ResetBoundaries {
  /** 직전 06:00 KST (현재 일일 구간의 시작) */
  dailyStartAt: string;
  nextDailyAt: string;
  /** 직전 목요일 06:00 KST (현재 주간 구간의 시작) */
  weeklyStartAt: string;
  nextWeeklyAt: string;
}

export function resetBoundaries(nowUtc: Date): ResetBoundaries {
  // KST 벽시계를 UTC 필드처럼 다루기 위해 9시간을 더한다
  const kstMs = nowUtc.getTime() + KST_OFFSET_MS;
  const kstMidnight = Math.floor(kstMs / DAY_MS) * DAY_MS;

  let dailyKst = kstMidnight + RESET_HOUR_MS;
  if (dailyKst <= kstMs) dailyKst += DAY_MS;

  let weeklyKst = kstMidnight + RESET_HOUR_MS;
  for (let i = 0; i < 8; i++) {
    const dow = new Date(weeklyKst).getUTCDay();
    if (dow === THURSDAY && weeklyKst > kstMs) break;
    weeklyKst += DAY_MS;
  }

  return {
    dailyStartAt: new Date(dailyKst - DAY_MS - KST_OFFSET_MS).toISOString(),
    nextDailyAt: new Date(dailyKst - KST_OFFSET_MS).toISOString(),
    weeklyStartAt: new Date(weeklyKst - 7 * DAY_MS - KST_OFFSET_MS).toISOString(),
    nextWeeklyAt: new Date(weeklyKst - KST_OFFSET_MS).toISOString(),
  };
}

const WEEKDAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** 게임이 "오늘"로 세는 요일(06:00 전에는 전날). C# ResetClock.GameDay와 같다. */
export function gameWeekday(nowUtc: Date): string {
  const start = Date.parse(resetBoundaries(nowUtc).dailyStartAt);
  return WEEKDAY_NAMES[new Date(start + KST_OFFSET_MS).getUTCDay()] as string;
}
