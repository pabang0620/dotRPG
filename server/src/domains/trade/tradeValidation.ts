import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

const GOLD_MAX = 2_147_483_647;

export const tradeParams = z.object({ uuid: z.uuid(), trade_id: z.uuid() });

export const createBody = z.strictObject({ request_id: requestId, target_name: z.string().trim().min(1).max(16) });
export const offerBody = z.strictObject({
  request_id: requestId,
  version: z.number().int().min(0),
  gold: z.number().int().min(0).max(GOLD_MAX),
  items: z.array(z.strictObject({ item_uuid: z.uuid(), count: z.number().int().min(1).max(GOLD_MAX) })).max(8),
});
/** accept, decline, cancel: 상태로 멱등하므로 request_id는 선택 */
export const plainBody = z.strictObject({ request_id: requestId.optional() });
export const lockBody = z.strictObject({ request_id: requestId.optional(), version: z.number().int().min(0) });
export const confirmBody = z.strictObject({ request_id: requestId, version: z.number().int().min(0) });

export type CreateBody = z.infer<typeof createBody>;
export type OfferBody = z.infer<typeof offerBody>;
export type PlainBody = z.infer<typeof plainBody>;
export type LockBody = z.infer<typeof lockBody>;
export type ConfirmBody = z.infer<typeof confirmBody>;
export type TradeParams = z.infer<typeof tradeParams>;
