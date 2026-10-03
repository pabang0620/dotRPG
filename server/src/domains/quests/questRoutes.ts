import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './questController';
import { claimBody, claimParams } from './questValidation';

export function createQuestRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/quests/:quest_id/claim',
    rateLimit({ name: 'quest-claim', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey }),
    validate({ params: claimParams, body: claimBody }),
    controller.claim,
  );
  return r;
}
