import { z } from 'zod';
import { itemKeySchema, requestId } from '../economy/economyValidation';

/** 받지 않는 값: 확률, 비용, 주사위 값, 결과. target은 worn_slot과 bag_key 중 정확히 하나 */
const target = z.union([
  z.strictObject({ worn_slot: z.number().int().min(0).max(5) }),
  z.strictObject({ bag_key: itemKeySchema }),
]);

export const enhanceBody = z.strictObject({ request_id: requestId, target });

export type EnhanceBody = z.infer<typeof enhanceBody>;
