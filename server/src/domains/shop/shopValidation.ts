import { z } from 'zod';
import { itemIdSchema, itemKeySchema, requestId } from '../economy/economyValidation';

/** 가격·총액은 받지 않는다(shop.json이 정한다) */
export const buyBody = z.strictObject({
  request_id: requestId,
  item_id: itemIdSchema,
  count: z.number().int().min(1).max(999),
});
export const sellBody = z.strictObject({
  request_id: requestId,
  item_key: itemKeySchema,
  count: z.number().int().min(1).max(9999),
});

/** 교환: 교환 종류만 받는다(비용·결과는 서버가 정한다) */
export const exchangeBody = z.strictObject({ request_id: requestId, exchange_id: z.literal('akab_seal') });

export type ExchangeBody = z.infer<typeof exchangeBody>;
export type BuyBody = z.infer<typeof buyBody>;
export type SellBody = z.infer<typeof sellBody>;
