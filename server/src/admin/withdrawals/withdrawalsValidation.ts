import { z } from 'zod';

export const uuidParams = z.object({ uuid: z.uuid() });

export const listQuery = z.object({
  state: z.enum(['requested', 'cancelled', 'completed']).optional(),
  deferred: z.enum(['true', 'false']).optional(),
  due_before: z.iso.datetime().optional(),
  cursor: z.uuid().optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

const note = z.string().min(1).max(200);

/** 재인증·확인 문구 없음(운영자 책임, 메모 필수). cancel_allowed:false 는 owner 만 */
export const startBody = z.strictObject({ request_id: z.uuid(), note, cancel_allowed: z.boolean().optional() });
export const cancelBody = z.strictObject({ request_id: z.uuid(), note });
export const anonymizeNowBody = z.strictObject({ request_id: z.uuid(), note, override_deferral: z.boolean().optional() });
export const holdBody = z.strictObject({ request_id: z.uuid(), on: z.boolean(), note });
export const releaseBody = z.strictObject({ request_id: z.uuid(), note });
/** 입력 원문은 감사 params·로그에 남기지 않는다: 서버가 HMAC 해시로 바꿔 조회한다 */
export const tombstoneQuery = z.object({ steam: z.string().regex(/^\d{17}$/, 'steam_id64 17자리 숫자입니다') });

export type ListQuery = z.infer<typeof listQuery>;
export type StartBody = z.infer<typeof startBody>;
export type CancelBody = z.infer<typeof cancelBody>;
export type AnonymizeNowBody = z.infer<typeof anonymizeNowBody>;
export type HoldBody = z.infer<typeof holdBody>;
export type ReleaseBody = z.infer<typeof releaseBody>;
export type TombstoneQuery = z.infer<typeof tombstoneQuery>;
