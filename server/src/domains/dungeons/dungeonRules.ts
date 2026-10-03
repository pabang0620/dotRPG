// 솔로 던전의 순수 규칙. C# 원본: DungeonRanking, DungeonRewards, ResetClock.IsOpen
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

/** DungeonRewards.ClearXp: round(clearXp * rewardMul * xpMul * (1 + 랭크보너스/100)) */
export function clearXp(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, rank: number): number {
  const xp = f32(f32(d.clearXp * diff.rewardMul) * d.xpMul);
  return roundHalfEven(f32(xp * f32(1 + xpBonusPercent(eco, rank) / 100)));
}

export interface Card {
  item_key: string;
  count: number;
}

const rarityIdx = (r: string): number => RARITY_ORDER.indexOf(r as (typeof RARITY_ORDER)[number]);

/** DungeonRewards.RollGear: 몬스터 드롭표 시도(절반) 후 희귀 장비까지 든 가중 풀. +0 기본 id를 돌려준다 */
export function rollGear(eco: EconomyData, cls: string, minRarity: string, rng: Rng): string {
  const usable = eco.shop.equipmentList.filter((e) => e.classOnly === null || e.classOnly === cls);
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

/** DungeonRewards.RollCards + Resolve */
export function rollCards(eco: EconomyData, d: DungeonDef, diff: DiffNumbers, cls: string, rng: Rng): Card[] {
  const table = [...d.rewards];
  if (diff.ticketWeight > 0) {
    table.push({ itemId: eco.enhance.ticketItem, min: 1, max: 1, weight: diff.ticketWeight });
  }
  const total = table.reduce((a, e) => a + Math.max(0, e.weight), 0);
  const cards: Card[] = [];
  for (let i = 0; i < eco.dungeons.cards.count; i++) {
    let roll = rng.int(0, Math.max(1, total));
    let pick = table[0] as (typeof table)[number];
    for (const e of table) {
      if (roll < e.weight) {
        pick = e;
        break;
      }
      roll -= e.weight;
    }
    if (pick.itemId === 'gear') {
      cards.push({ item_key: rollGear(eco, cls, diff.minGearRarity, rng), count: 1 });
      continue;
    }
    let n = rng.int(pick.min, pick.max + 1);
    // 보호권은 곱하지 않고, 나머지는 난이도 보상 배율을 곱한다
    if (pick.itemId !== eco.enhance.ticketItem) n = Math.max(1, roundHalfEven(f32(n * diff.rewardMul)));
    cards.push({ item_key: pick.itemId, count: n });
  }
  return cards;
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
>;

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
