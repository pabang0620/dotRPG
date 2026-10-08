// 상점 판매가(ItemPrices.SellPrice). shopService와 경제 집계(incomeMeter)가 같이 쓰므로 순환 import를 피하려고 따로 둔다.
import { getGameData } from '../../gamedata/loader';
import { parseItemKey, roundHalfAway } from '../../utils/itemKey';

/**
 * ItemPrices.SellPrice: 장비는 min(round(기본가 * (1 + 보너스 * 강화단계)), 기본가 + floor(비율 * 그 단계까지 성공 경로 강화 골드 합)) (.5는 올림),
 * 그 밖은 표. 팔 수 없으면 0. 뒤쪽 상한 덕에 강화로 늘어난 판매가가 강화에 쓴 골드의 비율 이하로 묶인다.
 */
export function sellPriceOf(itemKey: string): number {
  const eco = getGameData().economy;
  const p = parseItemKey(itemKey);
  if (!p) return 0;
  const equip = eco.shop.equipment.get(p.base);
  if (equip) {
    if (p.level > eco.enhance.maxEnhance) return 0;
    const plain = roundHalfAway(equip.sellPrice * (1 + eco.shop.enhancedSellBonusPerLevel * p.level));
    const levels = eco.enhance.steps.get(p.base) ?? [];
    let spent = 0;
    for (let i = 0; i < p.level && i < levels.length; i++) spent += levels[i]!.gold;
    return Math.min(plain, equip.sellPrice + Math.floor(eco.shop.enhancedSellGoldRatio * spent));
  }
  if (p.level !== 0) return 0;
  return eco.shop.sellPrices.get(p.base) ?? 0;
}
