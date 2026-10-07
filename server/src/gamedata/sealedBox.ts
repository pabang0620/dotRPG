// 봉인된 상자 확률표(server/data/sealed_box.json, Docs/PLAN_CASH_BOX_PASS.md 2절).
// 서버 정본이라 data_version 해시에 넣지 않는다(클라이언트는 GET /starshop/sealed 로 받는다).
// 모양과 합계(100%)가 틀리면 기동 시 던진다: 확률표가 조용히 틀어지면 안 된다.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { getConfig } from '../config/env';
import { getGameData } from './loader';

const reward = z.strictObject({ item_key: z.string().min(1), count: z.number().int().positive() });
const row = z.strictObject({
  id: z.string().min(1),
  rate: z.number().positive(),
  tier: z.enum(['common', 'rare']),
  grade: z.string().min(1),
  rewards: z.array(reward).min(1),
});
const fileSchema = z.looseObject({
  schema: z.literal(1),
  ratesVersion: z.string().min(1),
  priceOne: z.number().int().positive(),
  priceEleven: z.number().int().positive(),
  elevenCount: z.number().int().min(2),
  boosterEvery: z.number().int().positive(),
  rows: z.array(row).min(2),
  luckBoxFallback: reward,
});

export type SealedRow = z.infer<typeof row>;
export type SealedData = z.infer<typeof fileSchema>;

/** 검증 오류 목록(빈 배열 = 정상). 아이템 존재 확인은 itemIds를 줄 때만 한다 */
export function validateSealedBox(raw: unknown, itemIds?: Set<string>): { data: SealedData | null; errors: string[] } {
  const p = fileSchema.safeParse(raw);
  if (!p.success) return { data: null, errors: p.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`) };
  const errors: string[] = [];
  const d = p.data;
  const sum = d.rows.reduce((a, r) => a + r.rate, 0);
  if (Math.abs(sum - 100) > 1e-6) errors.push(`rows 확률 합이 100이 아닙니다: ${sum}`);
  if (new Set(d.rows.map((r) => r.id)).size !== d.rows.length) errors.push('rows id 중복');
  const rare = d.rows.filter((r) => r.tier === 'rare').reduce((a, r) => a + r.rate, 0);
  if (rare * 2 >= 100) errors.push('희귀 이상 확률을 2배 하면 100%를 넘습니다');
  if (itemIds) {
    const keys = [d.luckBoxFallback.item_key, ...d.rows.flatMap((r) => r.rewards.map((x) => x.item_key))];
    for (const k of keys) if (!itemIds.has(k)) errors.push(`items.json에 없는 아이템: ${k}`);
  }
  return { data: d, errors };
}

let current: SealedData | null = null;

export function setSealedBoxForTest(d: SealedData | null): void {
  if (process.env.NODE_ENV === 'production') throw new Error('운영에서는 확률표를 바꿀 수 없습니다');
  current = d;
}

export function loadSealedBox(dir: string = getConfig().gameDataDir, itemIds: Set<string> = getGameData().itemIds): SealedData {
  const file = path.join(dir, 'sealed_box.json');
  if (!fs.existsSync(file)) throw new Error('sealed_box.json 이 없습니다');
  const { data, errors } = validateSealedBox(JSON.parse(fs.readFileSync(file, 'utf8')) as unknown, itemIds);
  if (!data || errors.length > 0) throw new Error(`sealed_box.json 검증 실패: ${errors.join('; ')}`);
  current = data;
  return data;
}

export function getSealedBox(): SealedData {
  return current ?? loadSealedBox();
}

export interface RatedRow {
  row: SealedRow;
  /** 이 표에서 적용되는 확률(%) */
  rate: number;
  /** 지급 수량 배율(부스터 2) */
  mult: number;
}

/**
 * 일반 표 또는 부스터 표. 부스터: 희귀 이상 확률 x2, 일반 등급은 같은 비율로 줄여 합계 100을 유지,
 * 지급 수량 x2(Tools/balance/theory_cash.py rates()와 같다)
 */
export function sealedTable(d: SealedData, boosted: boolean): RatedRow[] {
  if (!boosted) return d.rows.map((row) => ({ row, rate: row.rate, mult: 1 }));
  const rare = d.rows.filter((r) => r.tier === 'rare').reduce((a, r) => a + r.rate, 0);
  const scale = (100 - rare - rare) / (100 - rare);
  return d.rows.map((row) => ({ row, rate: row.tier === 'rare' ? row.rate * 2 : row.rate * scale, mult: 2 }));
}
