// 캐시샵(별조각) 뽑기 정의. 확률·가격·목록을 바꾸면 RATES_VERSION을 올리고 화면 확률표가 같이 바뀐다.
// 클라이언트 CosmeticCatalog의 id와 같아야 한다(외형 그림·색은 클라이언트가 가진다).
//
// 등급: 일반 < 희귀 < 에픽 < 유니크 < 전설(Docs/PLAN_SKIN_TIERS_2026-10-23.md). 등급마다 "히든"이 있다(같은 등급 안에서
// 유독 잘 뽑힌 디자인, 능력치도 더 높다). 설계 원칙:
//   - 스킨 뽑기는 모든 등급이 내 직업 스킨이다. 등급 확률은 그 등급 히든을 포함한 합이고, 히든 1종 몫은 같은 등급 보통
//     1종 몫의 1/5(전설 히든만 0.006% 고정).
//   - 최상위(유니크·전설·히든)는 확률보다 선택 게이지가 주 획득 경로다. 천장은 CEILING 한 곳에서 바꾼다.
//   - 10+1의 보너스 1회: 오라·스킨 뽑기는 "일반(보통)"만 빼고 나머지 칸이 같은 비율로 커진다(일반 히든은 남는다).
//     장비 뽑기는 일반 1회와 같은 확률.
//   - retired: 풀에서 뺀 외형(그림 보관). 뽑기·합성·확정 교환·교환소 선택에 나오지 않고, 이미 가진 사람은 계속 입는다.
import type { CharacterClassId } from '../../utils/characterClass';

export type Rarity = 'common' | 'rare' | 'epic' | 'unique' | 'legend';
/** 높은 등급부터 */
export const RARITIES: readonly Rarity[] = ['legend', 'unique', 'epic', 'rare', 'common'];

export const RATES_VERSION = '2026-10-24.1'; // 스킨 등급 개편: 전설·히든, 전사·마법사 스킨 새 목록, 게이지 두 개, 합성·분해 표(0054)
// 이전: 2026-10-23.2 챕터 3 병합(장비 뽑기 표는 1~40 단계만). 2026-10-23.1 도적 스킨 33종. 2026-10-14.3 컬렉션 세트 9종(0044)

/** 확률 단위: 십만분율(0.001% 단위) */
export const RATE_SCALE = 100_000;
export const RARITY_NAME: Record<Rarity, string> = { common: '일반', rare: '희귀', epic: '에픽', unique: '유니크', legend: '전설' };
export const HIDDEN_NAME = '히든';
/** 확률표 한 칸의 이름: "에픽", "에픽 히든" */
export const slotName = (rarity: Rarity, hidden: boolean): string => (hidden ? `${RARITY_NAME[rarity]} ${HIDDEN_NAME}` : RARITY_NAME[rarity]);

export type Banner = 'aura' | 'skin' | 'weapon' | 'armor' | 'accessory';
export type CosmeticBanner = 'aura' | 'skin';

/** 외형 뽑기의 한 칸(등급 + 히든 여부)과 그 확률(십만분율) */
export interface WeightRow {
  rarity: Rarity;
  hidden: boolean;
  weight: number;
}
/**
 * 직업당 스킨 풀 구성(보통·히든 수). 히든 1종 몫 = 같은 등급 보통 1종 몫의 1/5 를 이 개수로 계산한다.
 * 세 직업을 같은 개수로 맞춘다(사용자 확정 2026-10-24: 전설 2·유니크 5·에픽 8·희귀 20·일반 32 = 67종,
 * 그중 히든 전설 1·유니크 1·에픽 2·희귀 3·일반 5, 사용자가 고른 수가 기준).
 * 바꾸면 RATES_VERSION도 올린다
 */
export const SKIN_POOL_SHAPE: Record<Rarity, { normal: number; hidden: number }> = {
  legend: { normal: 1, hidden: 1 },
  unique: { normal: 4, hidden: 1 },
  epic: { normal: 6, hidden: 2 },
  rare: { normal: 17, hidden: 3 },
  common: { normal: 27, hidden: 5 },
};
/** 스킨 등급 확률(십만분율, 히든 포함). 희귀는 나머지. 전설만 보통·히든을 따로 정한다(0.020% + 0.006%) */
const SKIN_TIER = { common: 60_000, epic: 5_000, unique: 500, legendNormal: 20, legendHidden: 6 } as const;
/** 등급 합을 보통·히든 칸으로 나눈다: 히든 칸 = 합 x (히든 수/5) / (보통 수 + 히든 수/5), 반올림 */
function splitTier(rarity: Rarity, total: number): WeightRow[] {
  const { normal, hidden } = SKIN_POOL_SHAPE[rarity];
  const h = hidden > 0 ? Math.round((total * (hidden / 5)) / (normal + hidden / 5)) : 0;
  return [
    { rarity, hidden: true, weight: h },
    { rarity, hidden: false, weight: total - h },
  ];
}
const SKIN_RARE_TOTAL = RATE_SCALE - SKIN_TIER.common - SKIN_TIER.epic - SKIN_TIER.unique - SKIN_TIER.legendNormal - SKIN_TIER.legendHidden;
/**
 * 외형 뽑기 칸 확률(십만분율, 합 100000). 지금 구성의 스킨 값:
 *   전설 20 + 히든 6, 유니크 476 + 히든 24, 에픽 4,687 + 히든 313, 희귀 33,299 + 히든 1,175, 일반 57,857 + 히든 2,143
 * 직업 풀에 그 칸의 외형이 없으면(아직 히든·전설이 없는 직업) 히든 몫은 같은 등급 보통으로, 보통 몫은 한 등급 아래
 * 보통으로 간다(starshopService.slotTable, 공시도 같은 표)
 */
export const BANNER_ROWS: Record<CosmeticBanner, readonly WeightRow[]> = {
  // 오라: 에픽 오라 1%, 희귀 18%, 일반 81%(히든·전설 없음)
  aura: [
    { rarity: 'epic', hidden: false, weight: 1_000 },
    { rarity: 'rare', hidden: false, weight: 18_000 },
    { rarity: 'common', hidden: false, weight: 81_000 },
  ],
  skin: [
    { rarity: 'legend', hidden: true, weight: SKIN_TIER.legendHidden },
    { rarity: 'legend', hidden: false, weight: SKIN_TIER.legendNormal },
    ...splitTier('unique', SKIN_TIER.unique),
    ...splitTier('epic', SKIN_TIER.epic),
    ...splitTier('rare', SKIN_RARE_TOTAL),
    ...splitTier('common', SKIN_TIER.common),
  ],
};
/** 등급별 합(히든 포함, 십만분율) */
export const BANNER_WEIGHT: Record<CosmeticBanner, Record<Rarity, number>> = {
  aura: tierTotals(BANNER_ROWS.aura),
  skin: tierTotals(BANNER_ROWS.skin),
};
function tierTotals(rows: readonly WeightRow[]): Record<Rarity, number> {
  const out: Record<Rarity, number> = { legend: 0, unique: 0, epic: 0, rare: 0, common: 0 };
  for (const r of rows) out[r.rarity] += r.weight;
  return out;
}

/**
 * 천장(선택 게이지 칸 수). 바꿀 곳은 여기 하나다(사용자 확정 2026-10-24). 근거는 기획서 (d) 선택 게이지·천장.
 *   스킨 선택 게이지: 유니크 100(평균 210회의 약 0.48배), 전설 1,000(전설 보통 평균 5,000회의 0.2배)
 *   히든 게이지: 에픽 히든 350, 유니크 히든 2,000, 전설 히든 3,000. 일반·희귀 히든은 자연히 나와 천장이 없다
 */
export const CEILING = {
  skinUnique: 100,
  skinLegend: 1_000,
  hiddenEpic: 350,
  hiddenUnique: 2_000,
  hiddenLegend: 3_000,
} as const;

/** 선택 게이지: 오라(오라 뽑기), 스킨·히든(스킨 뽑기 1회에 둘 다 1칸) */
export type GaugeId = 'aura' | 'skin' | 'hidden';
export interface GaugeTarget {
  rarity: Rarity;
  hidden: boolean;
  /** 이 목표 하나를 고를 때 쓰는 칸(남은 칸은 이월) */
  cost: number;
}
export const GAUGES: Record<GaugeId, readonly GaugeTarget[]> = {
  aura: [{ rarity: 'epic', hidden: false, cost: 50 }],
  skin: [
    { rarity: 'unique', hidden: false, cost: CEILING.skinUnique },
    { rarity: 'legend', hidden: false, cost: CEILING.skinLegend },
  ],
  hidden: [
    { rarity: 'epic', hidden: true, cost: CEILING.hiddenEpic },
    { rarity: 'unique', hidden: true, cost: CEILING.hiddenUnique },
    { rarity: 'legend', hidden: true, cost: CEILING.hiddenLegend },
  ],
};
/** 그 게이지로 처음 고를 수 있는 칸 수(가장 싼 목표) */
export const gaugeMin = (g: GaugeId): number => Math.min(...GAUGES[g].map((t) => t.cost));
/** 그 게이지의 가장 비싼 목표(교환소 +10칸 품목이 넘지 못하는 선) */
export const gaugeCap = (g: GaugeId): number => Math.max(...GAUGES[g].map((t) => t.cost));
export const AURA_GAUGE_MAX = gaugeMin('aura');
export const SKIN_GAUGE_MAX = gaugeMin('skin');
export const HIDDEN_GAUGE_MAX = gaugeMin('hidden');
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

/** 착용한 외형의 공격력 보너스(%): 오라 일반 1·희귀 2·에픽 3, 스킨 보통 에픽 4·유니크 5·전설 6, 히든은 한 단계 위 값 */
export const AURA_DAMAGE: Record<Rarity, number> = { common: 1, rare: 2, epic: 3, unique: 3, legend: 3 };
export const SKIN_DAMAGE: Record<Rarity, number> = { common: 0, rare: 0, epic: 4, unique: 5, legend: 6 };
export const SKIN_HIDDEN_DAMAGE: Record<Rarity, number> = { common: 1, rare: 1, epic: 5, unique: 6, legend: 7 };
/** 서버 화력 상한은 착용 외형 최대치만큼 넓힌다(에픽 오라 3 + 전설 히든 스킨 7) */
export const COSMETIC_DAMAGE_MAX = AURA_DAMAGE.epic + SKIN_HIDDEN_DAMAGE.legend;

export const PULL_PRICE = 100;
/** 10회 묶음: 1,000개에 11회 */
export const TEN_PRICE = 1000;
export const TEN_COUNT = 11;
/**
 * 이미 가진 외형이 또 나오면 여분으로 쌓인다(합성 재료). 여분은 언제든 분해해 별조각·별의 결정으로 바꿀 수 있다.
 * 이 표는 오라 값이다(오라에는 유니크·전설이 없다). 스킨은 SKIN_DISMANTLE
 */
export const DUPLICATE_REFUND: Record<Rarity, number> = { common: 20, rare: 50, epic: 300, unique: 1500, legend: 1500 };
export const DISMANTLE_STARS = DUPLICATE_REFUND;
/**
 * 스킨 여분 분해(기획서 (d) 분해 표): 뽑기 1회를 모두 분해하면 별조각 기준 약 45%(결정 1 = 별조각 2).
 * 결정은 별조각의 1/5, 히든은 같은 등급 보통의 3배
 */
export const SKIN_DISMANTLE: { stars: Record<Rarity, number>; crystals: Record<Rarity, number> } = {
  stars: { common: 15, rare: 25, epic: 120, unique: 1000, legend: 4500 },
  crystals: { common: 3, rare: 5, epic: 24, unique: 200, legend: 900 },
};
export const HIDDEN_DISMANTLE_MULT = 3;

/**
 * 합성: 같은 등급 여분 4개 -> 일정 확률로 한 등급 위 외형 하나. 실패하면 넣은 것 중 1개를 돌려준다.
 * 연속 실패가 pity번 쌓이면 다음 합성은 성공한다(0 = 천장 없음). 확률은 천분율. 히든 여분은 재료가 아니고 결과에도 없다.
 *   일반 -> 희귀(오라 또는 내 직업 희귀 스킨) 40%, 희귀 -> 에픽(오라 또는 내 직업 에픽 스킨) 30%(5회 실패 뒤 확정),
 *   에픽 -> 내 직업 유니크 스킨 15%(5회 실패 뒤 확정), 유니크 스킨 -> 내 직업 전설 스킨 10%(5회 실패 뒤 확정)
 */
export const SYNTH_COUNT = 4;
export const SYNTH: Record<'common' | 'rare' | 'epic' | 'unique', { to: Rarity; permille: number; pity: number }> = {
  common: { to: 'rare', permille: 400, pity: 0 },
  rare: { to: 'epic', permille: 300, pity: 5 },
  epic: { to: 'unique', permille: 150, pity: 5 },
  unique: { to: 'legend', permille: 100, pity: 5 },
};
export type SynthFrom = keyof typeof SYNTH;
/** 연속 실패를 세는 칸(결과 등급) */
export type SynthTier = 'rare' | 'epic' | 'unique' | 'legend';

// ---------------- 0044 캐시 아이템 가치(별의 결정, 별빛 교환소, 장비 도감) ----------------

/** 외형 여분 분해 시 별조각(DISMANTLE_STARS)에 더해 주는 별의 결정 */
export const DISMANTLE_CRYSTALS: Record<Rarity, number> = { common: 4, rare: 12, epic: 60, unique: 300, legend: 300 };
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
  kind: 'cosmetic' | 'skin_gauge' | 'hidden_gauge' | 'gear_gauge' | 'item';
  pick?: { rarity: Rarity; skin: boolean | null };
  itemId?: string;
  count?: number;
}
export const EXCHANGE_GOODS: readonly ExchangeGood[] = [
  { id: 'pick_rare', name: '원하는 희귀 오라·스킨', price: 150, weekly: 0, kind: 'cosmetic', pick: { rarity: 'rare', skin: null } },
  { id: 'pick_epic_aura', name: '원하는 에픽 오라', price: 500, weekly: 0, kind: 'cosmetic', pick: { rarity: 'epic', skin: false } },
  { id: 'pick_epic_skin', name: '원하는 에픽 스킨', price: 700, weekly: 0, kind: 'cosmetic', pick: { rarity: 'epic', skin: true } },
  { id: 'skin_gauge10', name: '스킨 선택 게이지 +10칸', price: 120, weekly: 1, kind: 'skin_gauge', count: 10 },
  { id: 'hidden_gauge10', name: '히든 게이지 +10칸', price: 300, weekly: 1, kind: 'hidden_gauge', count: 10 },
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
/** 원하는 외형을 바로 얻는 확정 교환 가격(오라). 0 = 교환 불가 */
export const EXCHANGE_PRICE: Record<Rarity, number> = { common: 300, rare: 1000, epic: 3000, unique: 0, legend: 0 };
export const EPIC_SKIN_PRICE = 4000;
/** 일반·희귀 스킨 확정 교환 가격(뽑기 1개당 평균 별조각의 약 2배). 유니크·전설·히든은 게이지·합성으로만 */
export const LOW_SKIN_PRICE: Record<'common' | 'rare', number> = { common: 300, rare: 600 };

export interface CosmeticDef {
  id: string;
  name: string;
  rarity: Rarity;
  /** 코스튬 스킨(직업별 전신 외형) */
  skin?: CharacterClassId;
  /** 별빛 교환소에서만 얻는 외형(뽑기·합성·확정 교환에 나오지 않는다, 0044) */
  exchangeOnly?: boolean;
  /** 히든: 같은 등급 안의 특별 디자인(확률 1/5, 능력치 한 단계 위, 합성 재료·결과 아님, 히든 게이지로 고른다) */
  hidden?: boolean;
  /** 풀에서 뺀 외형(그림 보관). 보유자는 계속 입고, 여분은 분해할 수 있다 */
  retired?: boolean;
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
  // 예전 전사 스킨 중 새 목록에 없는 것(retired: 보유자만 유지, 기획서 (g) 3)
  { id: 'skin_w_sand', name: '모래빛 여행복', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_rust', name: '적갈 견습복', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_amethyst', name: '자수정 기사복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_brass', name: '황동 경비복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_maple', name: '단풍 무사', rarity: 'epic', skin: 'warrior', retired: true },
  { id: 'skin_noir', name: '칠흑의 검희', rarity: 'epic', skin: 'warrior', retired: true },
  { id: 'skin_moon', name: '월광 검귀', rarity: 'unique', skin: 'warrior', retired: true },
  { id: 'skin_w_ranger', name: '숲 순찰대복', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_sailor', name: '견습 선원복', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_farmer', name: '들녘 일꾼복', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_scout', name: '정찰병 가죽옷', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_miner', name: '광산 작업복', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_smith', name: '대장간 앞치마', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_wanderer', name: '방랑 검객 망토', rarity: 'common', skin: 'warrior', retired: true },
  { id: 'skin_w_bluecoat', name: '청기사 견습복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_violet', name: '보랏빛 결투복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_jade', name: '비취 근위복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_scarlet', name: '진홍 검무복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_frost', name: '설원 순찰복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_sunguard', name: '태양 기사단복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_shade', name: '그림자 자객복', rarity: 'rare', skin: 'warrior', retired: true },
  { id: 'skin_w_valkyrie', name: '발키리 갑주', rarity: 'epic', skin: 'warrior', retired: true },
  { id: 'skin_w_dragonscale', name: '용비늘 갑주', rarity: 'epic', skin: 'warrior', retired: true },
  { id: 'skin_w_roseknight', name: '장미 기사', rarity: 'epic', skin: 'warrior', retired: true },
  { id: 'skin_w_aurora', name: '오로라 여제', rarity: 'unique', skin: 'warrior', retired: true },
  // 2026-10-24 전사 스킨 2차 확정(Docs/skins/SELECTED_WARRIOR_2026-10-24.txt, 67종 = 보통 55 + 히든 12).
  // 같은 콘셉트의 예전 id(skin_w_moss·skin_w_crimson·skin_obsidian·skin_lion·skin_w_snowblade·skin_w_phoenix·skin_w_voidblade)는 id를 그대로 쓰고 등급·그림만 바뀐다(기획서 (g) 1·2)
  { id: 'skin_w_skyqueen', name: '천공의 발키리 여왕', rarity: 'legend', skin: 'warrior', hidden: true },
  { id: 'skin_w_solar', name: '태양의 검황', rarity: 'legend', skin: 'warrior' },
  { id: 'skin_w_moonprincess', name: '달의 기사공주', rarity: 'unique', skin: 'warrior', hidden: true },
  { id: 'skin_lion', name: '황금 사자 기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_w_phoenix', name: '불사조 성기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_w_frostqueen', name: '서리 여기사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_w_sakurablade', name: '벚꽃 검무사', rarity: 'unique', skin: 'warrior' },
  { id: 'skin_w_azuredancer', name: '청룡 무희 검객', rarity: 'epic', skin: 'warrior', hidden: true },
  { id: 'skin_w_moonlit', name: '월하 검희', rarity: 'epic', skin: 'warrior', hidden: true },
  { id: 'skin_w_voidblade', name: '심연의 검성', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_thunderdragon', name: '천둥 용기사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_snowblade', name: '설화 검객', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_obsidian', name: '흑요 기사', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_desert', name: '사막 검무희', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_sunset', name: '노을빛 그라데이션', rarity: 'epic', skin: 'warrior' },
  { id: 'skin_w_whitetwin', name: '백발 낮은 트윈', rarity: 'rare', skin: 'warrior', hidden: true },
  { id: 'skin_w_starlight', name: '별빛 은발', rarity: 'rare', skin: 'warrior', hidden: true },
  { id: 'skin_w_aurorahair', name: '오로라 머리', rarity: 'rare', skin: 'warrior', hidden: true },
  { id: 'skin_w_blackstraight', name: '흑단 생머리', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_silverbob', name: '은발 단발', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_auburnlow', name: '적갈 낮은 묶음', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_chestnutwave', name: '밤색 웨이브', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_navybun', name: '남색 양갈래 똥머리', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_redside', name: '진홍 옆묶음', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_lilachalf', name: '라일락 반묶음', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_copperpony', name: '구리빛 포니테일', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_bluebraids', name: '청발 양갈래 땋기', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_blackbob', name: '흑발 단발', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_greybun', name: '잿빛 올림머리', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_pinklong', name: '장밋빛 긴머리', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_purpleside', name: '보랏빛 옆땋기', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_brownshort', name: '갈색 단발 땋기', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_tealpony', name: '청록 하이포니', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_strawberry', name: '딸기 금발 양묶음', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_ravenlong', name: '칠흑 긴 포니', rarity: 'rare', skin: 'warrior' },
  { id: 'skin_w_skyblue', name: '하늘 전투복', rarity: 'common', skin: 'warrior', hidden: true },
  { id: 'skin_w_scarletblack', name: '흑적 전투복', rarity: 'common', skin: 'warrior', hidden: true },
  { id: 'skin_w_turquoise', name: '옥빛 전투복', rarity: 'common', skin: 'warrior', hidden: true },
  { id: 'skin_w_navyred', name: '남홍 전투복', rarity: 'common', skin: 'warrior', hidden: true },
  { id: 'skin_w_blossom', name: '벚꽃 자수 전투복', rarity: 'common', skin: 'warrior', hidden: true },
  { id: 'skin_w_crimson', name: '진홍 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_lavender', name: '라벤더 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_mustard', name: '겨자 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_teal', name: '청록 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_charcoal', name: '먹빛 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_mint', name: '민트 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_pumpkin', name: '호박 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_ivory', name: '상아 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_coral', name: '산호 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_plum', name: '자두 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_royal', name: '왕청 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_emerald', name: '에메랄드 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_slate', name: '잿빛 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_lemon', name: '레몬 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_sakura', name: '벚꽃 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_indigo', name: '쪽빛 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_moss', name: '이끼 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_steel', name: '강철 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_cherry', name: '체리 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_aqua', name: '물빛 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_chocomint', name: '초코민트 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_lilacwhite', name: '라일락 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_grape', name: '포도 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_stripe', name: '줄무늬 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_pajama', name: '별무늬 잠옷', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_starnight', name: '별밤 자수 전투복', rarity: 'common', skin: 'warrior' },
  { id: 'skin_w_festival', name: '축제 예복 전투복', rarity: 'common', skin: 'warrior' },
  // 예전 마법사 스킨 중 새 목록에 없는 것(retired: 보유자만 유지)
  { id: 'skin_m_ash', name: '잿빛 견습 로브', rarity: 'common', skin: 'mage', retired: true },
  { id: 'skin_m_pilgrim', name: '순례자의 로브', rarity: 'common', skin: 'mage', retired: true },
  { id: 'skin_m_sprout', name: '새싹 로브', rarity: 'common', skin: 'mage', retired: true },
  { id: 'skin_m_rose', name: '장밋빛 로브', rarity: 'rare', skin: 'mage', retired: true },
  { id: 'skin_m_emerald', name: '에메랄드 로브', rarity: 'rare', skin: 'mage', retired: true },
  { id: 'skin_m_sunset', name: '노을 로브', rarity: 'rare', skin: 'mage', retired: true },
  { id: 'skin_forest', name: '숲의 정령', rarity: 'epic', skin: 'mage', retired: true },
  { id: 'skin_ice', name: '얼음 여왕', rarity: 'epic', skin: 'mage', retired: true },
  { id: 'skin_crimson', name: '홍염의 마녀', rarity: 'unique', skin: 'mage', retired: true },
  { id: 'skin_m_herbalist', name: '약초꾼 로브', rarity: 'common', skin: 'mage', retired: true },
  { id: 'skin_m_tea', name: '찻잎 견습 로브', rarity: 'common', skin: 'mage', retired: true },
  { id: 'skin_m_dusk', name: '밤하늘 견습 로브', rarity: 'common', skin: 'mage', retired: true },
  { id: 'skin_m_moonshrine', name: '달빛 무녀', rarity: 'epic', skin: 'mage', retired: true },
  { id: 'skin_m_storm', name: '뇌운의 마녀', rarity: 'epic', skin: 'mage', retired: true },
  { id: 'skin_m_sakura', name: '벚꽃 무녀', rarity: 'epic', skin: 'mage', retired: true },
  { id: 'skin_m_frostprincess', name: '서리 공주', rarity: 'epic', skin: 'mage', retired: true },
  { id: 'skin_m_celestial', name: '천공의 대마녀', rarity: 'unique', skin: 'mage', retired: true },
  { id: 'skin_m_flameempress', name: '화염 여제', rarity: 'unique', skin: 'mage', retired: true },
  { id: 'skin_m_dream', name: '몽환의 마녀', rarity: 'unique', skin: 'mage', retired: true },
  // 2026-10-24 마법사 스킨 최종 확정(Docs/skins/SELECTED_MAGE_2026-10-24.txt, 67종 = 보통 55 + 히든 12). 프레임 그림은 다음 단계(없으면 기본 몸).
  // 예전 id(skin_starnight·skin_m_lavender·skin_m_student·skin_m_snowcloak·skin_m_autumn·skin_m_sky·skin_m_cherry·skin_m_mint·skin_m_amber·skin_m_ocean·skin_m_lilac·skin_m_candy)는 id를 그대로 쓰고 등급만 바뀐다. 예전 id와 이름이 겹치는 새 그림은 rosebun·mintbraid·duskwave·tealtwin·lilacrobe로 따로 둔다
  { id: 'skin_m_gumiho', name: '구미호 여우 마녀', rarity: 'legend', skin: 'mage', hidden: true },
  { id: 'skin_m_pharaoh', name: '태양의 파라오 여왕', rarity: 'legend', skin: 'mage' },
  { id: 'skin_m_rainbowcloud', name: '무지개 구름 마녀', rarity: 'unique', skin: 'mage', hidden: true },
  { id: 'skin_m_vampire', name: '뱀파이어 장미 백작', rarity: 'unique', skin: 'mage' },
  { id: 'skin_starnight', name: '성야의 마녀', rarity: 'unique', skin: 'mage' },
  { id: 'skin_m_bee', name: '꿀벌 드레스', rarity: 'unique', skin: 'mage' },
  { id: 'skin_m_healer', name: '치유사 가운', rarity: 'unique', skin: 'mage' },
  { id: 'skin_m_pirate', name: '해적 마녀복', rarity: 'epic', skin: 'mage', hidden: true },
  { id: 'skin_m_maid', name: '홍차 메이드 마녀', rarity: 'epic', skin: 'mage', hidden: true },
  { id: 'skin_m_snowman', name: '눈사람 코트', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_alchemist', name: '연금술사 가운', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_hanbok', name: '마녀 한복', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_painter', name: '화가 마녀복', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_sport', name: '운동 마녀복', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_lavender', name: '라벤더 후드 로브', rarity: 'epic', skin: 'mage' },
  { id: 'skin_m_goldtwin', name: '금발 양갈래 마녀', rarity: 'rare', skin: 'mage', hidden: true },
  { id: 'skin_m_tealtwin', name: '청록 낮은 양갈래', rarity: 'rare', skin: 'mage', hidden: true },
  { id: 'skin_m_aurorahair', name: '오로라 그라데이션 마녀', rarity: 'rare', skin: 'mage', hidden: true },
  { id: 'skin_m_sky', name: '하늘빛 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_cherry', name: '벚꽃 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_mint', name: '민트 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_amber', name: '호박빛 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_ocean', name: '바다 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_lilac', name: '라일락 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_candy', name: '사탕 마녀', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_student', name: '마법학교 교복', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_autumn', name: '가을 낙엽 로브', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_rosebun', name: '장미 쌍올림', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_mintbraid', name: '민트 옆땋음', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_copper', name: '구리빛 픽시컷', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_duskwave', name: '황혼 긴 웨이브', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_ink', name: '먹빛 낮은 포니', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_pearl', name: '진주 곱슬 단발', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_chestnut', name: '밤빛 땋은 양갈래', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_cobalt', name: '코발트 높은 포니', rarity: 'rare', skin: 'mage' },
  { id: 'skin_m_snowcloak', name: '눈송이 망토', rarity: 'common', skin: 'mage', hidden: true },
  { id: 'skin_m_starcandy', name: '별사탕 축제 드레스', rarity: 'common', skin: 'mage', hidden: true },
  { id: 'skin_m_snowflake', name: '눈꽃무늬 로브', rarity: 'common', skin: 'mage', hidden: true },
  { id: 'skin_m_pajama', name: '구름 별 잠옷 로브', rarity: 'common', skin: 'mage', hidden: true },
  { id: 'skin_m_xmastree', name: '크리스마스 트리 로브', rarity: 'common', skin: 'mage', hidden: true },
  { id: 'skin_m_crimson', name: '진홍 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_forest', name: '숲빛 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_skyblue', name: '하늘 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_lilacrobe', name: '연보라 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_mustard', name: '겨자 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_teal', name: '청록 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_rosepink', name: '장미 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_charcoal', name: '잿빛 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_cocoa', name: '코코아 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_wine', name: '와인 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_mintgreen', name: '민트 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_pumpkin', name: '단호박 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_ivory', name: '상아 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_coral', name: '산호 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_plum', name: '자두 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_olive', name: '올리브 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_seastripe', name: '바다 줄무늬 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_polka', name: '물방울 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_starnavy', name: '별무늬 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_moonpurple', name: '달무늬 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_checker', name: '체크 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_leafpattern', name: '잎무늬 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_flowerpattern', name: '꽃무늬 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_greystripe', name: '회청 줄무늬 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_twotone', name: '투톤 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_cherryhat', name: '체리 로브', rarity: 'common', skin: 'mage' },
  { id: 'skin_m_umber', name: '황토 로브', rarity: 'common', skin: 'mage' },
  // 예전 도적 스킨 중 새 목록에 없는 것(retired: 보유자만 유지)
  { id: 'skin_r_wine', name: '와인빛 밀정복', rarity: 'common', skin: 'rogue', retired: true },
  { id: 'skin_r_moss', name: '숲길 정찰복', rarity: 'common', skin: 'rogue', retired: true },
  { id: 'skin_r_rust', name: '적갈 사냥꾼복', rarity: 'common', skin: 'rogue', retired: true },
  { id: 'skin_r_coal', name: '숯빛 야행복', rarity: 'common', skin: 'rogue', retired: true },
  { id: 'skin_r_crimson', name: '진홍 그림자복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_violet', name: '보랏빛 야행복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_gold', name: '황금 도둑복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_frost', name: '서리 밀정복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_rose', name: '장미 가면복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_ember', name: '불씨 두건복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_azure', name: '하늘빛 바람복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_onyx', name: '흑요 암살복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_lilac', name: '라일락 무희복', rarity: 'rare', skin: 'rogue', retired: true },
  { id: 'skin_r_moonshade', name: '달그림자 밀정', rarity: 'epic', skin: 'rogue', retired: true },
  { id: 'skin_r_corsair', name: '바다의 칼잡이', rarity: 'epic', skin: 'rogue', retired: true },
  { id: 'skin_r_raven', name: '까마귀 깃 암살자', rarity: 'epic', skin: 'rogue', retired: true },
  { id: 'skin_r_scorpion', name: '황금 전갈', rarity: 'unique', skin: 'rogue', retired: true },
  // 2026-10-24 도적 스킨 최종 확정(Docs/skins/SELECTED_ROGUE_2026-10-24.txt, 67종 = 보통 55 + 히든 12). 시트는 다음 단계(없으면 기본 몸).
  // 콘셉트가 같은 예전 id는 그대로 쓴다(고딕 트윈테일=nightqueen, 쿠노이치=crimsonmoon, 모래바람 칼춤꾼=dunedancer, 벚꽃 쿠노이치=sakura, 비취 독사=jade 등)
  { id: 'skin_r_aurora', name: '오로라 괴도', rarity: 'legend', skin: 'rogue', hidden: true },
  { id: 'skin_r_butterfly', name: '비단 나비', rarity: 'legend', skin: 'rogue' },
  { id: 'skin_r_nightqueen', name: '고딕 트윈테일', rarity: 'unique', skin: 'rogue', hidden: true },
  { id: 'skin_r_frosthunter', name: '서리 사냥꾼', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_lotus', name: '그림자 연꽃 자객', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_jewel', name: '보석 괴도', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_bloodmoon', name: '혈월 자객', rarity: 'unique', skin: 'rogue' },
  { id: 'skin_r_dunedancer', name: '모래바람 칼춤꾼', rarity: 'epic', skin: 'rogue', hidden: true },
  { id: 'skin_r_lantern', name: '등불 그림자 무희', rarity: 'epic', skin: 'rogue', hidden: true },
  { id: 'skin_r_crimsonmoon', name: '쿠노이치', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_redhood', name: '붉은 후드', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_cardthief', name: '트럼프 마술 괴도', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_thunder', name: '뇌신 닌자', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_foxmask', name: '여우 가면', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_jade', name: '비취 독사', rarity: 'epic', skin: 'rogue' },
  { id: 'skin_r_sakura', name: '벚꽃 쿠노이치', rarity: 'rare', skin: 'rogue', hidden: true },
  { id: 'skin_r_starlight', name: '별빛 은발', rarity: 'rare', skin: 'rogue', hidden: true },
  { id: 'skin_r_aurorahair', name: '오로라 머리', rarity: 'rare', skin: 'rogue', hidden: true },
  { id: 'skin_r_pinktwin', name: '분홍 트윈테일', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_auburnlow', name: '적갈 낮은 묶음', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_chestnutwave', name: '밤색 웨이브', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_navybun', name: '남색 양갈래 똥머리', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_redside', name: '진홍 옆묶음', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_mintlong', name: '민트 긴머리', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_lilachalf', name: '라일락 반묶음', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_whitetwin', name: '백발 낮은 트윈', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_bluebraids', name: '청발 양갈래 땋기', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_blackbob', name: '흑발 단발', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_goldwave', name: '금발 긴 웨이브', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_pinklong', name: '장밋빛 긴머리', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_purpleside', name: '보랏빛 옆땋기', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_tealpony', name: '청록 하이포니', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_strawberry', name: '딸기 금발 양묶음', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_wavebob', name: '청보라 곱슬 단발', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_sunset', name: '노을빛 그라데이션', rarity: 'rare', skin: 'rogue' },
  { id: 'skin_r_pajama', name: '별무늬 잠옷', rarity: 'common', skin: 'rogue', hidden: true },
  { id: 'skin_r_moonlit', name: '달빛 자수 정찰복', rarity: 'common', skin: 'rogue', hidden: true },
  { id: 'skin_r_maple', name: '단풍 자수 정찰복', rarity: 'common', skin: 'rogue', hidden: true },
  { id: 'skin_r_festival', name: '축제 정찰복', rarity: 'common', skin: 'rogue', hidden: true },
  { id: 'skin_r_snowfest', name: '눈꽃 정찰복', rarity: 'common', skin: 'rogue', hidden: true },
  { id: 'skin_r_navy', name: '남색 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_forest', name: '숲빛 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_sky', name: '하늘 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_lavender', name: '라벤더 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_mustard', name: '겨자 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_teal', name: '청록 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_charcoal', name: '먹빛 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_cocoa', name: '코코아 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_mint', name: '민트 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_pumpkin', name: '호박 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_ivory', name: '상아 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_coral', name: '산호 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_plum', name: '자두 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_royal', name: '왕청 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_blackred', name: '흑적 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_emerald', name: '에메랄드 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_slate', name: '잿빛 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_sand', name: '모래 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_turquoise', name: '옥빛 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_lemon', name: '레몬 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_sakurascout', name: '벚꽃 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_peach', name: '복숭아 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_steel', name: '강철 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_cherry', name: '체리 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_chocomint', name: '초코민트 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_lilacwhite', name: '라일락 정찰복', rarity: 'common', skin: 'rogue' },
  { id: 'skin_r_grape', name: '포도 정찰복', rarity: 'common', skin: 'rogue' },
  // 0044 별빛 교환소 전용(시즌 1): 별의 결정으로만 얻는다
  { id: 'aura_crystal', name: '결정빛 오라', rarity: 'epic', exchangeOnly: true },
  { id: 'aura_nova', name: '신성 폭발 오라', rarity: 'epic', exchangeOnly: true },
  { id: 'skin_w_starwarden', name: '별빛 수호 기사', rarity: 'epic', skin: 'warrior', exchangeOnly: true },
  { id: 'skin_m_starsage', name: '별의 현자', rarity: 'epic', skin: 'mage', exchangeOnly: true },
  { id: 'skin_r_starshade', name: '별빛 그림자', rarity: 'epic', skin: 'rogue', exchangeOnly: true },
];

export const STAR_COSMETIC_BY_ID = new Map(STAR_COSMETICS.map((c) => [c.id, c] as const));

/** 오라(스킨 제외) 중 한 등급(교환소 전용·retired 제외) */
export function cosmeticsOf(rarity: Rarity): CosmeticDef[] {
  return STAR_COSMETICS.filter((c) => c.rarity === rarity && !c.skin && !c.exchangeOnly && !c.retired);
}

/** 그 직업 스킨 풀의 한 칸(뽑기·합성·게이지가 쓰는 것: 교환소 전용·retired 제외) */
export function skinsOf(cls: string, rarity: Rarity, hidden: boolean): CosmeticDef[] {
  return STAR_COSMETICS.filter((c) => c.skin === cls && c.rarity === rarity && !!c.hidden === hidden && !c.exchangeOnly && !c.retired);
}

/** 뽑기·합성·확정 교환에 나오는 외형(교환소 전용·retired 제외) */
export const GACHA_COSMETICS: readonly CosmeticDef[] = STAR_COSMETICS.filter((c) => !c.exchangeOnly && !c.retired);

/** 착용 공격력(%) */
export function damageOf(c: CosmeticDef): number {
  if (!c.skin) return AURA_DAMAGE[c.rarity];
  return (c.hidden ? SKIN_HIDDEN_DAMAGE : SKIN_DAMAGE)[c.rarity];
}

/** 여분 1개 분해 값: 오라는 예전 표, 스킨은 SKIN_DISMANTLE(히든 3배) */
export function dismantleStarsOf(c: CosmeticDef): number {
  if (!c.skin) return DISMANTLE_STARS[c.rarity];
  return SKIN_DISMANTLE.stars[c.rarity] * (c.hidden ? HIDDEN_DISMANTLE_MULT : 1);
}
export function dismantleCrystalsOf(c: CosmeticDef): number {
  if (!c.skin) return DISMANTLE_CRYSTALS[c.rarity];
  return SKIN_DISMANTLE.crystals[c.rarity] * (c.hidden ? HIDDEN_DISMANTLE_MULT : 1);
}

/** 확정 교환 가격(0 = 교환 불가: 교환소 전용, retired, 히든, 유니크·전설 스킨) */
export function exchangePriceOf(c: CosmeticDef): number {
  if (c.exchangeOnly || c.retired || c.hidden) return 0;
  if (c.skin) return c.rarity === 'epic' ? EPIC_SKIN_PRICE : c.rarity === 'common' || c.rarity === 'rare' ? LOW_SKIN_PRICE[c.rarity] : 0;
  return EXCHANGE_PRICE[c.rarity];
}

// ---------------- 컬렉션 ----------------

/**
 * 컬렉션: 세트의 외형을 모두 가지면 등록할 수 있고, 등록하면 계정 전체에 능력치가 붙는다(외형은 소모하지 않는다).
 * attack/health는 %. 스킨 세트는 직업 풀에서 만든다(기획서 (f)): 일반·희귀·에픽 의상실(보통만), 유니크·전설 의상실,
 * 히든 컬렉션. 히든 수집가는 세 직업 히든 컬렉션을 모두 등록해야 열린다. 풀이 비어 있는 세트는 만들지 않는다
 * (마법사·도적은 아직 전설·히든이 없다. 목록이 들어오면 자동으로 생긴다)
 */
export interface CollectionDef {
  id: string;
  name: string;
  members: string[];
  requires?: string[];
  attack: number;
  health: number;
  /** 히든 묶음(화면에서 따로 보여 준다) */
  hidden?: boolean;
}
const AURA_COLLECTIONS: CollectionDef[] = [
  { id: 'col_dawn', name: '새벽빛 오라', members: ['aura_dew', 'aura_maple', 'aura_blossom', 'aura_forest', 'aura_ash'], attack: 0, health: 2 },
  { id: 'col_starlight', name: '별빛 오라', members: ['aura_sunset', 'aura_violet', 'aura_frost', 'aura_rose', 'aura_jade'], attack: 1, health: 0 },
  { id: 'col_legend', name: '찬란한 오라', members: ['aura_rainbow', 'aura_gold', 'aura_abyss'], attack: 1, health: 2 },
  { id: 'col_mist', name: '물안개 오라', members: ['aura_mint', 'aura_peach', 'aura_lemon', 'aura_lilac', 'aura_sea'], attack: 0, health: 2 },
  { id: 'col_meadow', name: '들판 오라', members: ['aura_sand', 'aura_berry', 'aura_moss', 'aura_cloud', 'aura_coral'], attack: 0, health: 1 },
  { id: 'col_twilight', name: '별자리 오라', members: ['aura_comet', 'aura_aurora', 'aura_amber', 'aura_sapphire', 'aura_ruby'], attack: 1, health: 0 },
  { id: 'col_jewel', name: '보석빛 오라', members: ['aura_pearl', 'aura_lavender', 'aura_ember', 'aura_lagoon', 'aura_dusk'], attack: 0, health: 2 },
  { id: 'col_celestial', name: '천상의 오라', members: ['aura_prism', 'aura_starfall', 'aura_sakura', 'aura_thunder', 'aura_deepsea', 'aura_spectrum'], attack: 1, health: 0 },
];
/** 0054에서 지운 예전 스킨 세트(등록 기록 삭제) */
export const RETIRED_COLLECTIONS = ['col_sword', 'col_magic', 'col_knight', 'col_witch', 'col_valor', 'col_arcana', 'col_shade', 'col_blades'] as const;

/** 직업별 세트 이름(번역 표 키라 문장 그대로 둔다): 일반·희귀·에픽·유니크와 전설·히든 */
const CLASS_SETS: { cls: CharacterClassId; key: string; names: [string, string, string, string, string] }[] = [
  { cls: 'warrior', key: 'w', names: ['전사 일반 의상실', '전사 희귀 의상실', '전사 에픽 의상실', '전사 유니크·전설 의상실', '전사 히든 컬렉션'] },
  { cls: 'mage', key: 'm', names: ['마법사 일반 의상실', '마법사 희귀 의상실', '마법사 에픽 의상실', '마법사 유니크·전설 의상실', '마법사 히든 컬렉션'] },
  { cls: 'rogue', key: 'r', names: ['도적 일반 의상실', '도적 희귀 의상실', '도적 에픽 의상실', '도적 유니크·전설 의상실', '도적 히든 컬렉션'] },
];
function classCollections(): CollectionDef[] {
  const out: CollectionDef[] = [];
  const ids = (list: CosmeticDef[]) => list.map((c) => c.id);
  for (const { cls, key, names } of CLASS_SETS) {
    const sets: CollectionDef[] = [
      { id: `col_${key}_common`, name: names[0], members: ids(skinsOf(cls, 'common', false)), attack: 0, health: 1 },
      { id: `col_${key}_rare`, name: names[1], members: ids(skinsOf(cls, 'rare', false)), attack: 0, health: 2 },
      { id: `col_${key}_epic`, name: names[2], members: ids(skinsOf(cls, 'epic', false)), attack: 1, health: 0 },
      { id: `col_${key}_top`, name: names[3], members: ids([...skinsOf(cls, 'unique', false), ...skinsOf(cls, 'legend', false)]), attack: 1, health: 1 },
      {
        id: `col_${key}_hidden`,
        name: names[4],
        members: ids(RARITIES.flatMap((r) => skinsOf(cls, r, true))),
        attack: 1,
        health: 1,
        hidden: true,
      },
    ];
    out.push(...sets.filter((c) => c.members.length > 0));
  }
  const hiddenSets = CLASS_SETS.map((c) => `col_${c.key}_hidden`);
  if (hiddenSets.every((id) => out.some((c) => c.id === id))) {
    out.push({ id: 'col_hidden_all', name: '히든 수집가', members: [], requires: hiddenSets, attack: 1, health: 2, hidden: true });
  }
  return out;
}
const SKIN_COLLECTIONS = classCollections();
const MASTER: CollectionDef = {
  id: 'col_master',
  name: '옷장의 주인',
  members: [],
  requires: ['col_dawn', 'col_starlight', 'col_legend', 'col_w_epic', 'col_m_epic'],
  attack: 0,
  health: 3,
};
export const COLLECTIONS: readonly CollectionDef[] = [...AURA_COLLECTIONS.slice(0, 3), ...SKIN_COLLECTIONS, MASTER, ...AURA_COLLECTIONS.slice(3)];
export const COLLECTION_BY_ID = new Map(COLLECTIONS.map((c) => [c.id, c] as const));
export const COLLECTION_ATTACK_MAX = COLLECTIONS.reduce((a, c) => a + c.attack, 0);
export const COLLECTION_HEALTH_MAX = COLLECTIONS.reduce((a, c) => a + c.health, 0);
