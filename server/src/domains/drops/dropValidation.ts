import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 드롭 id 이외의 값은 받지 않는다(어떤 아이템·수량인지는 서버 행이 정한다) */
export const claimBody = z.strictObject({
  request_id: requestId,
  drop_ids: z.array(z.uuid()).min(1).max(50),
});

export type ClaimBody = z.infer<typeof claimBody>;
