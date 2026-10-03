import { z } from 'zod';

const cursor = z.string().regex(/^\d{1,18}$/).optional();

export const anomalyQuery = z.object({
  account: z.uuid().optional(),
  kind: z.string().regex(/^[a-z_]{1,30}$/).optional(),
  min_severity: z.coerce.number().int().min(1).max(3).optional(),
  since: z.iso.datetime().optional(),
  before: cursor,
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export const flagQuery = z.object({
  account: z.uuid().optional(),
  kind: z.string().regex(/^[a-z_]{1,30}$/).optional(),
  since: z.iso.datetime().optional(),
  before: cursor,
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export const emptyQuery = z.object({});

export type AnomalyQueryT = z.infer<typeof anomalyQuery>;
export type FlagQueryT = z.infer<typeof flagQuery>;
