import { z } from 'zod';

export const uuidParams = z.object({ uuid: z.uuid() });
export const emptyQuery = z.object({});
export const reviewBody = z.strictObject({ request_id: z.uuid(), note: z.string().min(1).max(500) });
export type ReviewBody = z.infer<typeof reviewBody>;
