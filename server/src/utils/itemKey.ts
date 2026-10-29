// 아이템 키: 기본 id 또는 "id+강화단계"(+0은 기본 id). C# EquipmentDatabase.KeyFor와 같은 규칙.
// 계승한 장비는 뒤에 "^계승횟수"(1~9)가 붙고 이때는 +0도 적는다: "eq_sword_20_r+0^1", "eq_sword_30_r+12^2".
// 히든 옵션이 있는 장비는 맨 뒤에 "~옵션 글자"가 붙고 이때도 +0을 적는다: "eq_sword_20_x+0~d", "eq_sword_30_x+12^2~c"
// (Docs/PLAN_GEAR_HIDDEN_2026-10-29.md). 옵션 글자가 그 장비 부위에 맞는지는 utils/hiddenOption이 본다.
// '+' 앞은 언제나 기본 id라 split_part(key, '+', 1) 같은 기존 처리도 그대로 맞는다.
export const ITEM_KEY_RE = /^[a-z][a-z0-9_]{0,39}(?:\+[1-9][0-9]?|\+(?:0|[1-9][0-9]?)\^[1-9]|\+(?:0|[1-9][0-9]?)(?:\^[1-9])?~[a-z])?$/;

export interface ParsedKey {
  base: string;
  level: number;
  /** 계승 횟수(0 = 계승한 적 없음) */
  inherit: number;
  /** 히든 옵션 글자(없으면 속성 자체가 없다) */
  hidden?: string;
}

export function parseItemKey(key: string): ParsedKey | null {
  if (!ITEM_KEY_RE.test(key)) return null;
  const tilde = key.indexOf('~');
  const hidden = tilde >= 0 ? key.slice(tilde + 1) : '';
  const core = tilde >= 0 ? key.slice(0, tilde) : key;
  const i = core.indexOf('+');
  let out: ParsedKey;
  if (i < 0) out = { base: core, level: 0, inherit: 0 };
  else {
    const j = core.indexOf('^', i);
    out = j < 0
      ? { base: core.slice(0, i), level: Number(core.slice(i + 1)), inherit: 0 }
      : { base: core.slice(0, i), level: Number(core.slice(i + 1, j)), inherit: Number(core.slice(j + 1)) };
  }
  if (hidden) out.hidden = hidden;
  return out;
}

/** 기본 id + 강화 단계(+ 계승 횟수, + 히든 옵션)로 키를 만든다. 계승 횟수나 히든 옵션이 있으면 +0도 적는다 */
export function keyAt(base: string, level: number, inherit = 0, hidden = ''): string {
  const lv = Math.max(0, level);
  if (hidden) return `${base}+${lv}${inherit > 0 ? `^${inherit}` : ''}~${hidden}`;
  if (inherit > 0) return `${base}+${lv}^${inherit}`;
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
