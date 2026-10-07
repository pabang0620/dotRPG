import { z } from 'zod';

/** 받지 않는 값: 금액, 수량, 통화, 별조각 수, Steam ID, 주문 번호, 시각(strict) */
export const createOrderBody = z.strictObject({
  request_id: z.uuid(),
  product_id: z.string().regex(/^[a-z0-9_]{3,40}$/),
  /** B1이 준 서명 견적 참조 */
  quote_id: z.string().min(20).max(512),
});
export type CreateOrderBody = z.infer<typeof createOrderBody>;

/** 콜백의 승인 여부 등 어떤 값도 받지 않는다 */
export const emptyBody = z.strictObject({});
export const orderParams = z.object({ uuid: z.uuid() });
export const listQuery = z.object({
  limit: z.coerce.number().int().min(1).max(50).default(20),
  cursor: z.string().min(1).max(64).optional(),
});
export type ListQuery = z.infer<typeof listQuery>;
