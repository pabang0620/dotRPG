import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './raidController';

export function createRaidRouter(): Router {
  const r = Router();
  r.get(
    '/characters/:uuid/raids',
    rateLimit({ name: 'raids-list', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams }),
    controller.list,
  );
  return r;
}
