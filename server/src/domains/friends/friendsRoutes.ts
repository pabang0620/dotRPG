import { Router } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import * as controller from './friendsController';
import { idParams, respondBody, sendRequestBody } from './friendsValidation';

const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

/** 친구는 계정 단위라 클라이언트 버전만 검사한다(데이터 버전 검사 안 함) */
export function createFriendsRouter(): Router {
  const r = Router();
  r.use('/friends', versionCheck({ data: false }), requireAuth);
  const list = rateLimit({ name: 'friends-list', limit: (c) => c.rate.socialListPerSec, windowMs: 1000, key: accountKey });
  const request = rateLimit({ name: 'friends-request', limit: (c) => c.rate.socialRequestPerMin, windowMs: MINUTE, key: accountKey });
  const action = rateLimit({ name: 'friends-action', limit: (c) => c.rate.socialActionPerSec, windowMs: 1000, key: accountKey });

  r.get('/friends', list, controller.list);
  r.post('/friends/requests', request, validate({ body: sendRequestBody }), controller.sendRequest);
  r.post('/friends/requests/:id/respond', action, validate({ params: idParams, body: respondBody }), controller.respond);
  r.delete('/friends/requests/:id', action, validate({ params: idParams }), controller.cancelRequest);
  r.delete('/friends/:id', action, validate({ params: idParams }), controller.removeFriend);
  return r;
}
