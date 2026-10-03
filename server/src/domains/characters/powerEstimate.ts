// 서버가 계산하는 전투력(power_estimate). 클라이언트가 보낸 전투력은 받지 않는다.
// C# CharacterStatsCalc.Power = 공격*10 + 체력*5 + 마나*3 + 막기*20 + 이동속도*10 + 레벨*50 중 서버가 아는 항(패시브 제외).
import type { Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { parseItemKey } from '../../utils/itemKey';
import { listWornKeys } from '../economy/economyRepository';
import { gearAttack } from '../kills/killRules';
import { sumBonusMaxHealth } from '../quests/questRepository';

export function computePower(
  cls: string,
  level: number,
  wornKeys: string[],
  bonusMaxHealth: number,
): number {
  const eco = getGameData().economy;
  const p = eco.player.power;
  const equip = eco.shop.equipment;
  let attack = cls === 'mage' ? eco.player.mageBoltDamage : eco.player.attackDamage;
  let health = eco.player.maxHealth + bonusMaxHealth + (level - 1) * p.hpPerLevel;
  let block = 0;
  let speed = 0;
  for (const key of wornKeys) {
    attack += gearAttack(eco, key);
    const base = parseItemKey(key)?.base;
    const e = base ? equip.get(base) : undefined;
    if (!e) continue;
    health += Number((e as { maxHealth?: number }).maxHealth ?? 0);
    block += Number((e as { block?: number }).block ?? 0);
    speed += Number((e as { speed?: number }).speed ?? 0);
  }
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
