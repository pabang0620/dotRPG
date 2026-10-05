import { z } from 'zod';
import { itemIdSchema, requestId } from '../economy/economyValidation';

/** 가격은 받지 않는다(starshopDefs가 정한다). count 10은 10+1회 묶음 */
export const pullBody = z.strictObject({
  request_id: requestId,
  count: z.union([z.literal(1), z.literal(10)]),
  banner: z.enum(['aura', 'weapon', 'armor', 'accessory']).optional(),
});
export const exchangeBody = z.strictObject({
  request_id: requestId,
  item_id: itemIdSchema,
});

export type PullBody = z.infer<typeof pullBody>;
export type ExchangeBody = z.infer<typeof exchangeBody>;
