import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './partyInvitesController';
import { charParams, inviteBodyIn, inviteParams, respondBody } from './partyInvitesValidation';

/** 버전·인증은 /characters 공통 미들웨어(characterRoutes)가 처리한다. 반드시 그 뒤에 등록한다. */
export function createPartyInvitesRouter(): Router {
  const r = Router();
  const action = rateLimit({ name: 'party-invite', limit: (c) => c.rate.partyActionPerSec, windowMs: 1000, key: charKey });
  const base = '/characters/:uuid/party/invites';
  r.post(base, action, validate({ params: charParams, body: inviteBodyIn }), controller.send);
  r.post(`${base}/:id/respond`, action, validate({ params: inviteParams, body: respondBody }), controller.respond);
  r.delete(`${base}/:id`, action, validate({ params: inviteParams }), controller.cancel);
  return r;
}
