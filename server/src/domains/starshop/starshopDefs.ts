// 캐시샵(별조각) 외형 뽑기 정의. 확률·가격·목록을 바꾸면 RATES_VERSION을 올리고 화면 확률표가 같이 바뀐다.
// 클라이언트 CosmeticCatalog의 id와 같아야 한다(외형 그림·색은 클라이언트가 가진다).

export type Rarity = 'common' | 'rare' | 'legend';

export const RATES_VERSION = '2026-10-05.1';

/** 등급 확률(만분율, 합 10000) */
export const RARITY_WEIGHT: Record<Rarity, number> = { common: 7700, rare: 2000, legend: 300 };
export const RARITY_NAME: Record<Rarity, string> = { common: '일반', rare: '희귀', legend: '전설' };

export const PULL_PRICE = 100;
/** 10회 묶음: 1,000개에 11회 */
export const TEN_PRICE = 1000;
export const TEN_COUNT = 11;
/** 전설 없이 PITY_MAX - 1회를 뽑으면 PITY_MAX번째는 전설 확정 */
export const PITY_MAX = 50;
/** 같은 등급 외형을 모두 가졌을 때 나온 중복은 별조각으로 돌려준다 */
export const DUPLICATE_REFUND: Record<Rarity, number> = { common: 20, rare: 50, legend: 200 };
/** 원하는 외형을 바로 얻는 확정 교환 가격 */
export const EXCHANGE_PRICE: Record<Rarity, number> = { common: 300, rare: 1000, legend: 5000 };

export interface CosmeticDef {
  id: string;
  name: string;
  rarity: Rarity;
}

export const STAR_COSMETICS: readonly CosmeticDef[] = [
  { id: 'aura_dew', name: '이슬빛 오라', rarity: 'common' },
  { id: 'aura_maple', name: '단풍빛 오라', rarity: 'common' },
  { id: 'aura_blossom', name: '꽃잎빛 오라', rarity: 'common' },
  { id: 'aura_forest', name: '숲빛 오라', rarity: 'common' },
  { id: 'aura_ash', name: '잿빛 오라', rarity: 'common' },
  { id: 'aura_sunset', name: '노을빛 오라', rarity: 'rare' },
  { id: 'aura_violet', name: '별빛 오라', rarity: 'rare' },
  { id: 'aura_frost', name: '서리별 오라', rarity: 'rare' },
  { id: 'aura_rose', name: '장미별 오라', rarity: 'rare' },
  { id: 'aura_jade', name: '비취별 오라', rarity: 'rare' },
  { id: 'aura_rainbow', name: '무지개 오라', rarity: 'legend' },
  { id: 'aura_gold', name: '황금 오라', rarity: 'legend' },
  { id: 'aura_abyss', name: '심연 오라', rarity: 'legend' },
];

export const STAR_COSMETIC_BY_ID = new Map(STAR_COSMETICS.map((c) => [c.id, c] as const));

export function cosmeticsOf(rarity: Rarity): CosmeticDef[] {
  return STAR_COSMETICS.filter((c) => c.rarity === rarity);
}
