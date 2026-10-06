import { z } from 'zod';

export const uuidParams = z.object({ uuid: z.uuid() });

export const ORDER_STATES = ['pending_init', 'created', 'authorized', 'finalized', 'granted', 'failed', 'expired', 'refunded', 'chargeback'] as const;

export const orderListQuery = z.object({
  state: z.enum(ORDER_STATES).optional(),
  account: z.uuid().optional(),
  from: z.iso.datetime().optional(),
  to: z.iso.datetime().optional(),
  needs_review: z.enum(['true', 'false']).optional(),
  cursor: z.string().max(40).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});
export type OrderListQuery = z.infer<typeof orderListQuery>;

/** PA3 */
export const recheckBody = z.strictObject({ request_id: z.uuid() });
export type RecheckBody = z.infer<typeof recheckBody>;

/** PA5 */
export const blockBody = z.strictObject({
  request_id: z.uuid(),
  note: z.string().trim().min(1).max(500),
  reason: z.enum(['manual', 'fraud_suspect']),
});
export type BlockBody = z.infer<typeof blockBody>;

/** PA6 */
export const unblockBody = z.strictObject({ request_id: z.uuid(), note: z.string().trim().min(1).max(500) });
export type UnblockBody = z.infer<typeof unblockBody>;

export const flagListQuery = z.object({
  state: z.enum(['open', 'confirmed', 'dismissed']).default('open'),
  account: z.uuid().optional(),
  cursor: z.string().max(40).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});
export type FlagListQuery = z.infer<typeof flagListQuery>;

/** PA8 */
export const resolveBody = z.strictObject({
  request_id: z.uuid(),
  resolution: z.enum(['confirmed', 'dismissed']),
  note: z.string().trim().min(1).max(500),
});
export type ResolveBody = z.infer<typeof resolveBody>;

/** PA9: 부채 탕감에는 금액을 받지 않는다(전액만). 지급에는 stars 필수 */
export const grantCreateBody = z
  .strictObject({
    request_id: z.uuid(),
    kind: z.enum(['grant', 'debt_forgive']),
    account_id: z.uuid(),
    stars: z.number().int().min(1).optional(),
    related_order_id: z.uuid().optional(),
    memo: z.string().trim().min(1).max(200),
  })
  .superRefine((b, ctx) => {
    if (b.kind === 'grant' && b.stars === undefined) ctx.addIssue({ code: 'custom', path: ['stars'], message: 'grant 에는 stars 가 필요합니다' });
    if (b.kind === 'debt_forgive' && b.stars !== undefined) ctx.addIssue({ code: 'custom', path: ['stars'], message: '부채 탕감은 전액만이라 금액을 받지 않습니다' });
  });
export type GrantCreateBody = z.infer<typeof grantCreateBody>;

export const grantListQuery = z.object({
  state: z.enum(['pending', 'applied', 'cancelled', 'expired']).optional(),
  account: z.uuid().optional(),
  cursor: z.string().max(40).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});
export type GrantListQuery = z.infer<typeof grantListQuery>;

/** PA11, PA12 */
export const grantActionBody = z.strictObject({ request_id: z.uuid() });
export type GrantActionBody = z.infer<typeof grantActionBody>;

/** PA13 */
export const revokeBody = z.strictObject({
  request_id: z.uuid(),
  mode: z.enum(['preview', 'apply']),
  include: z.strictObject({ cosmetics: z.boolean(), gear: z.boolean(), gauge: z.boolean() }),
  preview_hash: z.string().regex(/^[0-9a-f]{64}$/).optional(),
  note: z.string().trim().min(1).max(500),
});
export type RevokeBody = z.infer<typeof revokeBody>;
