// 서버가 계산하는 전투력(power_estimate). 클라이언트가 보낸 전투력은 받지 않는다.
// C# CharacterStatsCalc.Power = 공격*10 + 체력*5 + 마나*3 + 막기*20 + 이동속도*10 + 레벨*50 중 서버가 아는 항(패시브 제외).
import type { Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { parseItemKey } from '../../utils/itemKey';
import { listWornKeys } from '../economy/economyRepository';
import { gearAttack } from '../kills/killRules';
import { sumBonusMaxHealth } from '../quests/questRepository';
import { HIDDEN_VALUE } from '../../utils/hiddenOption';
import { setBonusOf } from '../../utils/gearSet';

export function computePower(
  cls: string,
  level: number,
  wornKeys: string[],
  bonusMaxHealth: number,
): number {
  const eco = getGameData().economy;
  const p = eco.player.power;
  const equip = eco.shop.equipment;
  // 기본 공격력: 마법사는 구슬 피해, 전사·도적은 attackDamage(10, 도적은 전사와 같다)
  const baseAttack = cls === 'mage' ? eco.player.mageBoltDamage : eco.player.attackDamage;
  const baseHealth = eco.player.maxHealth + bonusMaxHealth + (level - 1) * p.hpPerLevel;
  let gearAtk = 0;
  let gearHp = 0;
  let block = 0;
  let speed = 0;
  for (const key of wornKeys) {
    gearAtk += gearAttack(eco, key);
    const parsed = parseItemKey(key);
    const e = parsed ? equip.get(parsed.base) : undefined;
    if (!e) continue;
    // 히든 옵션: 최대 체력은 이 방어구 체력 x1.3, 이동 속도 +10(공격 x1.3은 gearAttack이 한다)
    const hp = Number((e as { maxHealth?: number }).maxHealth ?? 0);
    gearHp += parsed?.hidden === 'h' ? hp * (1 + HIDDEN_VALUE.h / 100) : hp;
    block += Number((e as { block?: number }).block ?? 0);
    speed += Number((e as { speed?: number }).speed ?? 0) + (parsed?.hidden === 'm' ? HIDDEN_VALUE.m : 0);
  }
  // 세트 효과: 장비 공격력 합·체력 합에 % (Docs/PLAN_GEAR_SETS_2026-10-29.md, C# Equipment.AttackBonus·MaxHealthBonus)
  const set = setBonusOf(eco, wornKeys);
  const attack = baseAttack + gearAtk * (1 + set.attackPct / 100);
  const health = baseHealth + gearHp * (1 + set.healthPct / 100);
  const mana = p.baseMana + (level - 1) * p.mpPerLevel;
  return Math.max(
    0,
    Math.round(
      Math.max(1, attack) * p.attackWeight +
        Math.max(1, health) * p.hpWeight +
        mana * p.mpWeight +
        Math.min(block, p.maxBlock) * p.blockWeight +
        speed * p.speedWeight +
        level * p.levelWeight,
    ),
  );
}

export async function powerOf(db: Queryable, characterId: number, cls: string, level: number): Promise<number> {
  const worn = await listWornKeys(db, characterId);
  const bonus = await sumBonusMaxHealth(db, characterId);
  return computePower(cls, level, worn, bonus);
}
