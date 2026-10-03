import { z } from 'zod';
import { ITEM_KEY_RE } from '../../utils/itemKey';

export const dailyQuery = z.object({ day: z.iso.date().optional() });

export const grantBody = z
  .strictObject({
    request_id: z.uuid(),
    character_id: z.uuid(),
    system_code: z.enum(['compensation', 'event', 'refund', 'notice']),
    gold: z.number().int().min(1).optional(),
    item: z.strictObject({ item_key: z.string().regex(ITEM_KEY_RE), count: z.number().int().min(1) }).optional(),
    memo: z.string().min(1).max(200),
  })
  .refine((b) => b.gold !== undefined || b.item !== undefined, { message: 'gold 또는 item 중 하나는 필요합니다', path: ['gold'] });

export const grantListQuery = z.object({
  character: z.uuid().optional(),
  before: z.string().regex(/^\d{1,18}$/).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export type GrantBody = z.infer<typeof grantBody>;
export type GrantListQuery = z.infer<typeof grantListQuery>;
