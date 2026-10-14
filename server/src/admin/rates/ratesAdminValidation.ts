import { z } from 'zod';

export const listQuery = z.object({
  before: z.string().regex(/^\d{1,18}$/).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});
export const idParams = z.object({ id: z.string().regex(/^\d{1,18}$/) });

export type ListQueryT = z.infer<typeof listQuery>;
export type IdParamsT = z.infer<typeof idParams>;
