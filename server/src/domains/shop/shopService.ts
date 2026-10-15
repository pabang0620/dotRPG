import { getGameData } from '../../gamedata/loader';
import { assertNoHold } from '../antiabuse/holds';
import { AppError } from '../../utils/AppError';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { sellPriceOf } from './sellPrice';
import type { BuyBody, SellBody } from './shopValidation';
import { parseItemKey } from '../../utils/itemKey';
import { gachaCreditOf, gachaSafeSalePlan } from '../starshop/cashValueService';

export { sellPriceOf } from './sellPrice';

export function buy(accountId: number, characterUuid: string, body: BuyBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/shop/buy',
    requestId,
    payload,
    handler: async (ctx) => {
      const unit = getGameData().economy.shop.stock.get(body.item_id);
      if (unit === undefined) throw new AppError(422, '판매하지 않는 물건입니다.', 'NOT_FOR_SALE');
      // 커먼 장비는 착용 레벨이 된 단계만 판다
      const gear = getGameData().economy.shop.equipment.get(body.item_id);
      if (gear && ctx.level < gear.reqLevel) {
        throw new AppError(422, `레벨 ${gear.reqLevel}부터 살 수 있습니다.`, 'LEVEL_TOO_LOW', { need: gear.reqLevel, have: ctx.level });
      }
      const total = unit * body.count;
      if (ctx.gold < total) {
        throw new AppError(422, '골드가 모자랍니다.', 'NOT_ENOUGH_GOLD', { need: total, have: ctx.gold });
      }
      // 전부 아니면 없음. characters.gold의 CHECK(>= 0)가 마지막 안전장치다
      await ctx.changeGold(-total, 'shop_buy', body.item_id);
      await ctx.addItem('bag', body.item_id, body.count, 'shop_buy', body.item_id);
      return {
        status: 200,
        data: { item_id: body.item_id, count: body.count, unit_price: unit, total, delta: ctx.delta() },
      };
    },
  });
}

export function sell(accountId: number, characterUuid: string, body: SellBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/shop/sell',
    requestId,
    payload,
    handler: async (ctx) => {
      // 9단계 12.5: 경제 정지 중에는 팔 수 없다
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      const unit = sellPriceOf(body.item_key);
      if (unit <= 0) throw new AppError(422, '팔 수 없는 물건입니다.', 'NOT_SELLABLE');
      // 대상은 가방 스택뿐이다(착용·창고는 못 판다)
      const have = await ctx.stackCount('bag', body.item_key);
      if (have < body.count) {
        throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: body.count, have });
      }
      // 0044: 뽑기로 얻은 장비는 골드로 팔 수 없다(유료 -> 골드 -> 거래 통로 차단). 별빛 교환소에서 분해한다.
      // 뽑기 장비는 계정 귀속 행이라, 판매는 귀속 없음 -> 캐릭터 귀속 -> (뽑기 몫을 넘는) 계정 귀속 순서로만 뺀다.
      // 귀속 순서대로 빼면 뽑기 사본이 먼저 팔려 골드를 받고, 남은 드롭 사본을 뽑기 몫으로 분해하는 우회가 생긴다.
      const parsed = parseItemKey(body.item_key);
      const total = unit * body.count;
      if (parsed && parsed.base.startsWith('eq_') && (await gachaCreditOf(ctx.client, ctx.char.id, parsed.base)) > 0) {
        const plan = await gachaSafeSalePlan(ctx.client, ctx.char.id, parsed.base, body.item_key);
        const sellable = plan.none + plan.character + plan.account;
        if (sellable < body.count) {
          throw new AppError(422, '뽑기로 얻은 장비는 상점에 팔 수 없습니다. 별빛 교환소에서 분해해 별의 결정으로 바꿀 수 있습니다.', 'GACHA_GEAR_NOT_SELLABLE', { sellable });
        }
        let left = body.count;
        for (const bind of ['none', 'character', 'account'] as const) {
          const take = Math.min(left, plan[bind]);
          if (take <= 0) continue;
          if ((await ctx.removeItem('bag', body.item_key, take, 'shop_sell', body.item_key, bind)) === null) {
            throw new AppError(409, '수량이 바뀌었습니다. 다시 시도해 주세요.', 'NOT_ENOUGH_ITEMS');
          }
          left -= take;
        }
      } else {
        await ctx.removeItem('bag', body.item_key, body.count, 'shop_sell', body.item_key);
      }
      await ctx.changeGold(total, 'shop_sell', body.item_key);
      return {
        status: 200,
        data: { item_key: body.item_key, count: body.count, unit_price: unit, total, delta: ctx.delta() },
      };
    },
  });
}
