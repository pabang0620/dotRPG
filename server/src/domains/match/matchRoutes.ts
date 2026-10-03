import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './matchController';
import { charParams, queueBody, requestOnlyBody } from './matchValidation';

export function createMatchRouter(): Router {
  const r = Router();
  const create = rateLimit({ name: 'match-create', limit: (c) => c.rate.partyCreatePer3Sec, windowMs: 3000, key: charKey });
  r.post('/characters/:uuid/match/queue', create, validate({ params: charParams, body: queueBody }), controller.enqueue);
  r.delete('/characters/:uuid/match/queue', create, validate({ params: charParams }), controller.cancel);
  r.post('/characters/:uuid/match/fill-ai', create, validate({ params: charParams, body: requestOnlyBody }), controller.fillAi);
  return r;
}
