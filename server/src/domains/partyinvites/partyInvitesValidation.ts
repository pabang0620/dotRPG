import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 대상은 캐릭터 id(채팅·친구 목록) 또는 캐릭터 이름(파티 창에서 직접 입력) 중 하나 */
export const inviteBodyIn = z
  .strictObject({ request_id: requestId, target: z.uuid().optional(), target_name: z.string().trim().min(1).max(16).optional() })
  .refine((b) => (b.target === undefined) !== (b.target_name === undefined), 'target 또는 target_name 중 하나만 보냅니다');
export const respondBody = z.strictObject({ request_id: requestId, accept: z.boolean() });
export const charParams = z.object({ uuid: z.uuid() });
export const inviteParams = z.object({ uuid: z.uuid(), id: z.uuid() });

export type InviteBodyIn = z.infer<typeof inviteBodyIn>;
export type RespondBody = z.infer<typeof respondBody>;
export type InviteParams = z.infer<typeof inviteParams>;
