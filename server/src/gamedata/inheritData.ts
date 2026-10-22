// 장비 계승표(enhance.json "inheritTiers", C# InheritRules에서 내보낸다). 챕터 3의 promote.inherit와 이름이 겹치지 않게 따로 둔다.
import { z } from 'zod';

const nonNegInt = z.number().int().min(0);

export const inheritTiersSchema = z.looseObject({
  /** 키에 적을 수 있는 계승 횟수 상한(^1..^maxCount) */
  maxCount: z.number().int().min(1).max(9),
  /** 유지 실패 때 내려가는 단계 */
  failDrop: nonNegInt,
  /** 계승하는 강화 단계 N별 유지 확률(%) */
  keepPercent: z.array(z.number().int().min(0).max(100)).min(1),
  /** 이미 계승한 횟수 k별 추가 깎임(마지막 값이 계속된다) */
  stackPenalty: z.array(nonNegInt).min(1),
  /** from -> to, gold[N] = 강화 단계 N을 계승하는 골드 */
  rows: z.array(z.looseObject({ from: z.string().min(1), to: z.string().min(1), gold: z.array(nonNegInt).min(1) })),
});

export interface InheritTiers {
  maxCount: number;
  failDrop: number;
  keepPercent: number[];
  stackPenalty: number[];
  rows: Map<string, { to: string; gold: number[] }>;
}

/** 표 검증: 장비가 shop.equipment에 있고, 강화 단계마다 확률·골드가 있어야 한다 */
export function buildInheritTiers(
  raw: z.infer<typeof inheritTiersSchema> | undefined,
  equipment: Map<string, { starter: boolean }>,
  maxEnhance: number,
): InheritTiers | null {
  if (!raw) return null;
  const fail = (m: string): never => {
    throw new Error(`게임 데이터 검증 실패: enhance.inheritTiers ${m}`);
  };
  if (raw.keepPercent.length !== maxEnhance + 1) fail(`keepPercent 길이가 ${maxEnhance + 1} 이 아닙니다`);
  const rows = new Map<string, { to: string; gold: number[] }>();
  for (const r of raw.rows) {
    const from = equipment.get(r.from);
    const to = equipment.get(r.to);
    if (!from || !to) fail(`${r.from} -> ${r.to} 이 shop.equipment에 없습니다`);
    if (from?.starter || to?.starter) fail(`${r.from} 시작 장비는 계승하지 않습니다`);
    if (rows.has(r.from)) fail(`${r.from} 중복`);
    if (r.gold.length !== maxEnhance + 1) fail(`${r.from} gold 길이가 ${maxEnhance + 1} 이 아닙니다`);
    rows.set(r.from, { to: r.to, gold: r.gold });
  }
  return { maxCount: raw.maxCount, failDrop: raw.failDrop, keepPercent: raw.keepPercent, stackPenalty: raw.stackPenalty, rows };
}

/** 이미 k번 계승한 장비의 추가 깎임 */
export function stackPenaltyOf(t: InheritTiers, k: number): number {
  const i = Math.max(0, Math.min(t.stackPenalty.length - 1, k));
  return t.stackPenalty[i] as number;
}

/** 계승 결과 강화 단계: 유지면 N, 실패면 N - failDrop, 그 뒤 누적 깎임. 0 아래로 내려가지 않는다 */
export function inheritResultLevel(t: InheritTiers, level: number, k: number, kept: boolean): number {
  return Math.max(0, (kept ? level : level - t.failDrop) - stackPenaltyOf(t, k));
}
