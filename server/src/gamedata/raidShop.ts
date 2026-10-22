// 13단계: 레이드 상점(server/data/raid_shop.json, Docs/server/phase13_raid_rewards.md 5.4).
// 값의 원본은 Unity의 RaidShop 상수이고 내보내기가 파일을 만든다. 파일이 없으면 null(상점 503), 있는데 어긋나면 기동 실패.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { tierOfLevel } from '../utils/gearTier';
import type { EconomyData } from './economyData';
import { CHARACTER_CLASSES, usableBy } from '../utils/characterClass';

const RARITIES = ['Epic', 'Unique', 'Legendary'] as const;
export type ShopRarity = (typeof RARITIES)[number];
export const SHOP_CLASSES = CHARACTER_CLASSES;

const productSchema = z.looseObject({
  id: z.string().min(1).regex(/^[a-z][a-z0-9_]*$/),
  raidId: z.string().min(1),
  materialItem: z.string().min(1),
  price: z.number().int().positive(),
  category: z.enum(['Weapon', 'Top', 'Bottom', 'Necklace', 'Ring']),
  rarityWeights: z.partialRecord(z.enum(RARITIES), z.number().int().nonnegative()),
  /** 레이드 권장 레벨과 다른 단계의 상자(챕터 3 지바크 레전더리 상자는 Lv.50). 없으면 레이드 권장 레벨 */
  tierLevel: z.number().int().positive().optional(),
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
  /** 상자 장비 레벨: 데이터 tierLevel, 없으면 레이드 권장 레벨(raidNumbers.recommendedLevel) */
  tierLevel: number;
  tier: number;
  rarityWeights: { rarity: ShopRarity; weight: number }[];
  /** 직업 -> 등급 -> 결과 장비 +0 id 목록(전설 무기는 광역형·토벌형 두 개, 같은 확률로 하나) */
  results: Record<string, Partial<Record<ShopRarity, string[]>>>;
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
    const tierLevel = p.tierLevel ?? raid.raidNumbers.recommendedLevel;
    const tier = tierOfLevel(tierLevel);
    const results: RaidShopProduct['results'] = {};
    for (const cls of SHOP_CLASSES) {
      results[cls] = {};
      for (const w of weights) {
        const found = eco.shop.equipmentList.filter(
          (e) => !e.bossOnly && e.levelTier === tier && e.category === p.category && e.rarity === w.rarity && usableBy(e, cls),
        );
        if (found.length === 0) fail(`직업 ${cls} ${w.rarity} ${p.category} 장비가 없습니다`);
        (results[cls] as Record<string, string[]>)[w.rarity] = found.map((e) => e.id);
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
      tierLevel,
      tier,
      rarityWeights: weights,
      results,
    });
  }
  return { ratesVersion: parsed.data.ratesVersion, products };
}
