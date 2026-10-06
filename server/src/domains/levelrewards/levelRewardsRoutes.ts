import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './levelRewardsController';
import { claimBody } from './levelRewardsValidation';

/** 레벨 달성 보상(13단계). 계정 단위: 클라이언트 버전만 검사한다 */
export function createLevelRewardsRouter(): Router {
  const r = Router();
  r.use('/level-rewards', versionCheck({ data: false }), requireAuth);
  const key = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  const read = rateLimit({ name: 'level-rewards-read', limit: () => 30, windowMs: MINUTE, key });
  const write = rateLimit({ name: 'level-rewards-claim', limit: () => 10, windowMs: MINUTE, key });
  r.get('/level-rewards', read, controller.list);
  r.post('/level-rewards/claim', write, validate({ body: claimBody }), controller.claim);
  return r;
}
