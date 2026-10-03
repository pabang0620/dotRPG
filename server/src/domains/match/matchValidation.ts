import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

export const queueBody = z.strictObject({
  request_id: requestId,
  dungeon_id: z.string().min(1).max(40).regex(/^[a-z][a-z0-9_]*$/),
  difficulty: z.number().int().min(0).max(3),
});
export const requestOnlyBody = z.strictObject({ request_id: requestId });
export const charParams = z.object({ uuid: z.uuid() });
export type QueueBody = z.infer<typeof queueBody>;
export type RequestOnlyBody = z.infer<typeof requestOnlyBody>;
