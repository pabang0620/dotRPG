import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './shopController';
import { buyBody, exchangeBody, sellBody } from './shopValidation';

export function createShopRouter(): Router {
  const r = Router();
  const limit = rateLimit({ name: 'shop', limit: (c) => c.rate.shopPerSec, windowMs: 1000, key: charKey });
  r.post('/characters/:uuid/shop/buy', limit, validate({ params: charParams, body: buyBody }), controller.buy);
  r.post('/characters/:uuid/shop/sell', limit, validate({ params: charParams, body: sellBody }), controller.sell);
  r.post('/characters/:uuid/shop/exchange', limit, validate({ params: charParams, body: exchangeBody }), controller.exchange);
  return r;
}
