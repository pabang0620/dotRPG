import { z } from 'zod';
import { ITEM_KEY_RE } from '../../utils/itemKey';

// 줄바꿈은 허용하고 그 밖의 제어문자는 거절한다
const NO_CTRL_BODY = /^[^\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]*$/;
const NO_CTRL = /^[^\u0000-\u001F\u007F]+$/;
const dt = z.iso.datetime({ offset: true });

/** 대상 조건(설계 7.3): { all: true } 이거나 조건의 조합(AND). 비어 있으면 거절(실수로 전체 발송이 되지 않게 all을 명시) */
export const targetSchema = z
  .strictObject({
    all: z.literal(true).optional(),
    min_account_level: z.number().int().min(1).max(999).optional(),
    max_account_level: z.number().int().min(1).max(999).optional(),
    min_level: z.number().int().min(1).max(999).optional(),
    max_level: z.number().int().min(1).max(999).optional(),
    classes: z.array(z.enum(['warrior', 'mage'])).min(1).max(2).optional(),
    account_created_from: dt.optional(),
    account_created_to: dt.optional(),
    last_login_before: dt.optional(),
    account_ids: z.array(z.uuid()).min(1).max(10_000).optional(),
  })
  .refine((t) => Object.keys(t).length > 0, { message: 'target이 비어 있습니다. 전체 발송이면 { all: true } 로 명시하세요', path: ['all'] })
  .refine((t) => t.all === undefined || Object.keys(t).length === 1, { message: 'all 과 다른 조건을 함께 쓸 수 없습니다', path: ['all'] });

const attachment = z.discriminatedUnion('kind', [
  z.strictObject({ kind: z.literal('gold'), amount: z.number().int().min(1) }),
  z.strictObject({
    kind: z.literal('item'),
    item_key: z.string().regex(ITEM_KEY_RE),
    count: z.number().int().min(1),
    bind: z.enum(['none', 'account', 'character']).optional(),
  }),
  z.strictObject({ kind: z.literal('sweep_ticket'), count: z.number().int().min(1) }),
]);

export const createBody = z.strictObject({
  request_id: z.uuid(),
  title: z.string().trim().min(1).max(40).regex(NO_CTRL, '제목에 제어문자를 쓸 수 없습니다'),
  body: z.string().max(1000).regex(NO_CTRL_BODY, '본문에 줄바꿈 외의 제어문자를 쓸 수 없습니다'),
  category: z.enum(['maintenance', 'apology', 'event', 'attendance', 'other']),
  delivery_unit: z.enum(['account', 'character']).default('account'),
  target: targetSchema,
  mail_days: z.number().int().min(1).max(30),
  starts_at: dt,
  ends_at: dt,
  cap_count: z.number().int().min(1),
  attachments: z.array(attachment).min(1).max(5),
  memo: z.string().min(1).max(200),
});

export const uuidParams = z.object({ uuid: z.uuid() });
const cursor = z.string().regex(/^\d{1,18}$/);
export const listQuery = z.object({
  status: z.enum(['pending', 'active', 'ended', 'cancelled']).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
  before: cursor.optional(),
});
export const deliveriesQuery = z.object({
  state: z.enum(['claimed', 'open', 'expired', 'revoked']).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
  before: cursor.optional(),
});
export const approveBody = z.strictObject({ request_id: z.uuid() });
export const cancelBody = z.strictObject({
  request_id: z.uuid(),
  reason: z.string().min(1).max(200),
  revoke_unclaimed: z.boolean(),
});

export type CreateBody = z.infer<typeof createBody>;
export type ListQuery = z.infer<typeof listQuery>;
export type DeliveriesQuery = z.infer<typeof deliveriesQuery>;
export type ApproveBody = z.infer<typeof approveBody>;
export type CancelBody = z.infer<typeof cancelBody>;
