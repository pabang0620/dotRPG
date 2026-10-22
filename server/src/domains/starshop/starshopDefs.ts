// 캐시샵(별조각) 뽑기 정의. 확률·가격·목록을 바꾸면 RATES_VERSION을 올리고 화면 확률표가 같이 바뀐다.
// 클라이언트 CosmeticCatalog의 id와 같아야 한다(외형 그림·색은 클라이언트가 가진다).
//
// 등급: 일반 < 희귀 < 에픽 < 유니크 (< 전설: 추후 추가 자리). 설계 원칙(Docs/PLAN_MONETIZATION.md):
//   - 최상위(유니크 스킨)는 확률로는 거의 나오지 않고(0.02%) 선택 게이지가 주 획득 경로다.
//   - 한 단계 아래(에픽)는 1%로 "뽑는 맛"을 맡는다. 일반·희귀는 중복 방지와 별조각 환급으로 손해감을 줄인다.
//   - 스킨 뽑기는 모든 등급이 내 직업 스킨이다(일반·희귀 스킨은 기본 몸체 색 바꾸기, 2026-10-14).
//   - 10+1의 보너스 1회: 오라·스킨 뽑기는 희귀 이상, 장비 뽑기는 일반 1회와 같은 확률.
import type { CharacterClassId } from '../../utils/characterClass';

export type Rarity = 'common' | 'rare' | 'epic' | 'unique';

export const RATES_VERSION = '2026-10-23.1'; // 2026-10-23.1: 도적 스킨 33종(뽑기 32 + 교환소 전용 1)과 도적 장비 뽑기 표
// 이전: 2026-10-14.3 컬렉션 세트 9종 추가, 교환소 전용 외형은 뽑기에서 제외(0044)

/** 확률 단위: 십만분율(0.001% 단위) */
export const RATE_SCALE = 100_000;
export const RARITY_NAME: Record<Rarity, string> = { common: '일반', rare: '희귀', epic: '에픽', unique: '유니크' };

export type Banner = 'aura' | 'skin' | 'weapon' | 'armor' | 'accessory';
export type CosmeticBanner = 'aura' | 'skin';

/** 외형 뽑기 등급 확률(십만분율, 합 100000) */
export const BANNER_WEIGHT: Record<CosmeticBanner, Record<Rarity, number>> = {
  // 오라: 에픽 오라 1%, 희귀 18%, 일반 81%
  aura: { unique: 0, epic: 1_000, rare: 18_000, common: 81_000 },
  // 스킨: 유니크 스킨 0.02%, 에픽 스킨 1%, 희귀 스킨 18%, 일반 스킨 80.98%(전부 내 직업 스킨)
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
/**
 * 장비 뽑기 천장(0033, Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A). 장비 뽑기(세 종류 합산) 1회마다 +1, max가 차면
 * 고른 뽑기·단계의 유니크 장비 하나와 교환한다(그 단계에 유니크가 없으면 가장 높은 등급).
 * max = 유니크 이상 확률(45 + 5 = 50/10000, 기대 200회)의 약 1.5배 = 300회(별조각 30,000).
 */
export const GEAR_CEILING = { max: 300, rarity: 'unique' } as const;
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
/**
 * 이미 가진 외형이 또 나오면 여분으로 쌓인다(합성 재료). 여분은 언제든 이 값으로 분해해 별조각으로 바꿀 수 있다
 * (예전 중복 환급과 같은 값이라, 합성을 하지 않아도 손해가 없다)
 */
export const DUPLICATE_REFUND: Record<Rarity, number> = { common: 20, rare: 50, epic: 300, unique: 1500 };
export const DISMANTLE_STARS = DUPLICATE_REFUND;

/**
 * 합성: 같은 등급 여분 4개 -> 일정 확률로 한 등급 위 외형 하나. 실패하면 넣은 것 중 1개를 돌려준다.
 * 연속 실패가 pity번 쌓이면 다음 합성은 성공한다(0 = 천장 없음). 확률은 천분율.
 *   일반 -> 희귀(오라 또는 내 직업 희귀 스킨) 40%, 희귀 -> 에픽(오라 또는 내 직업 에픽 스킨) 10%(15회 실패 뒤 확정),
 *   에픽 -> 내 직업 유니크 스킨 3%(10회 실패 뒤 확정)
 */
export const SYNTH_COUNT = 4;
export const SYNTH: Record<'common' | 'rare' | 'epic', { to: Rarity; permille: number; pity: number }> = {
  common: { to: 'rare', permille: 400, pity: 0 },
  rare: { to: 'epic', permille: 100, pity: 15 },
  epic: { to: 'unique', permille: 30, pity: 10 },
};
export type SynthFrom = keyof typeof SYNTH;

/**
 * 컬렉션: 세트의 외형을 모두 가지면 등록할 수 있고, 등록하면 계정 전체에 능력치가 붙는다(외형은 소모하지 않는다).
 * attack/health는 %. "옷장의 주인"은 다른 다섯 세트를 모두 등록해야 열린다
 */
export interface CollectionDef {
  id: string;
  name: string;
  members: string[];
  requires?: string[];
  attack: number;
  health: number;
}
export const COLLECTIONS: readonly CollectionDef[] = [
  { id: 'col_dawn', name: '새벽빛 오라', members: ['aura_dew', 'aura_maple', 'aura_blossom', 'aura_forest', 'aura_ash'], attack: 0, health: 2 },
  { id: 'col_starlight', name: '별빛 오라', members: ['aura_sunset', 'aura_violet', 'aura_frost', 'aura_rose', 'aura_jade'], attack: 1, health: 0 },
  { id: 'col_legend', name: '찬란한 오라', members: ['aura_rainbow', 'aura_gold', 'aura_abyss'], attack: 1, health: 2 },
  { id: 'col_sword', name: '검사의 의상실', members: ['skin_maple', 'skin_obsidian', 'skin_lion', 'skin_moon'], attack: 1, health: 2 },
  { id: 'col_magic', name: '마법사의 의상실', members: ['skin_forest', 'skin_ice', 'skin_starnight', 'skin_crimson'], attack: 1, health: 2 },
  { id: 'col_master', name: '옷장의 주인', members: [], requires: ['col_dawn', 'col_starlight', 'col_legend', 'col_sword', 'col_magic'], attack: 0, health: 3 },
  // 0044 확장 세트: 늘어난 외형을 5~6개씩 묶는다. 전체 상한 공격 +8%, 체력 +20%(COLLECTION_*_MAX)
  { id: 'col_mist', name: '물안개 오라', members: ['aura_mint', 'aura_peach', 'aura_lemon', 'aura_lilac', 'aura_sea'], attack: 0, health: 2 },
  { id: 'col_meadow', name: '들판 오라', members: ['aura_sand', 'aura_berry', 'aura_moss', 'aura_cloud', 'aura_coral'], attack: 0, health: 1 },
  { id: 'col_twilight', name: '별자리 오라', members: ['aura_comet', 'aura_aurora', 'aura_amber', 'aura_sapphire', 'aura_ruby'], attack: 1, health: 0 },
  { id: 'col_jewel', name: '보석빛 오라', members: ['aura_pearl', 'aura_lavender', 'aura_ember', 'aura_lagoon', 'aura_dusk'], attack: 0, health: 2 },
  { id: 'col_celestial', name: '천상의 오라', members: ['aura_prism', 'aura_starfall', 'aura_sakura', 'aura_thunder', 'aura_deepsea', 'aura_spectrum'], attack: 1, health: 0 },
  { id: 'col_knight', name: '기사단 의상실', members: ['skin_w_violet', 'skin_w_jade', 'skin_w_scarlet', 'skin_w_frost', 'skin_w_sunguard'], attack: 0, health: 2 },
  { id: 'col_witch', name: '마녀의 옷장', members: ['skin_m_sky', 'skin_m_cherry', 'skin_m_mint', 'skin_m_amber', 'skin_m_ocean'], attack: 0, health: 2 },
  { id: 'col_valor', name: '용맹의 갑주', members: ['skin_w_valkyrie', 'skin_w_snowblade', 'skin_w_dragonscale', 'skin_w_roseknight'], attack: 1, health: 0 },
  { id: 'col_arcana', name: '비전의 로브', members: ['skin_m_moonshrine', 'skin_m_storm', 'skin_m_sakura', 'skin_m_frostprincess'], attack: 1, health: 0 },
  { id: 'col_shade', name: '밀정의 옷장', members: ['skin_r_crimson', 'skin_r_violet', 'skin_r_frost', 'skin_r_jade', 'skin_r_azure'], attack: 0, health: 2 },
  { id: 'col_blades', name: '칼춤의 의상', members: ['skin_r_sakura', 'skin_r_moonshade', 'skin_r_raven', 'skin_r_foxmask'], attack: 1, health: 0 },
];
export const COLLECTION_BY_ID = new Map(COLLECTIONS.map((c) => [c.id, c] as const));
export const COLLECTION_ATTACK_MAX = COLLECTIONS.reduce((a, c) => a + c.attack, 0);
export const COLLECTION_HEALTH_MAX = COLLECTIONS.reduce((a, c) => a + c.health, 0);

// ---------------- 0044 캐시 아이템 가치(별의 결정, 별빛 교환소, 장비 도감) ----------------

/** 외형 여분 분해 시 별조각(DISMANTLE_STARS)에 더해 주는 별의 결정 */
export const DISMANTLE_CRYSTALS: Record<Rarity, number> = { common: 4, rare: 12, epic: 60, unique: 300 };
/** 컬렉션에 등록한 세트의 외형 여분은 결정 +20% */
export const COLLECTION_DUP_BONUS = 0.2;
/** 뽑기 장비 분해(등급: 장비 데이터의 rarity). 결정 + 강화 재료. 강화 단계마다 결정 +5% */
export const GEAR_DISMANTLE: Record<string, { crystals: number; items: [string, number][] }> = {
  Common: { crystals: 2, items: [['mat_bone', 1]] },
  Uncommon: { crystals: 3, items: [['mat_bone', 2]] },
  Rare: { crystals: 8, items: [['mat_essence', 1]] },
  Epic: { crystals: 30, items: [['mat_essence', 3]] },
  Unique: { crystals: 150, items: [] },
  Legendary: { crystals: 600, items: [] },
};
export const GEAR_DISMANTLE_ENHANCE_BONUS = 0.05;

/** 별빛 교환소 품목. weekly = 주간 재고(0 = 제한 없음). pick = 고르는 외형의 조건 */
export interface ExchangeGood {
  id: string;
  name: string;
  price: number;
  weekly: number;
  kind: 'cosmetic' | 'skin_gauge' | 'gear_gauge' | 'item';
  pick?: { rarity: Rarity; skin: boolean | null };
  itemId?: string;
  count?: number;
}
export const EXCHANGE_GOODS: readonly ExchangeGood[] = [
  { id: 'pick_rare', name: '원하는 희귀 오라·스킨', price: 150, weekly: 0, kind: 'cosmetic', pick: { rarity: 'rare', skin: null } },
  { id: 'pick_epic_aura', name: '원하는 에픽 오라', price: 500, weekly: 0, kind: 'cosmetic', pick: { rarity: 'epic', skin: false } },
  { id: 'pick_epic_skin', name: '원하는 에픽 스킨', price: 700, weekly: 0, kind: 'cosmetic', pick: { rarity: 'epic', skin: true } },
  { id: 'skin_gauge10', name: '스킨 선택 게이지 +10칸', price: 120, weekly: 1, kind: 'skin_gauge', count: 10 },
  { id: 'gear_gauge10', name: '장비 뽑기 천장 +10칸', price: 100, weekly: 3, kind: 'gear_gauge', count: 10 },
  { id: 'protect_ticket', name: '강화 보호권', price: 80, weekly: 5, kind: 'item', itemId: 'ticket_protect', count: 1 },
  { id: 'dungeon_reset', name: '요일 던전 초기화권', price: 60, weekly: 2, kind: 'item', itemId: 'ticket_dungeon_reset', count: 1 },
  { id: 'essence10', name: '정수 10개', price: 40, weekly: 10, kind: 'item', itemId: 'mat_essence', count: 10 },
  { id: 'ex_aura_crystal', name: '결정빛 오라(교환소 전용)', price: 400, weekly: 0, kind: 'cosmetic', itemId: 'aura_crystal' },
  { id: 'ex_aura_nova', name: '신성 폭발 오라(교환소 전용)', price: 600, weekly: 0, kind: 'cosmetic', itemId: 'aura_nova' },
  { id: 'ex_skin_starwarden', name: '별빛 수호 기사(교환소 전용)', price: 900, weekly: 0, kind: 'cosmetic', itemId: 'skin_w_starwarden' },
  { id: 'ex_skin_starsage', name: '별의 현자(교환소 전용)', price: 900, weekly: 0, kind: 'cosmetic', itemId: 'skin_m_starsage' },
  { id: 'ex_skin_starshade', name: '별빛 그림자(교환소 전용)', price: 900, weekly: 0, kind: 'cosmetic', itemId: 'skin_r_starshade' },
];
export const EXCHANGE_GOOD_BY_ID = new Map(EXCHANGE_GOODS.map((g) => [g.id, g] as const));

/** 장비 도감: 처음 얻은 유니크·레전더리 기본 id 하나마다 계정 체력 +0.5%, 상한 +10% */
export const CODEX_RARITIES = ['Unique', 'Legendary'] as const;
export const CODEX_HEALTH_PER = 0.5;
export const CODEX_HEALTH_MAX = 10;
/** 원하는 외형을 바로 얻는 확정 교환 가격. 0 = 교환 불가(유니크 스킨은 선택 게이지로만) */
export const EXCHANGE_PRICE: Record<Rarity, number> = { common: 300, rare: 1000, epic: 3000, unique: 0 };
export const EPIC_SKIN_PRICE = 4000;
/** 일반·희귀 스킨 확정 교환 가격(오라와 같은 값) */
export const LOW_SKIN_PRICE: Record<'common' | 'rare', number> = { common: EXCHANGE_PRICE.common, rare: EXCHANGE_PRICE.rare };

export interface CosmeticDef {
  id: string;
  name: string;
  rarity: Rarity;
  /** 코스튬 스킨(직업별 전신 외형) */
  skin?: CharacterClassId;
  /** 별빛 교환소에서만 얻는 외형(뽑기·합성·확정 교환에 나오지 않는다, 0044) */
  exchangeOnly?: boolean;
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
  { id: 'skin_w_sand', name: '모래빛 여행복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_moss', name: '숲지기 옷', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_rust', name: '적갈 견습복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_m_ash', name: '잿빛 견습 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_pilgrim', name: '순례자의 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_sprout', name: '새싹 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_w_crimson', name: '진홍 용병복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_amethyst', name: '자수정 기사복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_brass', name: '황동 경비복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_m_rose', name: '장밋빛 로브', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_emerald', name: '에메랄드 로브', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_sunset', name: '노을 로브', rarity: 'rare', skin: 'mage' },
  { id: 'skin_maple', name: '단풍 무사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_obsidian', name: '흑요 기사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_noir', name: '칠흑의 검희', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_forest', name: '숲의 정령', rarity: 'epic', skin: 'mage' },
  { id: 'skin_ice', name: '얼음 여왕', rarity: 'epic', skin: 'mage' },
  { id: 'skin_lion', name: '황금 사자 기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_moon', name: '월광 검귀', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_starnight', name: '성야의 마녀', rarity: 'unique', skin: 'mage' },
  { id: 'skin_crimson', name: '홍염의 마녀', rarity: 'unique', skin: 'mage' },
  // 2026-10-14 추가: 오라 26종, 생성 여성 스킨 42종
  { id: 'aura_mint', name: '민트빛 오라', rarity: 'common' },
  { id: 'aura_peach', name: '복숭아빛 오라', rarity: 'common' },
  { id: 'aura_lemon', name: '레몬빛 오라', rarity: 'common' },
  { id: 'aura_lilac', name: '라일락빛 오라', rarity: 'common' },
  { id: 'aura_sea', name: '바닷빛 오라', rarity: 'common' },
  { id: 'aura_sand', name: '모래빛 오라', rarity: 'common' },
  { id: 'aura_berry', name: '산딸기빛 오라', rarity: 'common' },
  { id: 'aura_moss', name: '이끼빛 오라', rarity: 'common' },
  { id: 'aura_cloud', name: '구름빛 오라', rarity: 'common' },
  { id: 'aura_coral', name: '산호빛 오라', rarity: 'common' },
  { id: 'aura_comet', name: '혜성별 오라', rarity: 'rare' },
  { id: 'aura_aurora', name: '극광별 오라', rarity: 'rare' },
  { id: 'aura_amber', name: '호박별 오라', rarity: 'rare' },
  { id: 'aura_sapphire', name: '사파이어별 오라', rarity: 'rare' },
  { id: 'aura_ruby', name: '루비별 오라', rarity: 'rare' },
  { id: 'aura_pearl', name: '진주별 오라', rarity: 'rare' },
  { id: 'aura_lavender', name: '라벤더별 오라', rarity: 'rare' },
  { id: 'aura_ember', name: '불씨별 오라', rarity: 'rare' },
  { id: 'aura_lagoon', name: '석호별 오라', rarity: 'rare' },
  { id: 'aura_dusk', name: '황혼별 오라', rarity: 'rare' },
  { id: 'aura_prism', name: '프리즘 오라', rarity: 'epic' },
  { id: 'aura_starfall', name: '유성우 오라', rarity: 'epic' },
  { id: 'aura_sakura', name: '벚꽃 오라', rarity: 'epic' },
  { id: 'aura_thunder', name: '천둥 오라', rarity: 'epic' },
  { id: 'aura_deepsea', name: '심해 오라', rarity: 'epic' },
  { id: 'aura_spectrum', name: '스펙트럼 오라', rarity: 'epic' },
  { id: 'skin_w_ranger', name: '숲 순찰대복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_sailor', name: '견습 선원복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_farmer', name: '들녘 일꾼복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_scout', name: '정찰병 가죽옷', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_miner', name: '광산 작업복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_smith', name: '대장간 앞치마', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_wanderer', name: '방랑 검객 망토', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_bluecoat', name: '청기사 견습복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_violet', name: '보랏빛 결투복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_jade', name: '비취 근위복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_scarlet', name: '진홍 검무복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_frost', name: '설원 순찰복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_sunguard', name: '태양 기사단복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_shade', name: '그림자 자객복', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_valkyrie', name: '발키리 갑주', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_snowblade', name: '백설 무사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_dragonscale', name: '용비늘 갑주', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_roseknight', name: '장미 기사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_phoenix', name: '불사조 성기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_w_aurora', name: '오로라 여제', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_w_voidblade', name: '심연의 검성', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_m_lavender', name: '라벤더 후드 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_student', name: '마법학교 교복', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_herbalist', name: '약초꾼 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_tea', name: '찻잎 견습 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_snowcloak', name: '눈송이 망토', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_autumn', name: '가을 낙엽 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_dusk', name: '밤하늘 견습 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_sky', name: '하늘빛 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_cherry', name: '벚꽃 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_mint', name: '민트 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_amber', name: '호박빛 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_ocean', name: '바다 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_lilac', name: '라일락 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_candy', name: '사탕 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_moonshrine', name: '달빛 무녀', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_storm', name: '뇌운의 마녀', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_sakura', name: '벚꽃 무녀', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_frostprincess', name: '서리 공주', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_celestial', name: '천공의 대마녀', rarity: 'unique', skin: 'mage' },
  { id: 'skin_m_flameempress', name: '화염 여제', rarity: 'unique', skin: 'mage' },
  { id: 'skin_m_dream', name: '몽환의 마녀', rarity: 'unique', skin: 'mage' },
  // 2026-10-23 도적 스킨 33종(Docs/PLAN_CLASS_ROGUE.md 6.1절): 일반 10, 희귀 10, 에픽 7, 유니크 5 + 교환소 전용 에픽 1(아래)
  { id: 'skin_r_navy', name: '남색 밀정복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_wine', name: '와인빛 밀정복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_moss', name: '숲길 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_sand', name: '사막 행상복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_slate', name: '회청 잠행복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_rust', name: '적갈 사냥꾼복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_ivory', name: '상아 견습복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_plum', name: '자두빛 두건복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_teal', name: '청록 밀수꾼복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_coal', name: '숯빛 야행복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_crimson', name: '진홍 그림자복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_violet', name: '보랏빛 야행복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_gold', name: '황금 도둑복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_frost', name: '서리 밀정복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_rose', name: '장미 가면복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_jade', name: '비취 독사복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_ember', name: '불씨 두건복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_azure', name: '하늘빛 바람복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_onyx', name: '흑요 암살복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_lilac', name: '라일락 무희복', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_sakura', name: '벚꽃 쿠노이치', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_moonshade', name: '달그림자 밀정', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_dunedancer', name: '모래바람 칼춤꾼', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_corsair', name: '바다의 칼잡이', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_raven', name: '까마귀 깃 암살자', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_foxmask', name: '여우 가면 무희', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_frosthunter', name: '서리 사냥꾼', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_nightqueen', name: '칠흑의 그림자 여왕', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_thunder', name: '뇌신 닌자', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_crimsonmoon', name: '진홍 달의 처형자', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_butterfly', name: '비단 나비 무희', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_scorpion', name: '황금 전갈', rarity: 'unique', skin: 'rogue' },
  // 0044 별빛 교환소 전용(시즌 1): 별의 결정으로만 얻는다
  { id: 'aura_crystal', name: '결정빛 오라', rarity: 'epic', exchangeOnly: true },
  { id: 'aura_nova', name: '신성 폭발 오라', rarity: 'epic', exchangeOnly: true },
  { id: 'skin_w_starwarden', name: '별빛 수호 기사', rarity: 'epic', skin: 'warrior', exchangeOnly: true },
  { id: 'skin_m_starsage', name: '별의 현자', rarity: 'epic', skin: 'mage', exchangeOnly: true },
  { id: 'skin_r_starshade', name: '별빛 그림자', rarity: 'epic', skin: 'rogue', exchangeOnly: true },
];

export const STAR_COSMETIC_BY_ID = new Map(STAR_COSMETICS.map((c) => [c.id, c] as const));

/** 오라(스킨 제외) 중 한 등급 */
export function cosmeticsOf(rarity: Rarity): CosmeticDef[] {
  return STAR_COSMETICS.filter((c) => c.rarity === rarity && !c.skin && !c.exchangeOnly);
}

/** 뽑기·합성·확정 교환에 나오는 외형(교환소 전용 제외) */
export const GACHA_COSMETICS: readonly CosmeticDef[] = STAR_COSMETICS.filter((c) => !c.exchangeOnly);

/** 확정 교환 가격(0 = 교환 불가) */
export function exchangePriceOf(c: CosmeticDef): number {
  if (c.exchangeOnly) return 0;
  if (c.skin) return c.rarity === 'epic' ? EPIC_SKIN_PRICE : c.rarity === 'unique' ? 0 : LOW_SKIN_PRICE[c.rarity];
  return EXCHANGE_PRICE[c.rarity];
}
