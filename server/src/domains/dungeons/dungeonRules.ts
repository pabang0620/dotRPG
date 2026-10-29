// 솔로 던전의 순수 규칙. C# 원본: DungeonRanking, DungeonRewards, ResetClock.IsOpen
import { tierOfLevel } from '../../utils/gearTier';
import type { DifficultyDef, DungeonDef, EconomyData } from '../../gamedata/economyData';
import { RARITY_ORDER } from '../../gamedata/economyData';
import { f32, keyAt, roundHalfEven } from '../../utils/itemKey';
import { gameWeekday } from '../../utils/resetBoundaries';
import type { Rng } from '../../utils/rng';
import { usableBy } from '../../utils/characterClass';

export interface RankScore {
  time: number;
  hits: number;
  kills: number;
  combo: number;
  revive_penalty: number;
  total: number;
}

export const RANK_NAMES = ['SSS', 'SS', 'S', 'A', 'B', 'C', 'D', 'E', 'F'] as const;

const clamp01 = (x: number): number => Math.min(1, Math.max(0, x));

/** DungeonRanking.RankOf: 0 SSS ... 8 F */
export function rankOf(eco: EconomyData, total: number): number {
  const t = eco.dungeons.ranking.thresholds;
  for (let i = 0; i < t.length; i++) if (total >= (t[i] as number)) return i;
  return t.length;
}

export function xpBonusPercent(eco: EconomyData, rank: number): number {
  return eco.dungeons.ranking.xpBonus[rank] ?? 0;
}

/** DungeonRanking.Score (float32 연산을 흉내낸다) */
export function scoreRun(
  eco: EconomyData,
  seconds: number,
  referenceSeconds: number,
  hitsTaken: number,
  kills: number,
  monsters: number,
  maxCombo: number,
  revives: number,
): RankScore {
  const r = eco.dungeons.ranking;
  const over = referenceSeconds > 0 ? f32(seconds / referenceSeconds) : 1;
  const timeFactor = over <= 1 ? 1 : clamp01(f32(f32(r.timeZeroAt - over) / f32(r.timeZeroAt - 1)));
  const time = roundHalfEven(f32(r.timeMax * timeFactor));
  const hits = Math.max(0, r.hitsMax - hitsTaken * r.pointsPerHit);
  const killScore = monsters > 0 ? roundHalfEven(f32(r.killsMax * clamp01(f32(kills / monsters)))) : r.killsMax;
  const combo = roundHalfEven(f32(r.comboMax * clamp01(f32(maxCombo / r.comboTarget))));
  const revivePenalty = revives * r.revivePenalty;
  const total = Math.min(100, Math.max(0, time + hits + killScore + combo - revivePenalty));
  return { time, hits, kills: killScore, combo, revive_penalty: revivePenalty, total };
}

/** 난이도 번호(0 일반 1 모험 2 왕 3 영웅 4 악몽 5 지옥 6 심연 7 군주). 레이드는 0 */
function tierIndex(eco: EconomyData, d: DungeonDef, diff: DiffNumbers): number {
  return d.isRaid ? 0 : Math.max(0, eco.dungeons.difficulties.findIndex((x) => x.monsterLevel === diff.monsterLevel));
}

/** 클리어 경험치의 보너스 앞 값: max(clearXp x rewardMul, 하한[난이도]) x xpMul (float32). 소탕은 난이도 번호를 직접 준다 */
export function clearXpBase(d: DungeonDef, diff: Pick<DiffNumbers, 'rewardMul'>, tier: number): number {
  const base = Math.max(f32(d.clearXp * diff.rewardMul), d.clearXpFloor[tier] ?? 0);
  return f32(base * d.xpMul);
}

/** 보너스 퍼센트를 곱해 반올림(짝수 쪽). 직접 플레이는 랭크 보너스, 소탕은 sweep.json 값을 준다 */
export function applyXpBonus(xp: number, bonusPercent: number): number {
  return roundHalfEven(f32(xp * f32(1 + bonusPercent / 100)));
}

/** DungeonRewards.ClearXp: round(clearXp * rewardMul * xpMul * (1 + 랭크보너스/100)) */
export function clearXp(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, rank: number): number {
  return applyXpBonus(clearXpBase(d, diff, tierIndex(eco, d, diff)), xpBonusPercent(eco, rank));
}

export interface Card {
  item_key: string;
  count: number;
}

const rarityIdx = (r: string): number => RARITY_ORDER.indexOf(r as (typeof RARITY_ORDER)[number]);

/** DungeonRewards.RollGear: 몬스터 드롭표 시도(절반) 후 희귀 장비까지 든 가중 풀. +0 기본 id를 돌려준다 */
export function rollGear(eco: EconomyData, cls: string, minRarity: string, rng: Rng, tier = 0): string {
  // 던전 카드 장비는 그 난이도 권장 레벨의 단계 장비만(레전더리는 레이드 별도 굴림)
  const usable = eco.shop.equipmentList.filter(
    (e) => !e.bossOnly && e.levelTier === tier && e.rarity !== 'Legendary' && usableBy(e, cls),
  );
  const min = rarityIdx(minRarity);
  if (rng.int(0, 2) === 0) {
    const dropPool = usable.filter((e) => e.dropWeight > 0);
    const dropTotal = dropPool.reduce((a, e) => a + e.dropWeight, 0);
    for (let i = 0; i < eco.dungeons.cards.gearDropTries && dropTotal > 0; i++) {
      let roll = rng.int(0, dropTotal);
      for (const e of dropPool) {
        if (roll < e.dropWeight) {
          if (rarityIdx(e.rarity) >= min) return keyAt(e.id, 0);
          break;
        }
        roll -= e.dropWeight;
      }
    }
  }
  const pool = usable
    .filter((e) => !e.starter && rarityIdx(e.rarity) >= min)
    .map((e) => ({ id: e.id, w: e.dropWeight > 0 ? e.dropWeight : eco.dungeons.cards.gearRareWeight }));
  const total = pool.reduce((a, p) => a + p.w, 0);
  if (pool.length === 0) {
    // C#: RollDrop(cls, 1) ?? StarterWeapon(cls). 풀이 비는 경우는 데이터 오류 수준이라 가장 흔한 드롭 장비로 대신한다
    const any = usable.find((e) => e.dropWeight > 0);
    return keyAt((any ?? (usable[0] as { id: string })).id, 0);
  }
  let roll = rng.int(0, total);
  for (const p of pool) {
    if (roll < p.w) return keyAt(p.id, 0);
    roll -= p.w;
  }
  return keyAt((pool[0] as { id: string }).id, 0);
}

type RewardEntry = DungeonDef['rewards'][number];

/** 카드 표: 던전 보상 + 난이도 보호권 항목 */
function cardTable(eco: EconomyData, d: DungeonDef, diff: DiffNumbers): RewardEntry[] {
  const table = [...d.rewards];
  if (diff.ticketWeight > 0) {
    table.push({ itemId: eco.enhance.ticketItem, min: 1, max: 1, weight: diff.ticketWeight });
  }
  return table;
}

/** 가중 추첨 한 번(합이 0이면 roll 상한 1, 마지막으로 떨어지면 첫 항목: C# 원본과 같다) */
function pickWeighted(table: RewardEntry[], rng: Rng): RewardEntry {
  const total = table.reduce((a, e) => a + Math.max(0, e.weight), 0);
  let roll = rng.int(0, Math.max(1, total));
  let pick = table[0] as RewardEntry;
  for (const e of table) {
    if (roll < e.weight) {
      pick = e;
      break;
    }
    roll -= e.weight;
  }
  return pick;
}

/** 소탕 옵션: 장비가 뽑히면 gearKeepPercent 확률로만 유지하고 아니면 장비 없는 풀에서 다시 뽑는다. 대박 굴림은 하지 않는다 */
export interface OneCardOpts {
  sweepGearKeepPercent?: number;
}

/** 카드 한 장 굴림(직접 플레이 rollCards의 루프 본문과 같다. 난수 호출 순서는 바뀌지 않는다) */
export function rollOneCard(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng, opts: OneCardOpts = {}): Card {
  const table = cardTable(eco, d, diff);
  const tier = tierOfLevel(diff.recommendedLevel);
  let pick = pickWeighted(table, rng);
  const sweep = opts.sweepGearKeepPercent !== undefined;
  if (sweep && pick.itemId === 'gear' && rng.int(0, 100) >= (opts.sweepGearKeepPercent as number)) {
    const others = table.filter((e) => e.itemId !== 'gear');
    // 장비가 아닌 항목이 하나도 없으면 장비로 둔다
    if (others.length > 0) pick = pickWeighted(others, rng);
  }
  let card: Card;
  if (pick.itemId === 'gear') {
    card = { item_key: rollGear(eco, cls, diff.minGearRarity, rng, tier), count: 1 };
  } else {
    let n = rng.int(pick.min, pick.max + 1);
    // 보호권은 곱하지 않고, 나머지는 난이도 보상 배율을 곱한다
    if (pick.itemId !== eco.enhance.ticketItem) n = Math.max(1, roundHalfEven(f32(n * diff.rewardMul)));
    card = { item_key: pick.itemId, count: n };
  }
  // 대박 카드: 카드마다 아주 낮은 확률(천분율)로 에픽 위 등급(유니크·레전더리) 장비로 바뀐다(C# DungeonRewards와 같은 순서)
  if (!sweep && (diff.jackpotPerMille ?? 0) > 0 && rng.int(0, 1000) < (diff.jackpotPerMille ?? 0)) {
    const jackpot = rollJackpot(eco, cls, rng, tier);
    if (jackpot) card = { item_key: jackpot, count: 1 };
  }
  return card;
}

/** 13단계: 레이드 확정 골드. goldMin + step * int(0 .. (goldMax-goldMin)/step). 난이도 배율은 곱하지 않는다 */
export function rollRaidGold(d: DungeonDef, rng: Rng): number {
  const rr = d.raidReward;
  if (!rr) throw new Error(`레이드 보상 데이터가 없습니다: ${d.id}`);
  return rr.goldMin + rr.goldStep * rng.int(0, Math.floor((rr.goldMax - rr.goldMin) / rr.goldStep) + 1);
}

/** 13단계: 레이드 장비 카드 한 장. 난수 순서는 부위 -> 등급. 단계는 레이드 권장 레벨의 단계, 결과는 +0 키 */
export function rollRaidGear(eco: EconomyData, d: DungeonDef, cls: string, rng: Rng): string {
  const rr = d.raidReward;
  if (!rr || !d.raidNumbers) throw new Error(`레이드 보상 데이터가 없습니다: ${d.id}`);
  const tier = tierOfLevel(d.raidNumbers.recommendedLevel);
  const category = rr.gearCategories[rng.int(0, rr.gearCategories.length)] as string;
  const weights = (['Epic', 'Unique', 'Legendary'] as const).map((r) => ({ r, w: rr.gearRarityWeights[r] ?? 0 }));
  let roll = rng.int(0, weights.reduce((a, x) => a + x.w, 0));
  let rarity: string = weights[0]?.r ?? 'Epic';
  for (const x of weights) {
    if (roll < x.w) {
      rarity = x.r;
      break;
    }
    roll -= x.w;
  }
  // 전설 무기는 단계마다 두 갈래(광역형·토벌형): 등급이 정해진 뒤 같은 확률로 하나를 고른다(C# FindRaidGear와 같다)
  // 그 단계에 그 등급이 없으면(챕터 3 Lv.45에는 레전더리가 없다) 한 등급씩 내려 에픽까지 찾는다(C# FindRaidGear와 같다)
  const order = ['Epic', 'Unique', 'Legendary'];
  let found: typeof eco.shop.equipmentList = [];
  for (let r = order.indexOf(rarity); r >= 0 && found.length === 0; r--) {
    found = eco.shop.equipmentList.filter(
      (e) => !e.bossOnly && e.levelTier === tier && e.category === category && e.rarity === order[r] && usableBy(e, cls),
    );
  }
  if (found.length === 0) throw new Error(`레이드 장비를 찾을 수 없습니다: ${d.id} ${cls} ${category} ${rarity}`);
  const pick = found.length > 1 ? (found[rng.int(0, found.length)] as (typeof found)[number]) : (found[0] as (typeof found)[number]);
  return keyAt(pick.id, 0);
}

/** 13단계: 레이드 카드 4장(각각 독립 추첨, 수량은 표 그대로). 골드 카드는 없다 */
export function rollRaidCards(eco: EconomyData, d: DungeonDef, cls: string, rng: Rng): Card[] {
  const rr = d.raidReward;
  if (!rr) throw new Error(`레이드 보상 데이터가 없습니다: ${d.id}`);
  const cards: Card[] = [];
  for (let i = 0; i < eco.dungeons.cards.count; i++) {
    const pick = pickWeighted(rr.cards, rng);
    if (pick.itemId === 'gear') cards.push({ item_key: rollRaidGear(eco, d, cls, rng), count: 1 });
    else cards.push({ item_key: pick.itemId, count: rng.int(pick.min, pick.max + 1) });
  }
  return cards;
}

/** DungeonRewards.RollCards + Resolve */
export function rollCards(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng): Card[] {
  const cards: Card[] = [];
  for (let i = 0; i < eco.dungeons.cards.count; i++) cards.push(rollOneCard(eco, d, diff, cls, rng));
  return cards;
}

/** 클리어 운 카드 수(확정 재료·골드 2장 뒤). DungeonDatabase.LuckCards와 같다 */
export const LUCK_CARDS = 2;

/**
 * 2026-10-14 클리어 보상(레이드 제외, DungeonRewards.RollCards와 같은 구성): 확정 재료 1장 + 확정 골드 1장 +
 * 운 카드 LUCK_CARDS장(rollOneCard: 장비·재료·대박). 모두 받는다(take_all). sure가 없는 옛 데이터는 null
 */
export function rollClearRewards(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng): Card[] | null {
  const sure = d.sure;
  if (!sure) return null;
  const mat = Math.max(1, roundHalfEven(f32(rng.int(sure.materialMin, sure.materialMax + 1) * diff.rewardMul)));
  const gold = Math.max(1, roundHalfEven(f32(rng.int(sure.goldMin, sure.goldMax + 1) * diff.rewardMul)));
  const cards: Card[] = [
    { item_key: sure.material, count: mat },
    { item_key: 'gold', count: gold },
  ];
  for (let i = 0; i < LUCK_CARDS; i++) cards.push(rollOneCard(eco, d, diff, cls, rng));
  return cards;
}

/**
 * 2026-10-14 요일 던전 유니크 보너스: 하루 3판을 하면 3일에 약 2개(판당 22%). 불운 보정으로 유니크 없이 7판이 지나면
 * 8판째는 반드시 나온다. 나올 때는 그 단계에서 아직 유니크가 없는 부위를 먼저 고른다(모으는 데 끝없이 걸리지 않게).
 * 운 카드 한 장(마지막)을 그 유니크로 바꾼다. 일반 던전·레이드·소탕에는 적용하지 않는다
 */
export const UNIQUE_BONUS = { chancePct: 22, pityRuns: 8 } as const;

/** 이 던전·난이도가 유니크 보너스 대상인가(요일 던전, 그 단계에 유니크 장비가 있을 때) */
export function uniqueBonusApplies(d: DungeonDef): boolean {
  return !d.isRaid && !d.always && !!d.sure;
}

/**
 * 유니크 보너스 굴림. misses = 지금까지 유니크 없이 지난 판 수. ownedUniqueCategories = 그 단계에서 이미 가진 유니크의 부위.
 * 반환: 유니크 키(안 나오면 null)와 다음 misses. 난수 순서: 확률 1회 -> (나오면) 부위 1회 -> 같은 부위 안 장비 1회
 */
export function rollUniqueBonus(
  eco: EconomyData,
  diff: DiffNumbers,
  cls: string,
  rng: Rng,
  misses: number,
  ownedUniqueCategories: ReadonlySet<string>,
): { key: string | null; misses: number } {
  const hit = rng.int(0, 100) < UNIQUE_BONUS.chancePct || misses + 1 >= UNIQUE_BONUS.pityRuns;
  if (!hit) return { key: null, misses: misses + 1 };
  const tier = tierOfLevel(diff.recommendedLevel);
  const pool = eco.shop.equipmentList.filter(
    (e) => !e.starter && !e.bossOnly && e.levelTier === tier && e.rarity === 'Unique' && usableBy(e, cls),
  );
  if (pool.length === 0) return { key: null, misses: misses + 1 };
  const categories = [...new Set(pool.map((e) => e.category))].sort();
  const missing = categories.filter((c) => !ownedUniqueCategories.has(c));
  const from = missing.length > 0 ? missing : categories;
  const category = from[rng.int(0, from.length)] as string;
  const items = pool.filter((e) => e.category === category);
  const item = items[rng.int(0, items.length)] as (typeof items)[number];
  return { key: keyAt(item.id, 0), misses: 0 };
}

/** 그 단계 유니크의 부위(가진 아이템 키 목록에서). 강화 단계(+n)는 떼고 본다 */
export function uniqueCategoriesOwned(eco: EconomyData, tier: number, itemKeys: readonly string[]): Set<string> {
  const byId = new Map(eco.shop.equipmentList.map((e) => [e.id, e] as const));
  const out = new Set<string>();
  for (const k of itemKeys) {
    const e = byId.get(k.split('+')[0] as string);
    if (e && e.rarity === 'Unique' && e.levelTier === tier) out.add(e.category);
  }
  return out;
}

export interface RareChance {
  /** 카드 한 장이 그 등급 장비일 확률 */
  card: number;
  /** 한 번 클리어해서 그 등급 장비를 한 장 이상 받을 확률 */
  clear: number;
}

/**
 * 2026-10-14 유니크·레전더리 장비 확률(선택 화면 표시용, 서버 굴림 규칙 그대로 계산). 운이 들어가는 카드 수:
 * 레이드 4장, 확정 보상이 있는 던전 LUCK_CARDS장, 옛 4장 중 1장은 1장. DungeonRewards.RareChances와 같은 식
 */
export function rareChances(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string): { unique: RareChance; legendary: RareChance; cards: number } {
  const per = (p: number, n: number): RareChance => ({ card: p, clear: 1 - Math.pow(1 - p, n) });
  const rr = d.raidReward;
  if (d.isRaid && rr) {
    const total = rr.cards.reduce((a, e) => a + e.weight, 0);
    const gear = rr.cards.filter((e) => e.itemId === 'gear').reduce((a, e) => a + e.weight, 0) / Math.max(1, total);
    const ws = rr.gearRarityWeights as Record<string, number>;
    const sum = (ws.Epic ?? 0) + (ws.Unique ?? 0) + (ws.Legendary ?? 0);
    const n = eco.dungeons.cards.count;
    return { unique: per((gear * (ws.Unique ?? 0)) / Math.max(1, sum), n), legendary: per((gear * (ws.Legendary ?? 0)) / Math.max(1, sum), n), cards: n };
  }
  const tier = tierOfLevel(diff.recommendedLevel);
  const table = cardTable(eco, d, diff);
  const total = table.reduce((a, e) => a + Math.max(0, e.weight), 0);
  const gearCard = table.filter((e) => e.itemId === 'gear').reduce((a, e) => a + e.weight, 0) / Math.max(1, total);
  // rollGear: 절반은 드롭표 시도(최대 gearDropTries번, 최소 등급 이상이면 그 장비), 나머지와 실패분은 가중 풀
  const usable = eco.shop.equipmentList.filter(
    (e) => !e.bossOnly && e.levelTier === tier && e.rarity !== 'Legendary' && usableBy(e, cls),
  );
  const min = rarityIdx(diff.minGearRarity);
  const dropPool = usable.filter((e) => e.dropWeight > 0);
  const dropTotal = dropPool.reduce((a, e) => a + e.dropWeight, 0);
  const tries = eco.dungeons.cards.gearDropTries;
  let qOk = 0;
  let qUnique = 0;
  for (const e of dropPool) {
    if (rarityIdx(e.rarity) < min) continue;
    qOk += e.dropWeight / dropTotal;
    if (e.rarity === 'Unique') qUnique += e.dropWeight / dropTotal;
  }
  const dropHitAll = dropTotal > 0 && qOk > 0 ? 1 - Math.pow(1 - qOk, tries) : 0;
  const fromDrop = qOk > 0 ? (qUnique / qOk) * dropHitAll : 0;
  const pool = usable.filter((e) => !e.starter && rarityIdx(e.rarity) >= min).map((e) => ({ r: e.rarity, w: e.dropWeight > 0 ? e.dropWeight : eco.dungeons.cards.gearRareWeight }));
  const poolTotal = pool.reduce((a, p) => a + p.w, 0);
  const fromPool = poolTotal > 0 ? pool.filter((p) => p.r === 'Unique').reduce((a, p) => a + p.w, 0) / poolTotal : 0;
  const uniqueOfGear = 0.5 * fromDrop + (0.5 + 0.5 * (1 - dropHitAll)) * fromPool;
  // 대박: 같은 단계 유니크 풀이 있을 때만 카드가 바뀐다(레전더리는 레이드에서만)
  const jackpotPool = eco.shop.equipmentList.some(
    (e) => !e.starter && !e.bossOnly && e.levelTier === tier && e.rarity === 'Unique' && usableBy(e, cls),
  );
  const pj = jackpotPool ? (diff.jackpotPerMille ?? 0) / 1000 : 0;
  const n = d.sure ? LUCK_CARDS : 1;
  return { unique: per(pj + (1 - pj) * gearCard * uniqueOfGear, n), legendary: per(0, n), cards: n };
}

/** 유니크 3 : 레전더리 1 가중치로, 직업이 쓸 수 있는 에픽 위 장비 하나(+0). 없으면 null */
export function rollJackpot(eco: EconomyData, cls: string, rng: Rng, tier = 0): string | null {
  // 대박 카드: 같은 단계의 유니크(레전더리는 레이드에서만)
  const pool = eco.shop.equipmentList
    .filter((e) => !e.starter && !e.bossOnly && e.levelTier === tier && e.rarity === 'Unique' && usableBy(e, cls))
    .map((e) => ({ id: e.id, w: rarityIdx(e.rarity) >= rarityIdx('Legendary') ? 1 : 3 }));
  const total = pool.reduce((a, p) => a + p.w, 0);
  if (total === 0) return null;
  let roll = rng.int(0, total);
  for (const p of pool) {
    if (roll < p.w) return keyAt(p.id, 0);
    roll -= p.w;
  }
  return null;
}

export function roomTotal(d: DungeonDef, roomIndex: number): number {
  const room = d.rooms[roomIndex];
  return room ? room.groups.reduce((a, g) => a + g.count, 0) : 0;
}

export function monsterTotal(d: DungeonDef): number {
  return d.rooms.reduce((a, _r, i) => a + roomTotal(d, i), 0);
}

/** 방의 받아들인 처치 수(room_kills의 "방:몬스터" 키 합) */
export function roomKillCount(roomKills: Record<string, number>, roomIndex: number): number {
  let n = 0;
  for (const [k, v] of Object.entries(roomKills)) if (k.startsWith(`${roomIndex}:`)) n += v;
  return n;
}

/** 난이도 수치: 요일 던전은 난이도 표, 레이드는 레이드별 raidNumbers(레이드는 난이도가 하나) */
export type DiffNumbers = Pick<
  DifficultyDef,
  'recommendedLevel' | 'hpMul' | 'rewardMul' | 'monsterLevel' | 'revives' | 'minGearRarity' | 'ticketWeight'
> & { jackpotPerMille?: number };

/** 레이드 난이도: 0 = 레이드, 1 = 영웅 레이드(raidHeroic 배율). 그 밖은 없다 */
export const RAID_HEROIC = 1;

/**
 * 2026-10-29 요일 던전 전용 난이도의 첫 번호(DungeonDifficulty.Nightmare): 4 악몽(Lv32, Lv30 장비) 5 지옥(Lv37, Lv35 장비)
 * 6 심연(Lv40, Lv40 장비), 그리고 7 군주. 일반 던전은 0~3, 레이드는 0~1만 있다(Docs/PLAN_WEEKDAY_TIERS_2026-10-29.md)
 */
export const FIRST_WEEKDAY_ONLY = 4;
/** 챕터 3 요일 던전 "군주" 난이도 번호(DungeonDifficulty.Lord). 2026-10-29 전에는 4였다(마이그레이션 0060이 4를 7로 옮겼다) */
export const LORD_DIFFICULTY = 7;
/** 군주 난이도 입장 최소 레벨(권장 45 - 여유와 별개로 3장 구간 시작) */
export const LORD_MIN_LEVEL = 41;

/** 요일 던전 전용 난이도(악몽~군주)가 있는 던전인가: 요일 던전(레이드·일반 던전이 아님) */
export function hasWeekdayTiers(d: DungeonDef): boolean {
  return !d.isRaid && !d.always;
}

/** 이 던전에 군주 난이도가 있는가(요일 던전, 데이터에 군주 난이도가 있을 때). 열림 여부는 chapter3Open이 따로 본다 */
export function hasLord(eco: EconomyData, d: DungeonDef): boolean {
  return hasWeekdayTiers(d) && eco.dungeons.difficulties.length > LORD_DIFFICULTY;
}

/** 목록·소탕 창에 보일 난이도 수: 일반 던전은 영웅까지, 요일 던전은 심연까지, 군주는 3장이 열린 요일 던전만 */
export function shownDifficulties(eco: EconomyData, d: DungeonDef, chapter3IsOpen: boolean): number {
  const n = eco.dungeons.difficulties.length;
  if (!hasWeekdayTiers(d)) return Math.min(FIRST_WEEKDAY_ONLY, n);
  return hasLord(eco, d) && chapter3IsOpen ? n : Math.min(LORD_DIFFICULTY, n);
}

/** 군주 클리어 추가 확정 재료(카드가 아니라 클리어 때 가방에 바로). 군주 판이 아니거나 데이터가 없으면 null */
export function rollLordBonus(d: DungeonDef, difficulty: number, rng: Rng): Card | null {
  if (difficulty !== LORD_DIFFICULTY || d.isRaid || !d.lordSure) return null;
  return { item_key: d.lordSure.material, count: rng.int(d.lordSure.min, d.lordSure.max + 1) };
}

export function diffOf(eco: EconomyData, d: DungeonDef, index: number): DiffNumbers | null {
  if (d.isRaid) {
    const base = d.raidNumbers;
    if (!base) return null;
    if (index === 0) return base;
    const h = eco.dungeons.raidHeroic;
    if (index !== RAID_HEROIC || !h) return null;
    return { ...base, hpMul: base.hpMul * h.hpMul, rewardMul: base.rewardMul * h.rewardMul };
  }
  if (index >= FIRST_WEEKDAY_ONLY && !hasWeekdayTiers(d)) return null; // 악몽~군주는 요일 던전만
  if (index === LORD_DIFFICULTY && !hasLord(eco, d)) return null;
  return eco.dungeons.difficulties[index] ?? null;
}

/** ResetClock.IsOpen: 레이드는 openDays만 본다. 요일 던전은 토·일 전부 개방(weekendOpensAll), 아니면 openDays에 오늘 요일 */
export function isOpenToday(eco: EconomyData, d: DungeonDef, now: Date): boolean {
  // 시험 서버 전용: TEST_OPEN_ALL_DAYS=1 이고 DEPLOY_STAGE=test 이면 요일과 상관없이 모두 연다(운영에서는 두 값이 같이 있을 수 없다)
  if (process.env.DEPLOY_STAGE === 'test' && process.env.TEST_OPEN_ALL_DAYS === '1') return true;
  const day = gameWeekday(now);
  if (d.isRaid) return d.openDays.includes(day);
  if (d.always) return true;
  if (eco.dungeons.weekendOpensAll && (day === 'Saturday' || day === 'Sunday')) return true;
  return d.openDays.includes(day);
}
