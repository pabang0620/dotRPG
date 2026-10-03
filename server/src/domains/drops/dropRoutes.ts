import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './dropController';
import { claimBody } from './dropValidation';

export function createDropRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/drops/claim',
    rateLimit({ name: 'drop-claim', limit: (c) => c.rate.claimPerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: claimBody }),
    controller.claim,
  );
  return r;
}
