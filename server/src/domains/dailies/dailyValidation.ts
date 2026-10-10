import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 의뢰 id·보상은 받지 않는다(strict). 기간·날짜·칸으로 서버가 의뢰를 정한다. day는 일일이면 게임 날짜, 주간이면 주 시작 날짜 */
export const slotBody = z.strictObject({
  request_id: requestId,
  day: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  slot: z.number().int().min(0).max(9),
  period: z.enum(['day', 'week']).default('day'),
});
export type SlotBody = z.infer<typeof slotBody>;
