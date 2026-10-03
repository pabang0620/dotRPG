import { z } from 'zod';
import { duration, reasonCode, sanctionKind } from '../sanctions/sanctionsValidation';

export const uuidParams = z.object({ uuid: z.uuid() });

export const listQuery = z.object({
  state: z.enum(['open', 'reviewing', 'open,reviewing']).default('open,reviewing'),
  reason: reasonCode.optional(),
});

export const takeBody = z.strictObject({ request_id: z.uuid() });

export const resolveBody = z
  .strictObject({
    request_id: z.uuid(),
    decision: z.enum(['dismiss', 'sanction']),
    note: z.string().min(1).max(500),
    sanction: z.strictObject({ kind: sanctionKind, reason_code: reasonCode, duration: duration.optional() }).optional(),
    close_similar: z.boolean().default(true),
  })
  .refine((b) => (b.decision === 'sanction') === (b.sanction !== undefined), { message: 'decision이 sanction일 때만 sanction을 보냅니다', path: ['sanction'] });

export type ListQuery = z.infer<typeof listQuery>;
export type ResolveBody = z.infer<typeof resolveBody>;
