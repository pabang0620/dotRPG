import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

/** 수량, 가격, 직업, 등급은 받지 않는다(strictObject가 여분 필드를 400으로 막는다) */
export const buyBody = z.strictObject({
  request_id: requestId,
  product_id: z.string().min(1).max(40).regex(/^[a-z][a-z0-9_]*$/),
});

export type BuyBody = z.infer<typeof buyBody>;
