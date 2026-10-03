import { z } from 'zod';

export const emptyQuery = z.object({});
export const jobParams = z.object({ name: z.string().regex(/^[a-z0-9-]{1,40}$/) });
export const runJobBody = z.strictObject({ request_id: z.uuid(), full: z.boolean().optional() });
export type RunJobBody = z.infer<typeof runJobBody>;
