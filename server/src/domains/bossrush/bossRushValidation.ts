import { z } from 'zod';
import { charParams, requestId } from '../economy/economyValidation';

export const kindParams = charParams.extend({ kind: z.enum(['world', 'guild']) });
export const attemptParams = kindParams.extend({ attemptId: z.uuid() });
export const startBody = z.strictObject({ request_id: requestId, party_member_uuids: z.array(z.uuid()).max(3).optional() });
/** 피해는 클라이언트 보고(서버가 화력 상한으로 자른다). 상한은 DB BIGINT 안전 범위 */
export const finishBody = z.strictObject({
  request_id: requestId,
  damage_dealt: z.number().int().min(0).max(1_000_000_000),
  hits_landed: z.number().int().min(0).max(1_000_000),
  duration_s: z.number().min(0).max(600),
});
export type StartBody = z.infer<typeof startBody>;
export type FinishBody = z.infer<typeof finishBody>;
