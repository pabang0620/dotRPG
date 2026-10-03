import { z } from 'zod';
import { itemIdSchema, itemKeySchema, requestId } from '../economy/economyValidation';

export const useBody = z.strictObject({ request_id: requestId, item_id: itemIdSchema });

export const storageBody = z.strictObject({
  request_id: requestId,
  moves: z
    .array(
      z.strictObject({
        item_key: itemKeySchema,
        to: z.enum(['storage', 'bag']),
        count: z.union([z.number().int().min(1).max(9999), z.literal('all')]),
      }),
    )
    .min(1)
    .max(60),
});

export const equipBody = z.strictObject({
  request_id: requestId,
  item_key: itemKeySchema,
  slot: z.number().int().min(2).max(3).optional(),
});
export const unequipBody = z.strictObject({ request_id: requestId, slot: z.number().int().min(0).max(5) });

export type UseBody = z.infer<typeof useBody>;
export type StorageBody = z.infer<typeof storageBody>;
export type EquipBody = z.infer<typeof equipBody>;
export type UnequipBody = z.infer<typeof unequipBody>;
