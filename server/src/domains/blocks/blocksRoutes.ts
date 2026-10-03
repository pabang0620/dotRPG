import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './blocksController';
import { characterParams, idParams } from './blocksValidation';

const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

export function createBlocksRouter(): Router {
  const r = Router();
  r.use('/blocks', versionCheck({ data: false }), requireAuth);
  const list = rateLimit({ name: 'blocks-list', limit: (c) => c.rate.socialListPerSec, windowMs: 1000, key: accountKey });
  const write = rateLimit({ name: 'blocks-write', limit: (c) => c.rate.socialBlockPerMin, windowMs: MINUTE, key: accountKey });
  r.get('/blocks', list, controller.list);
  r.put('/blocks/:character_id', write, validate({ params: characterParams }), controller.put);
  r.delete('/blocks/:id', write, validate({ params: idParams }), controller.remove);
  return r;
}
