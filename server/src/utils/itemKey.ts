// 아이템 키: 기본 id 또는 "id+강화단계"(+0은 기본 id). C# EquipmentDatabase.KeyFor와 같은 규칙.
// 계승한 장비는 뒤에 "^계승횟수"(1~9)가 붙고 이때는 +0도 적는다: "eq_sword_20_r+0^1", "eq_sword_30_r+12^2".
// '+' 앞은 언제나 기본 id라 split_part(key, '+', 1) 같은 기존 처리도 그대로 맞는다.
export const ITEM_KEY_RE = /^[a-z][a-z0-9_]{0,39}(?:\+[1-9][0-9]?|\+(?:0|[1-9][0-9]?)\^[1-9])?$/;

export interface ParsedKey {
  base: string;
  level: number;
  /** 계승 횟수(0 = 계승한 적 없음) */
  inherit: number;
}

export function parseItemKey(key: string): ParsedKey | null {
  if (!ITEM_KEY_RE.test(key)) return null;
  const i = key.indexOf('+');
  if (i < 0) return { base: key, level: 0, inherit: 0 };
  const j = key.indexOf('^', i);
  if (j < 0) return { base: key.slice(0, i), level: Number(key.slice(i + 1)), inherit: 0 };
  return { base: key.slice(0, i), level: Number(key.slice(i + 1, j)), inherit: Number(key.slice(j + 1)) };
}

/** 기본 id + 강화 단계(+ 계승 횟수)로 키를 만든다. 계승 횟수가 있으면 +0도 적는다 */
export function keyAt(base: string, level: number, inherit = 0): string {
  if (inherit > 0) return `${base}+${Math.max(0, level)}^${inherit}`;
  return level <= 0 ? base : `${base}+${level}`;
}

/** C# Mathf.RoundToInt / Math.Round(기본): .5는 짝수 쪽 */
export function roundHalfEven(x: number): number {
  const f = Math.floor(x);
  const d = x - f;
  if (d < 0.5) return f;
  if (d > 0.5) return f + 1;
  return f % 2 === 0 ? f : f + 1;
}

/** C# Math.Round(x, MidpointRounding.AwayFromZero) (양수만 쓴다) */
export function roundHalfAway(x: number): number {
  return Math.floor(x + 0.5);
}

/** C# float32 연산 흉내(경험치·카드 수량 계산이 float이다) */
export const f32 = Math.fround;
