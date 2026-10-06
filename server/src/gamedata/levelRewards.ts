// 레벨 달성 보상 단계표(server/data/level_rewards.json, Docs/server/phase13_level_rewards.md 1절).
// star_products.json 과 같은 방식: 서버 정본, data_version 해시에 넣지 않는다(클라이언트는 GET /level-rewards 로 받는다).
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { getConfig } from '../config/env';

const tierSchema = z.strictObject({ level: z.number().int().min(1).max(999), stars: z.number().int().positive() });
const fileSchema = z.looseObject({ tiers: z.array(tierSchema).min(1) });

export type LevelRewardTier = z.infer<typeof tierSchema>;

/** 검증 오류 목록(빈 배열 = 정상) */
export function validateLevelRewards(raw: unknown): { tiers: LevelRewardTier[] | null; errors: string[] } {
  const p = fileSchema.safeParse(raw);
  if (!p.success) return { tiers: null, errors: p.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`) };
  const errors: string[] = [];
  const seen = new Set<number>();
  for (const t of p.data.tiers) {
    if (seen.has(t.level)) errors.push(`level 중복: ${t.level}`);
    seen.add(t.level);
  }
  return { tiers: [...p.data.tiers].sort((a, b) => a.level - b.level), errors };
}

let current: LevelRewardTier[] | null = null;

export function setLevelRewardsForTest(t: LevelRewardTier[] | null): void {
  if (process.env.NODE_ENV === 'production') throw new Error('운영에서는 단계표를 바꿀 수 없습니다');
  current = t;
}

/** 파일이 없거나 형식이 틀리면 던진다(보상 단계가 조용히 사라지면 안 된다) */
export function loadLevelRewards(dir: string = getConfig().gameDataDir): LevelRewardTier[] {
  const file = path.join(dir, 'level_rewards.json');
  if (!fs.existsSync(file)) throw new Error('level_rewards.json 이 없습니다');
  const { tiers, errors } = validateLevelRewards(JSON.parse(fs.readFileSync(file, 'utf8')) as unknown);
  if (!tiers || errors.length > 0) throw new Error(`level_rewards.json 검증 실패: ${errors.join('; ')}`);
  current = tiers;
  return tiers;
}

export function getLevelRewards(): LevelRewardTier[] {
  return current ?? loadLevelRewards();
}
