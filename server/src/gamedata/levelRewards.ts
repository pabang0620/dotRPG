// 레벨 달성 보상 단계표(server/data/level_rewards.json, Docs/server/phase13_level_rewards.md 1절).
// star_products.json 과 같은 방식: 서버 정본, data_version 해시에 넣지 않는다(클라이언트는 GET /characters/:uuid/level-rewards 로 받는다).
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { getConfig } from '../config/env';
import { getGameData } from './loader';

const tierSchema = z.strictObject({ level: z.number().int().min(1).max(999), stars: z.number().int().positive() });
const passReward = z.strictObject({ item_key: z.string().min(1), count: z.number().int().positive() });
const passTierSchema = z.strictObject({ level: z.number().int().min(1).max(999), rewards: z.array(passReward).min(1) });
const passSchema = z.strictObject({ price: z.number().int().positive(), tiers: z.array(passTierSchema).min(1) });
const fileSchema = z.looseObject({ tiers: z.array(tierSchema).min(1), pass: passSchema });

export type LevelRewardTier = z.infer<typeof tierSchema>;
export type PassTier = z.infer<typeof passTierSchema>;
export interface GrowthPass {
  price: number;
  tiers: PassTier[];
}
export interface LevelRewardData {
  tiers: LevelRewardTier[];
  pass: GrowthPass;
}

/** 검증 오류 목록(빈 배열 = 정상). itemIds를 주면 패스 보상 아이템이 items.json에 있는지도 본다 */
export function validateLevelRewards(
  raw: unknown,
  itemIds?: Set<string>,
): { tiers: LevelRewardTier[] | null; pass: GrowthPass | null; errors: string[] } {
  const p = fileSchema.safeParse(raw);
  if (!p.success) return { tiers: null, pass: null, errors: p.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`) };
  const errors: string[] = [];
  const seen = new Set<number>();
  for (const t of p.data.tiers) {
    if (seen.has(t.level)) errors.push(`level 중복: ${t.level}`);
    seen.add(t.level);
  }
  const seenPass = new Set<number>();
  for (const t of p.data.pass.tiers) {
    if (seenPass.has(t.level)) errors.push(`pass level 중복: ${t.level}`);
    seenPass.add(t.level);
    for (const r of t.rewards) if (itemIds && !itemIds.has(r.item_key)) errors.push(`pass ${t.level}: items.json에 없는 아이템 ${r.item_key}`);
  }
  const pass: GrowthPass = { price: p.data.pass.price, tiers: [...p.data.pass.tiers].sort((a, b) => a.level - b.level) };
  return { tiers: [...p.data.tiers].sort((a, b) => a.level - b.level), pass, errors };
}

let current: LevelRewardData | null = null;

export function setLevelRewardsForTest(t: LevelRewardTier[] | null): void {
  if (process.env.NODE_ENV === 'production') throw new Error('운영에서는 단계표를 바꿀 수 없습니다');
  if (t === null) {
    current = null;
    return;
  }
  if (!current) loadLevelRewards();
  current = { ...(current as LevelRewardData), tiers: t };
}

/** 파일이 없거나 형식이 틀리면 던진다(보상 단계가 조용히 사라지면 안 된다) */
export function loadLevelRewards(dir: string = getConfig().gameDataDir): LevelRewardTier[] {
  const file = path.join(dir, 'level_rewards.json');
  if (!fs.existsSync(file)) throw new Error('level_rewards.json 이 없습니다');
  const { tiers, pass, errors } = validateLevelRewards(JSON.parse(fs.readFileSync(file, 'utf8')) as unknown, getGameData().itemIds);
  if (!tiers || !pass || errors.length > 0) throw new Error(`level_rewards.json 검증 실패: ${errors.join('; ')}`);
  current = { tiers, pass };
  return tiers;
}

export function getLevelRewards(): LevelRewardTier[] {
  if (!current) loadLevelRewards();
  return (current as LevelRewardData).tiers;
}

/** 성장 패스 가격과 단계별 보상 */
export function getGrowthPass(): GrowthPass {
  if (!current) loadLevelRewards();
  return (current as LevelRewardData).pass;
}
