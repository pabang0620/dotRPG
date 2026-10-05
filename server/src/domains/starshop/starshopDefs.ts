// 캐시샵(별조각) 뽑기 정의. 확률·가격·목록을 바꾸면 RATES_VERSION을 올리고 화면 확률표가 같이 바뀐다.
// 클라이언트 CosmeticCatalog의 id와 같아야 한다(외형 그림·색은 클라이언트가 가진다).
//
// 등급: 일반 < 희귀 < 에픽 < 유니크 (< 전설: 추후 추가 자리). 설계 원칙(Docs/PLAN_MONETIZATION.md):
//   - 최상위(유니크 스킨)는 확률로는 거의 나오지 않고(0.02%) 선택 게이지가 주 획득 경로다.
//   - 한 단계 아래(에픽)는 1%로 "뽑는 맛"을 맡는다. 일반·희귀는 중복 방지와 별조각 환급으로 손해감을 줄인다.
//   - 10+1의 보너스 1회는 희귀 이상(장비는 최하 등급 제외)으로 묶음 구매의 가치를 분명히 한다.

export type Rarity = 'common' | 'rare' | 'epic' | 'unique';

export const RATES_VERSION = '2026-10-05.5';

/** 확률 단위: 십만분율(0.001% 단위) */
export const RATE_SCALE = 100_000;
export const RARITY_NAME: Record<Rarity, string> = { common: '일반', rare: '희귀', epic: '에픽', unique: '유니크' };

export type Banner = 'aura' | 'skin' | 'weapon' | 'armor' | 'accessory';
export type CosmeticBanner = 'aura' | 'skin';

/** 외형 뽑기 등급 확률(십만분율, 합 100000) */
export const BANNER_WEIGHT: Record<CosmeticBanner, Record<Rarity, number>> = {
  // 오라: 에픽 오라 1%, 희귀 18%, 일반 81%
  aura: { unique: 0, epic: 1_000, rare: 18_000, common: 81_000 },
  // 스킨: 유니크 스킨 0.02%, 에픽 스킨 1%, 희귀 오라 18%, 일반 오라 80.98%
  skin: { unique: 20, epic: 1_000, rare: 18_000, common: 80_980 },
};

/** 선택 게이지(1회 1칸): 가득 차면 그 뽑기의 목표 등급 중 하나를 직접 고른다 */
export const GAUGE: Record<CosmeticBanner, { max: number; rarity: Rarity }> = {
  aura: { max: 50, rarity: 'epic' },
  skin: { max: 150, rarity: 'unique' },
};
export const AURA_GAUGE_MAX = GAUGE.aura.max;
export const SKIN_GAUGE_MAX = GAUGE.skin.max;
/** 예전 이름(오라 게이지) */
export const PITY_MAX = AURA_GAUGE_MAX;

export const GEAR_BANNERS: Record<Exclude<Banner, CosmeticBanner>, { name: string; categories: string[] }> = {
  weapon: { name: '무기 뽑기', categories: ['Weapon'] },
  armor: { name: '방어구 뽑기', categories: ['Top', 'Bottom'] },
  accessory: { name: '장신구 뽑기', categories: ['Necklace', 'Ring'] },
};
/**
 * 장비 등급 확률(만분율, 합 10000). 커먼은 넣지 않는다(돈을 쓰는 뽑기라 최하 등급은 언커먼).
 * 그 뽑기에 없는 등급의 몫은 있는 등급 중 가장 낮은 등급으로 간다
 */
export const GEAR_RARITY_PERMILLE: [string, number][] = [
  ['Uncommon', 6200], ['Rare', 3000], ['Epic', 750], ['Unique', 45], ['Legendary', 5],
];
export const GEAR_RATE_SCALE = 10000;
export const GEAR_RARITY_NAME: Record<string, string> = {
  Common: '커먼', Uncommon: '언커먼', Rare: '레어', Epic: '에픽', Unique: '유니크', Legendary: '레전더리',
};

/** 착용한 외형의 공격력 보너스(%): 오라 일반 1·희귀 2·에픽 3, 스킨 에픽 4·유니크 5. 서버 화력 상한은 최대치만큼 넓힌다 */
export const AURA_DAMAGE: Record<Rarity, number> = { common: 1, rare: 2, epic: 3, unique: 3 };
export const SKIN_DAMAGE: Record<Rarity, number> = { common: 0, rare: 0, epic: 4, unique: 5 };
export const COSMETIC_DAMAGE_MAX = AURA_DAMAGE.epic + SKIN_DAMAGE.unique;

export const PULL_PRICE = 100;
/** 10회 묶음: 1,000개에 11회 */
export const TEN_PRICE = 1000;
export const TEN_COUNT = 11;
/** 같은 등급을 모두 가졌을 때 나온 중복은 별조각으로 돌려준다 */
export const DUPLICATE_REFUND: Record<Rarity, number> = { common: 20, rare: 50, epic: 300, unique: 1500 };
/** 원하는 외형을 바로 얻는 확정 교환 가격. 0 = 교환 불가(유니크 스킨은 선택 게이지로만) */
export const EXCHANGE_PRICE: Record<Rarity, number> = { common: 300, rare: 1000, epic: 3000, unique: 0 };
export const EPIC_SKIN_PRICE = 4000;

export interface CosmeticDef {
  id: string;
  name: string;
  rarity: Rarity;
  /** 코스튬 스킨(직업별 전신 외형) */
  skin?: 'warrior' | 'mage';
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
  { id: 'aura_rainbow', name: '무지개 오라', rarity: 'epic' },
  { id: 'aura_gold', name: '황금 오라', rarity: 'epic' },
  { id: 'aura_abyss', name: '심연 오라', rarity: 'epic' },
  { id: 'skin_maple', name: '단풍 무사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_obsidian', name: '흑요 기사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_forest', name: '숲의 정령', rarity: 'epic', skin: 'mage' },
  { id: 'skin_ice', name: '얼음 여왕', rarity: 'epic', skin: 'mage' },
  { id: 'skin_lion', name: '황금 사자 기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_moon', name: '월광 검귀', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_starnight', name: '성야의 마녀', rarity: 'unique', skin: 'mage' },
  { id: 'skin_crimson', name: '홍염의 마녀', rarity: 'unique', skin: 'mage' },
];

export const STAR_COSMETIC_BY_ID = new Map(STAR_COSMETICS.map((c) => [c.id, c] as const));

/** 오라(스킨 제외) 중 한 등급 */
export function cosmeticsOf(rarity: Rarity): CosmeticDef[] {
  return STAR_COSMETICS.filter((c) => c.rarity === rarity && !c.skin);
}

/** 확정 교환 가격(0 = 교환 불가) */
export function exchangePriceOf(c: CosmeticDef): number {
  if (c.skin) return c.rarity === 'epic' ? EPIC_SKIN_PRICE : 0;
  return EXCHANGE_PRICE[c.rarity];
}
