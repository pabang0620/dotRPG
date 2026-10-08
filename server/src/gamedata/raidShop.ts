// 13단계: 레이드 상점(server/data/raid_shop.json, Docs/server/phase13_raid_rewards.md 5.4).
// 값의 원본은 Unity의 RaidShop 상수이고 내보내기가 파일을 만든다. 파일이 없으면 null(상점 503), 있는데 어긋나면 기동 실패.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { tierOfLevel } from '../utils/gearTier';
import type { EconomyData } from './economyData';

const RARITIES = ['Epic', 'Unique', 'Legendary'] as const;
export type ShopRarity = (typeof RARITIES)[number];
export const SHOP_CLASSES = ['warrior', 'mage'] as const;

const productSchema = z.looseObject({
  id: z.string().min(1).regex(/^[a-z][a-z0-9_]*$/),
  raidId: z.string().min(1),
  materialItem: z.string().min(1),
  price: z.number().int().positive(),
  category: z.enum(['Weapon', 'Top', 'Bottom', 'Necklace', 'Ring']),
  rarityWeights: z.partialRecord(z.enum(RARITIES), z.number().int().nonnegative()),
});
const fileSchema = z.looseObject({
  schema: z.literal(1),
  ratesVersion: z.string().min(1),
  products: z.array(productSchema).min(1),
});

export interface RaidShopProduct {
  id: string;
  raidId: string;
  raidName: string;
  materialItem: string;
  materialName: string;
  price: number;
  category: string;
  /** 레이드 권장 레벨(20, 40): 서버가 raidNumbers.recommendedLevel로 계산한다 */
  tierLevel: number;
  tier: number;
  rarityWeights: { rarity: ShopRarity; weight: number }[];
  /** 직업 -> 등급 -> 결과 장비 +0 id */
  results: Record<string, Partial<Record<ShopRarity, string>>>;
}
export interface RaidShopData {
  ratesVersion: string;
  products: Map<string, RaidShopProduct>;
}

export function loadRaidShop(dir: string, eco: EconomyData): RaidShopData | null {
  const full = path.join(dir, 'raid_shop.json');
  if (!fs.existsSync(full)) return null;
  const parsed = fileSchema.safeParse(JSON.parse(fs.readFileSync(full, 'utf8')) as unknown);
  if (!parsed.success) {
    const detail = parsed.error.issues.slice(0, 5).map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
    throw new Error(`게임 데이터 raid_shop.json 검증 실패: ${detail}`);
  }
  const products = new Map<string, RaidShopProduct>();
  for (const p of parsed.data.products) {
    const fail = (msg: string): never => {
      throw new Error(`게임 데이터 검증 실패: 레이드 상점 ${p.id} ${msg}`);
    };
    if (products.has(p.id)) fail('id 중복');
    const raid = eco.dungeons.byId.get(p.raidId);
    if (!raid || !raid.isRaid || !raid.raidNumbers) return fail(`raidId ${p.raidId} 가 레이드가 아닙니다`);
    const mat = eco.items.get(p.materialItem);
    if (!mat) return fail(`재료 ${p.materialItem} 이 items.json에 없습니다`);
    const weights = RARITIES.flatMap((r) => ((p.rarityWeights[r] ?? 0) > 0 ? [{ rarity: r, weight: p.rarityWeights[r] as number }] : []));
    if (weights.length === 0) fail('등급 가중치가 없습니다');
    const tier = tierOfLevel(raid.raidNumbers.recommendedLevel);
    const results: RaidShopProduct['results'] = {};
    for (const cls of SHOP_CLASSES) {
      results[cls] = {};
      for (const w of weights) {
        const found = eco.shop.equipmentList.filter(
          (e) => !e.bossOnly && e.levelTier === tier && e.category === p.category && e.rarity === w.rarity && (e.classOnly === null || e.classOnly === cls),
        );
        if (found.length !== 1) fail(`직업 ${cls} ${w.rarity} ${p.category} 장비가 정확히 하나가 아닙니다(${found.length}개)`);
        (results[cls] as Record<string, string>)[w.rarity] = (found[0] as { id: string }).id;
      }
    }
    products.set(p.id, {
      id: p.id,
      raidId: p.raidId,
      raidName: raid.name || raid.id,
      materialItem: p.materialItem,
      materialName: mat.name,
      price: p.price,
      category: p.category,
      tierLevel: raid.raidNumbers.recommendedLevel,
      tier,
      rarityWeights: weights,
      results,
    });
  }
  return { ratesVersion: parsed.data.ratesVersion, products };
}
