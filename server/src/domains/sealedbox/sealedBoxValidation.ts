import { z } from 'zod';
import { itemIdSchema, requestId } from '../economy/economyValidation';

/** 가격·확률·결과는 받지 않는다(strict). count는 1 또는 11 */
export const pullBody = z.strictObject({
  request_id: requestId,
  count: z.union([z.literal(1), z.literal(11)]),
  /** 화면에서 본 확률표 버전. STAR_RATES_ACK_REQUIRED 이면 필수 */
  rates_version: z.string().min(1).max(40).optional(),
});
export const openBody = z.strictObject({ request_id: requestId, item_key: itemIdSchema });

export type PullBody = z.infer<typeof pullBody>;
export type OpenBody = z.infer<typeof openBody>;
