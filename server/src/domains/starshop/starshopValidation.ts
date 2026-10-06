import { z } from 'zod';
import { itemIdSchema, requestId } from '../economy/economyValidation';

/** 가격은 받지 않는다(starshopDefs가 정한다). count 10은 10+1회 묶음 */
export const pullBody = z.strictObject({
  request_id: requestId,
  count: z.union([z.literal(1), z.literal(10)]),
  banner: z.enum(['aura', 'skin', 'weapon', 'armor', 'accessory']).optional(),
  /** 장비 뽑기의 레벨 단계(0 = Lv.1 ... 7 = Lv.40). 없으면 내 레벨의 단계 */
  tier: z.number().int().min(0).max(7).optional(),
  /** 클라이언트가 화면에 보여 준 확률표 버전(11단계 11.4). STAR_RATES_ACK_REQUIRED 이면 필수 */
  rates_version: z.string().min(1).max(40).optional(),
});
export const exchangeBody = z.strictObject({
  request_id: requestId,
  item_id: itemIdSchema,
});

export const claimBody = z.strictObject({
  request_id: requestId,
  banner: z.enum(['aura', 'skin']),
  item_id: itemIdSchema,
});
export type ClaimBody = z.infer<typeof claimBody>;

export const synthBody = z.strictObject({
  request_id: requestId,
  rarity: z.enum(['common', 'rare', 'epic']),
  times: z.number().int().min(1).max(20),
});
export const dismantleBody = z.strictObject({
  request_id: requestId,
  item_id: itemIdSchema,
  count: z.number().int().min(1).max(999),
});
export const collectionBody = z.strictObject({
  request_id: requestId,
  set_id: z.string().min(1).max(40),
});
export type SynthBody = z.infer<typeof synthBody>;
export type DismantleBody = z.infer<typeof dismantleBody>;
export type CollectionBody = z.infer<typeof collectionBody>;

export type PullBody = z.infer<typeof pullBody>;
export type ExchangeBody = z.infer<typeof exchangeBody>;
