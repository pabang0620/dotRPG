// 장비 세트 효과(Docs/PLAN_GEAR_SETS_2026-10-29.md). 같은 레벨 단계(Lv.20 이상) 장비를 무기·상의·하의·목걸이·반지 5부위에
// 몇 개 착용했는지 센다(반지 두 개는 한 부위). 등급은 섞여도 되고, 효과 크기는 센 부위들 중 가장 낮은 등급을 따른다.
// 2·3·5부위 단계가 쌓이고, %는 착용 장비의 공격력 합·체력 합에 곱한다. 계승(^k)·히든(~x) 키는 그 장비의 단계로 센다.
// C# 원본: Items/GearSets.cs (표가 같아야 한다)
import type { EconomyData, EquipmentDef } from '../gamedata/economyData';
import { parseItemKey } from './itemKey';

/** 세트가 있는 가장 낮은 레벨 단계 번호(3 = Lv.20). Lv.1~15는 손으로 맞춘 균형을 그대로 둔다 */
export const SET_MIN_TIER = 3;
export const SET_PARTS = 5;
export const SET_STEPS: readonly number[] = [2, 3, 5];

const RARITY_ORDER: readonly EquipmentDef['rarity'][] = ['Common', 'Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary'];

/** [그룹][단계] %. 그룹 0 = 커먼~레어(체력만), 1 에픽, 2 유니크, 3 레전더리 */
const ATTACK_PCT: readonly (readonly number[])[] = [[0, 0, 0], [0, 5, 5], [0, 6, 6], [0, 7, 0]];
const HEALTH_PCT: readonly (readonly number[])[] = [[3, 3, 4], [5, 0, 5], [6, 0, 6], [7, 0, 0]];
/** 최종 공격력 %(캐릭터 공격력 전체에 곱한다, 2026-10-30): 레전더리 5부위만 +30 */
const FINAL_PCT: readonly (readonly number[])[] = [[0, 0, 0], [0, 0, 0], [0, 0, 0], [0, 0, 30]];

function groupOf(lowest: number): number {
  return lowest >= 5 ? 3 : lowest >= 4 ? 2 : lowest >= 3 ? 1 : 0;
}

function partOf(category: EquipmentDef['category']): number {
  switch (category) {
    case 'Weapon':
      return 0;
    case 'Top':
      return 1;
    case 'Bottom':
      return 2;
    case 'Necklace':
      return 3;
    default:
      return 4;
  }
}

export interface SetBonus {
  attackPct: number;
  healthPct: number;
  finalPct: number;
}

/** 착용 키 전체의 세트 효과 합(단계별로 따로 센 뒤 더한다) */
export function setBonusOf(eco: EconomyData, wornKeys: readonly string[]): SetBonus {
  // 단계 번호 -> 부위별 가장 높은 등급 순번
  const tiers = new Map<number, number[]>();
  for (const key of wornKeys) {
    const p = parseItemKey(key);
    const e = p ? eco.shop.equipment.get(p.base) : undefined;
    if (!e || e.levelTier < SET_MIN_TIER) continue;
    let best = tiers.get(e.levelTier);
    if (!best) {
      best = [-1, -1, -1, -1, -1];
      tiers.set(e.levelTier, best);
    }
    const part = partOf(e.category);
    best[part] = Math.max(best[part] as number, RARITY_ORDER.indexOf(e.rarity));
  }
  let attackPct = 0;
  let healthPct = 0;
  let finalPct = 0;
  for (const best of tiers.values()) {
    const counted = best.filter((r) => r >= 0);
    const g = groupOf(Math.min(...counted));
    SET_STEPS.forEach((step, i) => {
      if (counted.length < step) return;
      attackPct += (ATTACK_PCT[g] as readonly number[])[i] as number;
      healthPct += (HEALTH_PCT[g] as readonly number[])[i] as number;
      finalPct += (FINAL_PCT[g] as readonly number[])[i] as number;
    });
  }
  return { attackPct, healthPct, finalPct };
}
