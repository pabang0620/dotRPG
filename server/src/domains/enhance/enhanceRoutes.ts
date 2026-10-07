import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './enhanceController';
import { enhanceBody, ticketBody } from './enhanceValidation';

export function createEnhanceRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/enhance',
    rateLimit({ name: 'enhance', limit: (c) => c.rate.enhancePerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: enhanceBody }),
    controller.enhance,
  );
  r.post(
    '/characters/:uuid/enhance/ticket',
    rateLimit({ name: 'enhance', limit: (c) => c.rate.enhancePerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: ticketBody }),
    controller.ticket,
  );
  return r;
}
