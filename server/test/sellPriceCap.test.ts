// 강화 판매가 가산 상한: 모든 장비·강화 수치에서 늘어난 판매가가 성공 경로 강화 골드 합의 비율 이하
import { getConfig } from '../src/config/env';
import { getGameData, initGameData } from '../src/gamedata/loader';
import { sellPriceOf } from '../src/domains/shop/sellPrice';

beforeAll(() => initGameData(getConfig().gameDataDir));

describe('강화 판매가 상한', () => {
  it('모든 단계x등급x강화 0~최대에서 판매가 증가분 <= 비율 x 누적 강화 골드, 판매가는 줄지 않는다', () => {
    const eco = getGameData().economy;
    const ratio = eco.shop.enhancedSellGoldRatio;
    expect(ratio).toBeLessThanOrEqual(0.5);
    let checked = 0;
    for (const [id, equip] of eco.shop.equipment) {
      const levels = eco.enhance.steps.get(id);
      if (!levels) continue;
      let spent = 0;
      let prev = sellPriceOf(id);
      expect(prev).toBe(equip.sellPrice);
      for (let l = 1; l <= eco.enhance.maxEnhance; l++) {
        spent += levels[l - 1]!.gold;
        const price = sellPriceOf(`${id}+${l}`);
        expect(price - equip.sellPrice).toBeLessThanOrEqual(ratio * spent);
        expect(price).toBeGreaterThanOrEqual(prev);
        prev = price;
        checked++;
      }
    }
    expect(checked).toBeGreaterThan(1000);
  });
});
