import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 보상은 요청에 없다(quest_index.json의 reward가 정한다) */
export const claimBody = z.strictObject({ request_id: requestId });
export const claimParams = z.object({
  uuid: z.uuid(),
  quest_id: z.string().min(1).max(64).regex(/^[A-Za-z0-9_]+$/),
});

export type ClaimBody = z.infer<typeof claimBody>;
export type ClaimParams = z.infer<typeof claimParams>;
