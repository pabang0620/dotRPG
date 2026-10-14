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

/** PL9(0038): 부적절한 캐릭터 이름. note는 플레이어에게 보이는 사유 */
export const forceRenameBody = z.strictObject({ request_id: requestId, note: z.string().min(1).max(200) });

/** 0039 게임시간 선택제: start/end 둘 다 "HH:MM"(KST) 또는 둘 다 null(해제). 같은 시각은 구간이 아니다 */
const hm = z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/, 'HH:MM 형식입니다');
export const playWindowBody = z
  .strictObject({ request_id: requestId, start: hm.nullable(), end: hm.nullable(), note: z.string().min(1).max(200) })
  .refine((b) => (b.start === null) === (b.end === null), { message: 'start와 end는 함께 지정하거나 함께 null이어야 합니다', path: ['end'] })
  .refine((b) => b.start === null || b.start !== b.end, { message: 'start와 end가 같을 수 없습니다', path: ['end'] });

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
export type PlayWindowBody = z.infer<typeof playWindowBody>;
export type ForceRenameBody = z.infer<typeof forceRenameBody>;
