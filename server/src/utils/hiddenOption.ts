// 장비 히든 옵션(Docs/PLAN_GEAR_HIDDEN_2026-10-29.md). 유니크·레전더리 무기·상의·하의가 드롭으로 생길 때 2% 확률로
// 옵션 하나가 붙는다. 서버만 굴리고, 키 맨 뒤 "~글자"로 저장한다(utils/itemKey). 강화·승급·계승은 그대로 옮긴다.
// C# 원본: Items/HiddenOptions.cs (글자·수치가 같아야 한다)
import type { EconomyData, EquipmentDef } from '../gamedata/economyData';
import { keyAt, parseItemKey } from './itemKey';
import { cryptoRng, type Rng } from './rng';

export type HiddenCode = 'd' | 'r' | 's' | 'c' | 'h' | 'g' | 'm';

/** 무기: 피해(이 무기 공격 +30%), 공격 범위 +30%, 공격 속도 +15%, 치명타 확률 +10%p */
export const WEAPON_HIDDEN: readonly HiddenCode[] = ['d', 'r', 's', 'c'];
/** 상의·하의: 최대 체력(이 방어구 체력 +30%), 받는 피해 -15%, 이동 속도 +10% */
export const ARMOR_HIDDEN: readonly HiddenCode[] = ['h', 'g', 'm'];

/** 옵션별 수치(%, 치명타는 %p) */
export const HIDDEN_VALUE: Readonly<Record<HiddenCode, number>> = { d: 30, r: 30, s: 15, c: 10, h: 30, g: 15, m: 10 };
/** 치명타 피해 배율(치명타 옵션이 화력 상한에 주는 몫 = 1 + 확률 x (배율 - 1)) */
export const HIDDEN_CRIT_MUL = 1.5;
/** 드롭으로 생길 때 붙을 확률(만분율): 2% */
export const HIDDEN_CHANCE_BP = 200;

type GearLike = Pick<EquipmentDef, 'category' | 'rarity'>;

/** 히든 옵션이 붙을 수 있는 장비의 옵션 목록(없으면 빈 배열): 유니크·레전더리의 무기·상의·하의 */
export function hiddenPoolOf(e: GearLike | undefined): readonly HiddenCode[] {
  if (!e || (e.rarity !== 'Unique' && e.rarity !== 'Legendary')) return [];
  if (e.category === 'Weapon') return WEAPON_HIDDEN;
  if (e.category === 'Top' || e.category === 'Bottom') return ARMOR_HIDDEN;
  return [];
}

/** 키의 히든 옵션이 그 장비에 맞는지(없으면 true) */
export function hiddenValidFor(eco: EconomyData, itemKey: string): boolean {
  const p = parseItemKey(itemKey);
  if (!p) return false;
  if (!p.hidden) return true;
  return (hiddenPoolOf(eco.shop.equipment.get(p.base)) as readonly string[]).includes(p.hidden);
}

/** 승급·계승 결과 장비로 옵션을 옮긴다. 결과 장비가 그 옵션을 가질 수 없으면 버린다(같은 부위라 실제로는 일어나지 않는다) */
export function carryHidden(eco: EconomyData, hidden: string | undefined, toBase: string): string {
  if (!hidden) return '';
  return (hiddenPoolOf(eco.shop.equipment.get(toBase)) as readonly string[]).includes(hidden) ? hidden : '';
}

/** 키의 히든 옵션 글자('' = 없음) */
export function hiddenOf(itemKey: string): HiddenCode | '' {
  return (parseItemKey(itemKey)?.hidden ?? '') as HiddenCode | '';
}

// 히든 옵션 굴림은 별도 난수 흐름을 쓴다. 기존 드롭·카드 굴림의 난수 순서(테스트가 주입하는 순서 포함)를 바꾸지 않기 위해서다.
// jest(NODE_ENV=test)의 기본값은 "절대 안 붙음"이다: 2% 우연으로 기존 테스트의 기대 키가 흔들리지 않게 하고,
// 히든 옵션 테스트는 setHiddenRng로 결과를 정한다.
const NEVER: Rng = { int: (_min, max) => max - 1, unit: () => 1 };
const defaultHiddenRng = (): Rng => (process.env.NODE_ENV === 'test' ? NEVER : cryptoRng);
let hiddenRng: Rng = defaultHiddenRng();

export function getHiddenRng(): Rng {
  return hiddenRng;
}

/** 테스트 전용. null이면 crypto 난수로 되돌린다 */
export function setHiddenRng(r: Rng | null): void {
  if (process.env.NODE_ENV === 'production') throw new Error('운영에서는 난수를 바꿀 수 없습니다');
  hiddenRng = r ?? defaultHiddenRng();
}

/**
 * 드롭으로 새로 생기는 장비 키에 히든 옵션을 굴린다. 대상이 아니거나(등급·부위), 이미 옵션이 있거나, 장비가 아니면 그대로.
 * 2%에 걸리면 그 부위 옵션 중 하나를 같은 확률로 고른다.
 */
export function rollHidden(eco: EconomyData, itemKey: string, rng: Rng = hiddenRng): string {
  const p = parseItemKey(itemKey);
  if (!p || p.hidden) return itemKey;
  const pool = hiddenPoolOf(eco.shop.equipment.get(p.base));
  if (pool.length === 0) return itemKey;
  if (rng.int(0, 10_000) >= HIDDEN_CHANCE_BP) return itemKey;
  const code = pool[rng.int(0, pool.length)] as HiddenCode;
  return keyAt(p.base, p.level, p.inherit, code);
}

/** 카드 목록(던전·레이드)의 장비 카드마다 rollHidden. 새 배열을 돌려준다 */
export function rollHiddenCards<T extends { item_key: string }>(eco: EconomyData, cards: T[]): T[] {
  return cards.map((c) => (c.item_key === 'gold' ? c : { ...c, item_key: rollHidden(eco, c.item_key) }));
}

/** 착용 장비의 화력 상한 배율: 공격 속도 x1.15, 치명타 x(1 + 0.10 x 0.5). 피해 옵션은 gearAttack이 무기 공격에 곱한다 */
export function hiddenAttackMul(wornKeys: readonly string[]): number {
  let mul = 1;
  for (const k of wornKeys) {
    const h = hiddenOf(k);
    if (h === 's') mul *= 1 + HIDDEN_VALUE.s / 100;
    else if (h === 'c') mul *= 1 + (HIDDEN_VALUE.c / 100) * (HIDDEN_CRIT_MUL - 1);
  }
  return mul;
}
