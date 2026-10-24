// 일일·주간 의뢰의 기간 날짜 계산(06:00 KST 게임 날짜, 목요일 06:00 KST 주 시작). 의뢰 칸과 완료 상자가 같이 쓴다.
import type { Board } from '../../gamedata/dailyQuests';
import { resetBoundaries } from '../../utils/resetBoundaries';

const KST_OFFSET_MS = 9 * 60 * 60 * 1000;
const DAY_MS = 24 * 60 * 60 * 1000;

const kstDate = (iso: string): string => new Date(Date.parse(iso) + KST_OFFSET_MS).toISOString().slice(0, 10);

/** 06:00 KST 경계의 게임 날짜(YYYY-MM-DD) */
export function gameDayOf(now: Date): string {
  return kstDate(resetBoundaries(now).dailyStartAt);
}

/** 목요일 06:00 KST 주 시작의 KST 날짜 */
export function weekStartOf(now: Date): string {
  return kstDate(resetBoundaries(now).weeklyStartAt);
}

export const shiftDay = (day: string, n: number): string => new Date(Date.parse(`${day}T00:00:00Z`) + n * DAY_MS).toISOString().slice(0, 10);

/** 진행 가능한 기간 시작 날짜들(오래된 것 먼저, 지금 기간이 마지막) */
export function openDays(board: Board, now: Date): string[] {
  if (board.period === 'week') return [weekStartOf(now)];
  const today = gameDayOf(now);
  const days: string[] = [];
  for (let i = board.carry; i >= 0; i--) days.push(shiftDay(today, -i));
  return days;
}

