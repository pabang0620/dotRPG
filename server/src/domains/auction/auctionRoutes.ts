import { Router } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as c from './auctionController';
import {
  bidBody,
  buyoutBody,
  cancelQuery,
  listBody,
  listingParams,
  pricesParams,
  pricesQuery,
  searchQuery,
} from './auctionValidation';

export function createAuctionRouter(): Router {
  const r = Router();
  // 검색은 캐릭터를 바꿔 우회하지 못하게 계정당 한도다(phase6_api.md 3.1)
  const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  const searchLimit = rateLimit({ name: 'auction-search', limit: (x) => x.rate.auctionSearchPerSec, windowMs: 1000, key: accountKey });
  const priceLimit = rateLimit({ name: 'auction-price', limit: (x) => x.rate.auctionPricePerSec, windowMs: 1000, key: charKey });
  const sellableLimit = rateLimit({ name: 'auction-sellable', limit: (x) => x.rate.auctionSellablePerSec, windowMs: 1000, key: charKey });
  const listSec = rateLimit({ name: 'auction-list-sec', limit: (x) => x.rate.auctionListPerSec, windowMs: 1000, key: charKey });
  const listMin = rateLimit({ name: 'auction-list-min', limit: (x) => x.rate.auctionListPerMin, windowMs: MINUTE, key: charKey });
  const actionLimit = rateLimit({ name: 'auction-action', limit: (x) => x.rate.auctionActionPerSec, windowMs: 1000, key: charKey });
  const mineLimit = rateLimit({ name: 'auction-mine', limit: (x) => x.rate.auctionMinePerSec, windowMs: 1000, key: charKey });

  r.get('/characters/:uuid/auction/search', searchLimit, validate({ params: charParams, query: searchQuery }), c.search);
  r.get('/characters/:uuid/auction/prices/:item_key', priceLimit, validate({ params: pricesParams, query: pricesQuery }), c.prices);
  r.get('/characters/:uuid/auction/sellable', sellableLimit, validate({ params: charParams }), c.sellable);
  r.post('/characters/:uuid/auction/listings', listSec, listMin, validate({ params: charParams, body: listBody }), c.list);
  r.delete('/characters/:uuid/auction/listings/:listing_id', actionLimit, validate({ params: listingParams, query: cancelQuery }), c.cancel);
  r.post(
    '/characters/:uuid/auction/listings/:listing_id/buyout',
    actionLimit,
    validate({ params: listingParams, body: buyoutBody }),
    c.buyout,
  );
  r.post(
    '/characters/:uuid/auction/listings/:listing_id/bids',
    actionLimit,
    validate({ params: listingParams, body: bidBody }),
    c.bid,
  );
  r.get('/characters/:uuid/auction/mine', mineLimit, validate({ params: charParams }), c.mine);
  return r;
}
