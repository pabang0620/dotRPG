import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 경험치·카드·횟수·시간은 받지 않는다(strict) */
export const sweepRunBody = z.strictObject({
  request_id: requestId,
  dungeon_id: z.string().min(1).max(40),
  difficulty: z.number().int().min(0).max(3),
});
export const sweepBuyBody = z.strictObject({
  request_id: requestId,
  count: z.number().int().min(1).max(7),
});
export const sweepClaimBody = z.strictObject({ request_id: requestId });

export type SweepRunBody = z.infer<typeof sweepRunBody>;
export type SweepBuyBody = z.infer<typeof sweepBuyBody>;
export type SweepClaimBody = z.infer<typeof sweepClaimBody>;
