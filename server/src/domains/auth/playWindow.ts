// 게임시간 선택제(청소년 보호 정책 제4조, 0039): 계정별 이용 가능 시간대(KST 분). start > end 이면 자정을 넘는 구간.
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';

const KST_OFFSET_MS = 9 * 60 * 60 * 1000;
const DAY_MIN = 1440;

export interface PlayWindow {
  start: number | null | undefined;
  end: number | null | undefined;
}

export const hasWindow = (w: PlayWindow): w is { start: number; end: number } =>
  typeof w.start === 'number' && typeof w.end === 'number';

export function fmtMinutes(m: number): string {
  return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
}

/** "HH:MM" -> 분. 형식이 틀리면 null */
export function parseHm(s: string): number | null {
  const m = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(s);
  return m ? Number(m[1]) * 60 + Number(m[2]) : null;
}

export function kstMinutes(now: Date = getNow()): number {
  return Math.floor(((now.getTime() + KST_OFFSET_MS) % (DAY_MIN * 60_000)) / 60_000);
}

/** 제한이 없거나 지금이 허용 구간 안이면 true */
export function isOpen(w: PlayWindow, now: Date = getNow()): boolean {
  if (!hasWindow(w)) return true;
  const m = kstMinutes(now);
  return w.start < w.end ? m >= w.start && m < w.end : m >= w.start || m < w.end;
}

/** 구간 밖일 때 로그인할 수 없다: 403 PLAY_WINDOW_CLOSED, details.opens_at = 다음 시작 "HH:MM" */
export function assertOpen(w: PlayWindow): void {
  if (isOpen(w)) return;
  throw new AppError(403, '설정된 이용 시간대가 아닙니다.', 'PLAY_WINDOW_CLOSED', { opens_at: fmtMinutes((w as { start: number }).start) });
}
