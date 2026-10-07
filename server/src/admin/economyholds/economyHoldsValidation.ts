import { z } from 'zod';

export const uuidParams = z.object({ uuid: z.uuid() });

export const holdListQuery = z.object({
  state: z.enum(['shadow', 'active', 'released', 'clawed_back']).optional(),
  kind: z.enum(['velocity', 'auction', 'linked', 'manual', 'payment']).optional(),
  q: z.string().min(1).max(40).optional(),
  cursor: z.string().max(40).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export const releaseBody = z.strictObject({
  request_id: z.uuid(),
  note: z.string().min(1).max(500),
  release_linked: z.boolean().optional(),
});

/** 금액은 받지 않는다(strict). 서버가 정지 근거 구간의 원장에서 계산한다 */
export const clawbackBody = z.strictObject({
  request_id: z.uuid(),
  note: z.string().min(1).max(500),
  gold: z.boolean(),
  items: z.boolean(),
  void_mail: z.boolean(),
});

export const manualHoldBody = z
  .strictObject({
    request_id: z.uuid(),
    character_id: z.uuid().optional(),
    account_id: z.uuid().optional(),
    note: z.string().min(1).max(500),
  })
  .refine((b) => (b.character_id === undefined) !== (b.account_id === undefined), { message: 'character_id 와 account_id 중 정확히 하나만 보내야 합니다', path: ['character_id'] });

export const linksQuery = z.object({ full_ip: z.enum(['true', 'false']).optional() });

export const tradeFlagsQuery = z.object({
  flag: z.enum(['CEILING_PRICE', 'NEW_BUYER', 'SAME_DEVICE', 'SAME_STEAM', 'SAME_IP', 'PAIR_REPEAT']).optional(),
  since: z.iso.datetime().optional(),
  min_price: z.coerce.number().int().min(0).optional(),
  cursor: z.string().max(40).optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export type HoldListQuery = z.infer<typeof holdListQuery>;
export type ReleaseBody = z.infer<typeof releaseBody>;
export type ClawbackBody = z.infer<typeof clawbackBody>;
export type ManualHoldBody = z.infer<typeof manualHoldBody>;
export type LinksQuery = z.infer<typeof linksQuery>;
export type TradeFlagsQuery = z.infer<typeof tradeFlagsQuery>;
