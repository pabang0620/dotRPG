import { z } from 'zod';

export const sanctionKind = z.enum(['warning', 'chat_mute', 'ban']);
export const reasonCode = z.enum(['abuse', 'spam', 'scam_ad', 'cheat', 'other']);
export const duration = z.enum(['1h', '1d', '7d', '30d', 'permanent']);

export const createSanctionBody = z.strictObject({
  request_id: z.uuid(),
  kind: sanctionKind,
  reason_code: reasonCode,
  duration: duration.optional(),
  report_id: z.uuid().optional(),
  note: z.string().min(1).max(500),
});

export const revokeBody = z.strictObject({ request_id: z.uuid(), note: z.string().min(1).max(500) });
export const uuidParams = z.object({ uuid: z.uuid() });

export type CreateSanctionBody = z.infer<typeof createSanctionBody>;
export type RevokeBody = z.infer<typeof revokeBody>;
export type SanctionInput = Pick<CreateSanctionBody, 'kind' | 'reason_code' | 'duration' | 'report_id' | 'note'>;
