// 상점 판매가(ItemPrices.SellPrice). shopService와 경제 집계(incomeMeter)가 같이 쓰므로 순환 import를 피하려고 따로 둔다.
import { getGameData } from '../../gamedata/loader';
import { parseItemKey, roundHalfAway } from '../../utils/itemKey';

/** ItemPrices.SellPrice: 장비는 round(기본가 * (1 + 보너스 * 강화단계)) (.5는 올림), 그 밖은 표. 팔 수 없으면 0 */
export function sellPriceOf(itemKey: string): number {
  const eco = getGameData().economy;
  const p = parseItemKey(itemKey);
  if (!p) return 0;
  const equip = eco.shop.equipment.get(p.base);
  if (equip) {
    if (p.level > eco.enhance.maxEnhance) return 0;
    return roundHalfAway(equip.sellPrice * (1 + eco.shop.enhancedSellBonusPerLevel * p.level));
  }
  if (p.level !== 0) return 0;
  return eco.shop.sellPrices.get(p.base) ?? 0;
}
