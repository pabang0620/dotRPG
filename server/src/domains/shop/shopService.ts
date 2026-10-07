import { getGameData } from '../../gamedata/loader';
import { assertNoHold } from '../antiabuse/holds';
import { AppError } from '../../utils/AppError';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { sellPriceOf } from './sellPrice';
import type { BuyBody, SellBody } from './shopValidation';

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
      const total = unit * body.count;
      await ctx.removeItem('bag', body.item_key, body.count, 'shop_sell', body.item_key);
      await ctx.changeGold(total, 'shop_sell', body.item_key);
      return {
        status: 200,
        data: { item_key: body.item_key, count: body.count, unit_price: unit, total, delta: ctx.delta() },
      };
    },
  });
}
