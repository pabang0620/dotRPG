import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './skillStyleController';
import { styleBuyBody } from './skillStyleValidation';

/** 스킬 스타일 3 해금(계정 단위). Docs/PLAN_SKILL_STYLES.md */
export function createSkillStyleRouter(): Router {
  const r = Router();
  r.use('/skill-styles', versionCheck({ data: false }), requireAuth);
  const key = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  r.get('/skill-styles', rateLimit({ name: 'skill-styles-read', limit: () => 30, windowMs: MINUTE, key }), controller.view);
  r.post('/skill-styles/buy', rateLimit({ name: 'skill-styles-buy', limit: () => 10, windowMs: MINUTE, key }), validate({ body: styleBuyBody }), controller.buy);
  return r;
}
