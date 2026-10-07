import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 별조각 수량은 받지 않는다(strict). 단계 표에 없는 level은 서비스가 422 UNKNOWN_TIER로 거절한다 */
export const claimBody = z.strictObject({
  request_id: requestId,
  level: z.number().int().min(1).max(999),
});
export type ClaimBody = z.infer<typeof claimBody>;

/** 가격은 받지 않는다(strict) */
export const passBuyBody = z.strictObject({ request_id: requestId });
export const passClaimBody = z.strictObject({ request_id: requestId, level: z.number().int().min(1).max(999) });
export type PassBuyBody = z.infer<typeof passBuyBody>;
export type PassClaimBody = z.infer<typeof passClaimBody>;
