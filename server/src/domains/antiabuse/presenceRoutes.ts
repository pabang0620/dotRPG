import { Router } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import * as controller from './presenceController';
import { charParams, leaveBody, presenceBody } from './presenceValidation';

/** 버전·인증은 characterRoutes의 /characters 공통 미들웨어가 먼저 처리한다(routes/index.ts 순서). 계정당 속도 제한 */
export function createPresenceRouter(): Router {
  const r = Router();
  const acct = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  r.post(
    '/characters/:uuid/presence',
    rateLimit({ name: 'presence-10s', limit: (c) => c.aa.presence.rate10s, windowMs: 10_000, key: acct }),
    rateLimit({ name: 'presence-min', limit: (c) => c.aa.presence.rateMin, windowMs: 60_000, key: acct }),
    validate({ params: charParams, body: presenceBody }),
    controller.presence,
  );
  r.post('/characters/:uuid/presence/leave', validate({ params: charParams, body: leaveBody }), controller.leave);
  r.get('/characters/:uuid/economy-hold', validate({ params: charParams }), controller.economyHold);
  return r;
}
