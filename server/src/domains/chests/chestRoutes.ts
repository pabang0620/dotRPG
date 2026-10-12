import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './chestController';
import { chestBody } from './chestValidation';

export function createChestRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/chests/open',
    rateLimit({ name: 'chest', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: chestBody }),
    controller.openChest,
  );
  return r;
}
