// 아이템 키: 기본 id 또는 "id+강화단계"(+0은 기본 id). C# EquipmentDatabase.KeyFor와 같은 규칙.
export const ITEM_KEY_RE = /^[a-z][a-z0-9_]{0,39}(\+[1-9][0-9]?)?$/;

export interface ParsedKey {
  base: string;
  level: number;
}

export function parseItemKey(key: string): ParsedKey | null {
  if (!ITEM_KEY_RE.test(key)) return null;
  const i = key.indexOf('+');
  if (i < 0) return { base: key, level: 0 };
  return { base: key.slice(0, i), level: Number(key.slice(i + 1)) };
}

export function keyAt(base: string, level: number): string {
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
