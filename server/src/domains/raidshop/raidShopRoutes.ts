import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './raidShopController';
import { buyBody } from './raidShopValidation';

export function createRaidShopRouter(): Router {
  const r = Router();
  r.get(
    '/characters/:uuid/raid-shop',
    rateLimit({ name: 'raid-shop-list', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams }),
    controller.list,
  );
  r.post(
    '/characters/:uuid/raid-shop/buy',
    rateLimit({ name: 'raid-shop-buy', limit: (c) => c.rate.shopPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: buyBody }),
    controller.buy,
  );
  return r;
}
