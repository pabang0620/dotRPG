import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './promoteController';
import { promoteBody } from './promoteValidation';

export function createPromoteRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/promote',
    rateLimit({ name: 'promote', limit: (c) => c.rate.enhancePerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: promoteBody }),
    controller.promote,
  );
  return r;
}
