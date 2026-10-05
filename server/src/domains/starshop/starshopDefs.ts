// 캐시샵(별조각) 외형 뽑기 정의. 확률·가격·목록을 바꾸면 RATES_VERSION을 올리고 화면 확률표가 같이 바뀐다.
// 클라이언트 CosmeticCatalog의 id와 같아야 한다(외형 그림·색은 클라이언트가 가진다).

export type Rarity = 'common' | 'rare' | 'unique';

export const RATES_VERSION = '2026-10-05.4';

/** 등급 확률(만분율, 합 10000). 최상위는 유니크(전설 등급은 아직 열지 않는다) */
export const RARITY_WEIGHT: Record<Rarity, number> = { common: 7700, rare: 2000, unique: 300 };
export const RARITY_NAME: Record<Rarity, string> = { common: '일반', rare: '희귀', unique: '유니크' };
/** 장비 뽑기: 종류별 장비 분류(shop.json category) */
export type Banner = 'aura' | 'skin' | 'weapon' | 'armor' | 'accessory';
/** 스킨 뽑기: 유니크 등급 자리에 내 직업 스킨이 나오고(중복 방지), 나머지 등급은 오라 뽑기와 같다 */
export const SKIN_DUPLICATE_REFUND = 600;
export const GEAR_BANNERS: Record<Exclude<Banner, 'aura' | 'skin'>, { name: string; categories: string[] }> = {
  weapon: { name: '무기 뽑기', categories: ['Weapon'] },
  armor: { name: '방어구 뽑기', categories: ['Top', 'Bottom'] },
  accessory: { name: '장신구 뽑기', categories: ['Necklace', 'Ring'] },
};
/**
 * 장비 등급 확률(만분율, 합 10000). 커먼은 넣지 않는다(돈을 쓰는 뽑기라 최하 등급은 언커먼).
 * 그 뽑기에 없는 등급의 몫은 있는 등급 중 가장 낮은 등급으로 간다
 */
export const GEAR_RARITY_PERMILLE: [string, number][] = [
  ['Uncommon', 5500], ['Rare', 3000], ['Epic', 1120], ['Unique', 350], ['Legendary', 30],
];
export const GEAR_RATE_SCALE = 10000;
export const GEAR_RARITY_NAME: Record<string, string> = {
  Common: '커먼', Uncommon: '언커먼', Rare: '레어', Epic: '에픽', Unique: '유니크', Legendary: '레전더리',
};

/** 착용한 외형의 공격력 보너스(%): 오라는 등급별, 코스튬 스킨은 고정. 서버 화력 상한은 최대치만큼 넓힌다 */
export const AURA_DAMAGE: Record<Rarity, number> = { common: 1, rare: 2, unique: 3 };
export const SKIN_DAMAGE = 5;
export const COSMETIC_DAMAGE_MAX = AURA_DAMAGE.unique + SKIN_DAMAGE;

export const PULL_PRICE = 100;
/** 10회 묶음: 1,000개에 11회 */
export const TEN_PRICE = 1000;
export const TEN_COUNT = 11;
/**
 * 선택 게이지: 오라·스킨 뽑기 1회마다 1칸씩 찬다. 가득 차면 그 뽑기의 최상위(유니크 오라 / 내 직업 스킨) 중
 * 원하는 것 하나를 고른다(고르면 그만큼 줄어든다). 뽑기에서 자연히 나와도 게이지는 그대로다.
 */
export const AURA_GAUGE_MAX = 50;
export const SKIN_GAUGE_MAX = 100;
/** 예전 이름(오라 게이지) */
export const PITY_MAX = AURA_GAUGE_MAX;
/** 같은 등급 외형을 모두 가졌을 때 나온 중복은 별조각으로 돌려준다 */
export const DUPLICATE_REFUND: Record<Rarity, number> = { common: 20, rare: 50, unique: 200 };
/** 원하는 외형을 바로 얻는 확정 교환 가격 */
export const EXCHANGE_PRICE: Record<Rarity, number> = { common: 300, rare: 1000, unique: 5000 };

export interface CosmeticDef {
  id: string;
  name: string;
  rarity: Rarity;
  /** 코스튬 스킨(직업별 전신 외형): 뽑기에 들어가지 않고 확정 구매만 한다 */
  skin?: 'warrior' | 'mage';
}

/** 코스튬 스킨 확정 구매 가격 */
export const SKIN_PRICE = 3000;

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
  { id: 'aura_rainbow', name: '무지개 오라', rarity: 'unique' },
  { id: 'aura_gold', name: '황금 오라', rarity: 'unique' },
  { id: 'aura_abyss', name: '심연 오라', rarity: 'unique' },
  { id: 'skin_lion', name: '황금 사자 기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_moon', name: '월광 검귀', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_starnight', name: '성야의 마녀', rarity: 'unique', skin: 'mage' },
  { id: 'skin_crimson', name: '홍염의 마녀', rarity: 'unique', skin: 'mage' },
];

export const STAR_COSMETIC_BY_ID = new Map(STAR_COSMETICS.map((c) => [c.id, c] as const));

/** 뽑기 풀(스킨 제외) */
export function cosmeticsOf(rarity: Rarity): CosmeticDef[] {
  return STAR_COSMETICS.filter((c) => c.rarity === rarity && !c.skin);
}

export function exchangePriceOf(c: CosmeticDef): number {
  return c.skin ? SKIN_PRICE : EXCHANGE_PRICE[c.rarity];
}
