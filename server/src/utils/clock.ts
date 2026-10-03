// 서버 시각. 판정(만료, 재생, 일일 초기화)은 모두 이 시계를 쓴다. 테스트만 바꿀 수 있다.
let override: (() => Date) | null = null;

export function getNow(): Date {
  return override ? override() : new Date();
}

/** 테스트 전용. null이면 실제 시계로 되돌린다. */
export function setClockOverride(fn: (() => Date) | null): void {
  override = fn;
}
