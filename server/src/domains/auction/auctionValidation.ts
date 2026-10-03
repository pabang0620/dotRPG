import { z } from 'zod';
import { getConfig } from '../../config/env';
import { itemKeySchema, requestId } from '../economy/economyValidation';

/** 가격·입찰가: 정수, 1 이상, 서버 상한(AUCTION_MAX_PRICE, 클라이언트 Inventory가 int) 이하 */
const price = z
  .number()
  .int()
  .min(1)
  .refine((v) => v <= getConfig().auction.maxPrice, '가격이 허용 상한을 넘었습니다');

export const listBody = z.strictObject({
  request_id: requestId,
  item_key: itemKeySchema,
  count: z.number().int().min(1).max(9999),
  buyout: price,
  start_bid: price.optional(),
  // 허용 기간은 zod가 아니라 서버가 auction.json으로 검사한다(BAD_DURATION)
  hours: z.number().int().min(1).max(10_000),
});

export const buyoutBody = z.strictObject({ request_id: requestId });
export const bidBody = z.strictObject({ request_id: requestId, amount: price });

export const listingParams = z.object({ uuid: z.uuid(), listing_id: z.uuid() });
export const pricesParams = z.object({ uuid: z.uuid(), item_key: itemKeySchema });

const int = (min: number, max?: number) => {
  const base = z.coerce.number().int().min(min);
  return max === undefined ? base : base.max(max);
};

export const searchQuery = z
  .strictObject({
    q: z.string().min(1).max(20).optional(),
    category: z.enum(['all', 'weapon', 'armor', 'accessory', 'material', 'consumable']).default('all'),
    rarity: z
      .string()
      .regex(/^[0-5](,[0-5]){0,5}$/, '등급은 0~5를 콤마로 최대 6개까지 줄 수 있습니다')
      .transform((v) => [...new Set(v.split(',').map(Number))])
      .optional(),
    enh_min: int(0).optional(),
    enh_max: int(0).optional(),
    price_min: int(0).optional(),
    price_max: int(0).optional(),
    class: z.enum(['mine', 'any']).default('any'),
    sort: z.enum(['price_asc', 'price_desc', 'unit_price_asc', 'time_left', 'enhance_desc', 'newest']).default('price_asc'),
    page: int(1).default(1),
    limit: int(1, 20).default(20),
  })
  .refine((v) => v.enh_min === undefined || v.enh_max === undefined || v.enh_min <= v.enh_max, {
    message: 'enh_min 은 enh_max 이하여야 합니다',
    path: ['enh_min'],
  })
  .refine((v) => v.price_min === undefined || v.price_max === undefined || v.price_min <= v.price_max, {
    message: 'price_min 은 price_max 이하여야 합니다',
    path: ['price_min'],
  });

/** 취소는 본문이 없어 request_id를 쿼리로 선택해서 받는다(원장 연결용, 멱등성은 상태 기반) */
export const cancelQuery = z.strictObject({ request_id: requestId.optional() });
export const pricesQuery = z.strictObject({ count: int(1, 9999).default(1) });

export type ListBody = z.infer<typeof listBody>;
export type BuyoutBody = z.infer<typeof buyoutBody>;
export type BidBody = z.infer<typeof bidBody>;
export type ListingParams = z.infer<typeof listingParams>;
export type PricesParams = z.infer<typeof pricesParams>;
export type SearchQuery = z.infer<typeof searchQuery>;
export type CancelQuery = z.infer<typeof cancelQuery>;
export type PricesQuery = z.infer<typeof pricesQuery>;
