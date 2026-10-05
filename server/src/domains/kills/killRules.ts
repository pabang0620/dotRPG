// 처치 판정의 순수 규칙(DB 없음): 경험치, 실효 HP, 화력 상한, 드롭 굴림.
// C# 원본: MonsterDatabase.SpawnDef, EnemyController.DropLoot/MonsterLoot, GoldRunnerBehaviour.OnHit, EquipmentDatabase.RollDrop
import type { EconomyData, MonsterDef } from '../../gamedata/economyData';
import { keyAt, parseItemKey, roundHalfEven } from '../../utils/itemKey';
import { COSMETIC_DAMAGE_MAX } from '../starshop/starshopDefs';
import type { Rng } from '../../utils/rng';

/** 몬스터 경험치: xpByLevel 표가 있으면 표, 없으면 round(xp * (1 + xpPerLevel * (레벨 - 1))) (.5는 짝수 쪽) */
export function monsterXp(eco: EconomyData, def: MonsterDef, level: number): number {
  const table = def.xpByLevel;
  if (table && level >= 1 && level <= table.length) return table[level - 1] as number;
  return roundHalfEven(Math.fround(def.xp * Math.fround(1 + eco.monsterRules.xpPerLevel * (level - 1))));
}

/** 실효 HP = hp * hpMul * (1 + hpPerLevel * (레벨 - 1)) */
export function effectiveHp(eco: EconomyData, def: MonsterDef, level: number, hpMul: number): number {
  return def.hp * hpMul * (1 + eco.monsterRules.hpPerLevel * (level - 1));
}

/** 장비 키 하나의 공격력(강화 단계 반영). 단계표 밖(만렙)은 마지막 증가분을 한 번 더 더한다 */
export function gearAttack(eco: EconomyData, key: string): number {
  const p = parseItemKey(key);
  if (!p) return 0;
  const levels = eco.enhance.steps.get(p.base);
  if (!levels || levels.length === 0) return 0;
  if (p.level < levels.length) return (levels[p.level] as { statsAttack: number }).statsAttack;
  const last = (levels[levels.length - 1] as { statsAttack: number }).statsAttack;
  const prev = levels.length > 1 ? (levels[levels.length - 2] as { statsAttack: number }).statsAttack : last;
  return last + Math.max(0, last - prev);
}

export interface PowerPolicy {
  powerPassivePerLevel: number;
  powerSkillFactor: number;
  powerAoeCap: number;
}

/** 3.2.3 attackCap(level, worn) */
export function attackCap(eco: EconomyData, pol: PowerPolicy, level: number, wornKeys: string[]): number {
  const gear = wornKeys.reduce((a, k) => a + gearAttack(eco, k), 0);
  // 착용 외형(오라·코스튬 스킨)의 공격력 보너스는 클라이언트 설정이라, 가능한 최대치만큼 넓힌다
  return (eco.player.attackDamage + gear) * (1 + pol.powerPassivePerLevel * (level - 1)) * (1 + COSMETIC_DAMAGE_MAX / 100);
}

/** 창 안 실효 HP 합이 화력 상한 x 창 길이 x AOE_CAP + 가장 큰 몬스터 HP 이하이면 true */
export function powerAllows(
  eco: EconomyData,
  pol: PowerPolicy,
  cap: number,
  windowSeconds: number,
  hpList: number[],
): boolean {
  const dpsCap = (cap / eco.player.attackCooldown) * pol.powerSkillFactor;
  const total = hpList.reduce((a, b) => a + b, 0);
  const biggest = hpList.reduce((a, b) => Math.max(a, b), 0);
  return total <= dpsCap * windowSeconds * pol.powerAoeCap + biggest;
}

export interface DropSpec {
  itemKey: string;
  count: number;
}

/** 추가 골드 총액을 더미로 나눈다: 더미 수 clamp(총액 / divisor, min, max), 마지막 더미가 나머지 */
export function splitGoldPiles(eco: EconomyData, total: number): number[] {
  const l = eco.monsterRules.loot;
  const piles = Math.min(l.goldPileMax, Math.max(l.goldPileMin, Math.floor(total / l.goldPileDivisor)));
  const each = Math.floor(total / piles);
  const out: number[] = [];
  for (let i = 0; i < piles; i++) out.push(i === piles - 1 ? total - each * (piles - 1) : each);
  return out;
}

/** 장비 드롭: u <= chance 이면 dropWeight 가중으로 하나(직업이 쓸 수 있는 것, +0 기본 id). 없으면 null */
export function rollEquipmentDrop(eco: EconomyData, cls: string, chance: number, rng: Rng): string | null {
  if (rng.unit() > chance) return null;
  const pool = eco.shop.equipmentList.filter(
    (e) => e.dropWeight > 0 && (e.classOnly === null || e.classOnly === cls),
  );
  const total = pool.reduce((a, e) => a + e.dropWeight, 0);
  if (total <= 0) return null;
  let roll = rng.int(0, total);
  for (const e of pool) {
    if (roll < e.dropWeight) return keyAt(e.id, 0);
    roll -= e.dropWeight;
  }
  return null;
}

/** 처치 한 번의 드롭 전체(3.1.2). hits는 이미 서버가 자른 값 */
/** chanceMul: 파티 세션의 레벨 격차 감쇠·경제 손잡이(재료·장비 확률에 곱한다, 골드 금액은 아니다). 기본 1 */
export function rollKillDrops(eco: EconomyData, def: MonsterDef, cls: string, hits: number, rng: Rng, chanceMul = 1): DropSpec[] {
  const out: DropSpec[] = [];
  // 타격당 골드(황금 해골): 타격마다 더미 1개
  if (def.goldPerHitMax > 0) {
    for (let i = 0; i < hits; i++) {
      out.push({ itemKey: 'gold', count: rng.int(def.goldPerHitMin, def.goldPerHitMax + 1) });
    }
  }
  // 몬스터 추가 골드(황금 해골, 보스): 총액 [goldMin, goldMax] 폐구간
  if (def.goldMax > 0) {
    const total = rng.int(def.goldMin, def.goldMax + 1);
    for (const part of splitGoldPiles(eco, total)) if (part > 0) out.push({ itemKey: 'gold', count: part });
  }
  // 기본 골드 1더미 [fieldGoldMin, fieldGoldMaxExclusive)
  const r = eco.monsterRules;
  out.push({ itemKey: 'gold', count: rng.int(r.fieldGoldMin, r.fieldGoldMaxExclusive) });
  // 재료: u <= dropChance 이면 [minDrop, maxDrop] 폐구간 개, 한 개가 한 행
  for (const m of eco.shop.materials) {
    if (rng.unit() > m.dropChance * chanceMul) continue;
    const n = rng.int(m.minDrop, m.maxDrop + 1);
    for (let i = 0; i < n; i++) out.push({ itemKey: m.id, count: 1 });
  }
  const gear = rollEquipmentDrop(eco, cls, r.equipmentDropChance * chanceMul, rng);
  if (gear) out.push({ itemKey: gear, count: 1 });
  return out;
}
