// 일일(06:00 KST)·주간(목요일 06:00 KST) 초기화 경계. 서버 어디서든 이 함수만 쓴다.
const KST_OFFSET_MS = 9 * 60 * 60 * 1000;
const DAY_MS = 24 * 60 * 60 * 1000;
const RESET_HOUR_MS = 6 * 60 * 60 * 1000;
const THURSDAY = 4;

export interface ResetBoundaries {
  nextDailyAt: string;
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
    nextDailyAt: new Date(dailyKst - KST_OFFSET_MS).toISOString(),
    nextWeeklyAt: new Date(weeklyKst - KST_OFFSET_MS).toISOString(),
  };
}
