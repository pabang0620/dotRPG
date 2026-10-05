import { z } from 'zod';
import { itemKeySchema, requestId } from '../economy/economyValidation';

/** 받지 않는 값: 결과 키, 비용. target은 worn_slot과 bag_key 중 정확히 하나 */
const target = z.union([
  z.strictObject({ worn_slot: z.number().int().min(0).max(5) }),
  z.strictObject({ bag_key: itemKeySchema }),
]);

export const promoteBody = z.strictObject({ request_id: requestId, target });

export type PromoteBody = z.infer<typeof promoteBody>;
