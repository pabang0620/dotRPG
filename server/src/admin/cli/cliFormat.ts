// 표 출력. --json이면 data 그대로 출력한다.
const isObj = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);

const cell = (v: unknown): string => {
  if (v === null || v === undefined) return '-';
  if (typeof v === 'object') return JSON.stringify(v);
  return String(v);
};

export function table(rows: Record<string, unknown>[]): string[] {
  if (rows.length === 0) return ['(없음)'];
  const cols = [...new Set(rows.flatMap((r) => Object.keys(r)))];
  const width = cols.map((c) => Math.min(48, Math.max(c.length, ...rows.map((r) => cell(r[c]).length))));
  const fmt = (vals: string[]): string => vals.map((v, i) => v.slice(0, width[i]).padEnd(width[i] as number)).join('  ');
  return [fmt(cols), fmt(width.map((w) => '-'.repeat(w))), ...rows.map((r) => fmt(cols.map((c) => cell(r[c]))))];
}

/** data를 사람이 읽기 쉽게: 배열 하나가 중심이면 표, 아니면 키: 값 줄 */
export function render(data: unknown, indent = ''): string[] {
  if (Array.isArray(data)) return data.every(isObj) ? table(data as Record<string, unknown>[]).map((l) => indent + l) : data.map((v) => `${indent}${cell(v)}`);
  if (!isObj(data)) return [`${indent}${cell(data)}`];
  const lines: string[] = [];
  for (const [k, v] of Object.entries(data)) {
    if (Array.isArray(v) && v.length > 0 && v.every(isObj)) {
      lines.push(`${indent}${k}:`, ...render(v, `${indent}  `));
    } else if (isObj(v)) {
      lines.push(`${indent}${k}:`, ...render(v, `${indent}  `));
    } else {
      lines.push(`${indent}${k}: ${cell(v)}`);
    }
  }
  return lines;
}
