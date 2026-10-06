// 솔로 던전의 순수 규칙. C# 원본: DungeonRanking, DungeonRewards, ResetClock.IsOpen
import { tierOfLevel } from '../../utils/gearTier';
import type { DifficultyDef, DungeonDef, EconomyData } from '../../gamedata/economyData';
import { RARITY_ORDER } from '../../gamedata/economyData';
import { f32, keyAt, roundHalfEven } from '../../utils/itemKey';
import { gameWeekday } from '../../utils/resetBoundaries';
import type { Rng } from '../../utils/rng';

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

/** 난이도 번호(0 일반 ... 3 영웅). 레이드는 0 */
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

/** 레이드 장비 카드가 레전더리로 바뀌는 천분율 */
export const RAID_LEGENDARY_PERMILLE = 5;
const CATEGORIES = ['Weapon', 'Top', 'Bottom', 'Necklace', 'Ring'] as const;
const pickCategory = (rng: Rng): string => CATEGORIES[rng.int(0, CATEGORIES.length)] as string;

const rarityIdx = (r: string): number => RARITY_ORDER.indexOf(r as (typeof RARITY_ORDER)[number]);

/** DungeonRewards.RollGear: 몬스터 드롭표 시도(절반) 후 희귀 장비까지 든 가중 풀. +0 기본 id를 돌려준다 */
export function rollGear(eco: EconomyData, cls: string, minRarity: string, rng: Rng, tier = 0): string {
  // 던전 카드 장비는 그 난이도 권장 레벨의 단계 장비만(레전더리는 레이드 별도 굴림)
  const usable = eco.shop.equipmentList.filter(
    (e) => !e.bossOnly && e.levelTier === tier && e.rarity !== 'Legendary' && (e.classOnly === null || e.classOnly === cls),
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
    // 레이드 장비 카드: 아주 낮은 확률로 그 단계의 레전더리(Lv.20 해골왕, Lv.40 그라흐)
    if (d.isRaid && rng.int(0, 1000) < RAID_LEGENDARY_PERMILLE) {
      const legend = eco.shop.equipmentList.find(
        (e) => e.rarity === 'Legendary' && e.levelTier === tier && (e.classOnly === null || e.classOnly === cls) && e.category === pickCategory(rng),
      );
      if (legend) card = { item_key: keyAt(legend.id, 0), count: 1 };
    }
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

/** DungeonRewards.RollCards + Resolve */
export function rollCards(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng): Card[] {
  const cards: Card[] = [];
  for (let i = 0; i < eco.dungeons.cards.count; i++) cards.push(rollOneCard(eco, d, diff, cls, rng));
  return cards;
}

/** 유니크 3 : 레전더리 1 가중치로, 직업이 쓸 수 있는 에픽 위 장비 하나(+0). 없으면 null */
export function rollJackpot(eco: EconomyData, cls: string, rng: Rng, tier = 0): string | null {
  // 대박 카드: 같은 단계의 유니크(레전더리는 레이드에서만)
  const pool = eco.shop.equipmentList
    .filter((e) => !e.starter && !e.bossOnly && e.levelTier === tier && e.rarity === 'Unique' && (e.classOnly === null || e.classOnly === cls))
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

export function diffOf(eco: EconomyData, d: DungeonDef, index: number): DiffNumbers | null {
  if (d.isRaid) return d.raidNumbers ?? null;
  return eco.dungeons.difficulties[index] ?? null;
}

/** ResetClock.IsOpen: 레이드는 openDays만 본다. 요일 던전은 토·일 전부 개방(weekendOpensAll), 아니면 openDays에 오늘 요일 */
export function isOpenToday(eco: EconomyData, d: DungeonDef, now: Date): boolean {
  const day = gameWeekday(now);
  if (d.isRaid) return d.openDays.includes(day);
  if (eco.dungeons.weekendOpensAll && (day === 'Saturday' || day === 'Sunday')) return true;
  return d.openDays.includes(day);
}
