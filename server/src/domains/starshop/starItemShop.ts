// 별조각 상품(0056): 별조각으로 가방 아이템(부활 코인 등)을 바로 산다. 구매 한도 없음(결제 제한 금지 원칙).
// 락 순서: 캐릭터(runEconomy) -> 별조각 지갑. 원장: star_ledger item_buy(ref = 상품 id) + item_ledger star_item_buy(같은 request_id).
import { AppError } from '../../utils/AppError';
import { assertNoHold } from '../antiabuse/holds';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { STAR_ITEM_GOODS, STAR_ITEM_GOOD_BY_ID } from './starshopDefs';
import * as wallets from './starWallet';

/** 교환소 조회 응답에 붙이는 별조각 상품 목록 */
export function starItemGoodsView() {
  return STAR_ITEM_GOODS.map((g) => ({ id: g.id, name: g.name, price: g.price, item_id: g.itemId, count: g.count }));
}

export interface StarItemBuyBody {
  request_id: string;
  good_id: string;
}

/** POST /characters/:uuid/starshop/item-buy: 별조각을 내고 상품 아이템을 가방(계정 귀속)으로 받는다 */
export function buyStarItem(accountId: number, characterUuid: string, body: StarItemBuyBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/item-buy',
    requestId,
    payload,
    handler: async (ctx) => {
      const good = STAR_ITEM_GOOD_BY_ID.get(body.good_id);
      if (!good) throw new AppError(404, '없는 상품입니다.', 'GOOD_UNKNOWN');
      await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
      // debit 이 지갑을 잠그고 부채·잔액을 확인한다(모자라면 422 NOT_ENOUGH_STARS, 부채면 STAR_DEBT)
      const w = await wallets.debit(ctx.client, { accountId, reason: 'item_buy', price: good.price, ref: good.id, requestId });
      await ctx.addItem('bag', good.itemId, good.count, 'star_item_buy', good.id, 'account');
      return { status: 200, data: { good_id: good.id, item_id: good.itemId, count: good.count, price: good.price, balance: w.balance, delta: ctx.delta() } };
    },
  });
}
