// 채팅 순수 규칙(DB·소켓 없음): 문장 정리, 길이, 반복 판정, 간격 판정
import type { LimiterState } from './limiterStore';

/** NFC 정규화, 줄바꿈·제어·제로폭·방향 제어 문자 제거, 연속 공백 하나로, 앞뒤 공백 제거. '<', '>'는 그대로 둔다 */
export function cleanText(raw: string): string {
  return raw
    .normalize('NFC')
    .replace(/[\r\n\t\v\f]+/g, ' ')
    .replace(/[\p{Cc}\p{Cf}]/gu, '')
    .replace(/\s+/gu, ' ')
    .trim();
}

export const codePointLength = (s: string): number => Array.from(s).length;

/** 반복 비교용: 소문자, 공백 제거 */
export const repeatKey = (s: string): string => s.toLowerCase().replace(/\s+/gu, '');

export interface IntervalCheck {
  ok: boolean;
  retryAfterMs: number;
}

/** 최소 간격과 분당 상한. 거절되면 상태를 바꾸지 않는다 */
export function checkInterval(st: LimiterState, nowMs: number, minGapMs: number, perMinute: number): IntervalCheck {
  const gap = nowMs - st.lastSentAt;
  if (gap < minGapMs) return { ok: false, retryAfterMs: Math.ceil(minGapMs - gap) };
  st.sent = st.sent.filter((t) => nowMs - t < 60_000);
  const oldest = st.sent[0];
  if (st.sent.length >= perMinute && oldest !== undefined) return { ok: false, retryAfterMs: 60_000 - (nowMs - oldest) };
  return { ok: true, retryAfterMs: 0 };
}

/** 보내기로 확정된 문장을 반영한다. repeatLimit에 닿으면 true(이 메시지는 보내고 이후 금지) */
export function recordSend(st: LimiterState, nowMs: number, text: string): { repeats: number } {
  const key = repeatKey(text);
  st.repeats = key === st.lastText ? st.repeats + 1 : 1;
  st.lastText = key;
  st.lastSentAt = nowMs;
  st.sent.push(nowMs);
  return { repeats: st.repeats };
}

export const pruneWindow = (list: number[], nowMs: number, windowMs: number): number[] => list.filter((t) => nowMs - t < windowMs);
