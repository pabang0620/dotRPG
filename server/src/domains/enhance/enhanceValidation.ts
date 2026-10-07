import { z } from 'zod';
import { itemIdSchema, itemKeySchema, requestId } from '../economy/economyValidation';

/** 받지 않는 값: 확률, 비용, 주사위 값, 결과. target은 worn_slot과 bag_key 중 정확히 하나 */
const target = z.union([
  z.strictObject({ worn_slot: z.number().int().min(0).max(5) }),
  z.strictObject({ bag_key: itemKeySchema }),
]);

export const enhanceBody = z.strictObject({ request_id: requestId, target });

export type EnhanceBody = z.infer<typeof enhanceBody>;

/** 강화 단계·확률은 받지 않는다: 단계는 강화권 종류(items.json power)가 정한다 */
export const ticketBody = z.strictObject({ request_id: requestId, ticket_key: itemIdSchema, gear_key: itemKeySchema });

export type TicketBody = z.infer<typeof ticketBody>;
