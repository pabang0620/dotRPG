import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 코인 수·레벨·시간은 받지 않는다(strict) */
export const reviveBody = z.strictObject({
  request_id: requestId,
  context: z.enum(['field', 'dungeon']),
  map_id: z.string().min(1).max(60).optional(),
});
export type ReviveBody = z.infer<typeof reviveBody>;
