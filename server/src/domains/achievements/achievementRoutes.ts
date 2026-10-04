import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './achievementController';
import { charParams, titleBody } from './achievementValidation';

const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

export function createAchievementRouter(): Router {
  const r = Router();
  const base = '/characters/:uuid';
  const limit = rateLimit({ name: 'achievements', limit: () => 4, windowMs: 1000, key: accountKey });
  r.get(`${base}/achievements`, versionCheck({ data: false }), requireAuth, limit, validate({ params: charParams }), controller.list);
  r.post(`${base}/title`, versionCheck({ data: false }), requireAuth, limit, validate({ params: charParams, body: titleBody }), controller.equip);
  return r;
}
