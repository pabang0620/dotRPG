import { z } from 'zod';

const requestId = z.uuid();

export const findQuery = z.object({ q: z.string().min(1).max(100) });
export const uuidParams = z.object({ uuid: z.uuid() });

export const ledgerQuery = z.object({
  kind: z.enum(['gold', 'item', 'xp']).default('gold'),
  reason: z.string().regex(/^[a-z_]{1,30}$/).optional(),
  request: z.uuid().optional(),
  since: z.iso.datetime().optional(),
  before: z.string().regex(/^[A-Za-z0-9_-]{1,40}$/).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export const noteBody = z.strictObject({
  request_id: requestId,
  kind: z.enum(['note', 'review_ack']).default('note'),
  note: z.string().min(1).max(500),
});

export const requestOnlyBody = z.strictObject({ request_id: requestId });

export const devCreateBody = z.strictObject({
  request_id: requestId,
  login_id: z
    .string()
    .transform((s) => s.toLowerCase())
    .pipe(z.string().regex(/^[a-z0-9_]{4,20}$/, '아이디는 영문 소문자·숫자·_ 4~20자입니다')),
});

export type LedgerQuery = z.infer<typeof ledgerQuery>;
export type NoteBody = z.infer<typeof noteBody>;
export type DevCreateBody = z.infer<typeof devCreateBody>;
