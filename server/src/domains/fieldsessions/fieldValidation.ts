import { z } from 'zod';
import { charParams, requestId } from '../economy/economyValidation';

export { charParams };
export const sessionParams = z.object({ uuid: z.uuid(), id: z.uuid() });

/** 받지 않는 값: 좌석, 호스트 여부, 시각, 인원 */
export const enterBody = z.strictObject({ request_id: requestId, map_id: z.string().min(1).max(32) });
export const requestOnlyBody = z.strictObject({ request_id: requestId });
export const heartbeatBody = z.strictObject({ seen_epoch: z.number().int().min(1), synced: z.boolean() });
export const claimBody = z.strictObject({ request_id: requestId, observed_epoch: z.number().int().min(1) });
export const getQuery = z.object({ after_version: z.coerce.number().int().min(0).optional() });

/** 받지 않는 값: 경험치, 골드, 드롭, 시각 */
export const observeBody = z.strictObject({
  request_id: requestId,
  host_epoch: z.number().int().min(1),
  window_ms: z.number().int().min(1000).max(30000),
  credits: z
    .array(z.strictObject({ seat: z.number().int().min(0).max(3), kills: z.number().int().min(0).max(99), card_mismatch: z.boolean().optional() }))
    .min(1)
    .max(4)
    .refine((cs) => new Set(cs.map((c) => c.seat)).size === cs.length, '좌석이 중복되었습니다'),
});

export type EnterBody = z.infer<typeof enterBody>;
export type RequestOnlyBody = z.infer<typeof requestOnlyBody>;
export type HeartbeatBody = z.infer<typeof heartbeatBody>;
export type ClaimBody = z.infer<typeof claimBody>;
export type ObserveBody = z.infer<typeof observeBody>;
export type SessionParams = z.infer<typeof sessionParams>;
export type GetQuery = z.infer<typeof getQuery>;
