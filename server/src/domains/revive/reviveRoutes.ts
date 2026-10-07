import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as c from './reviveController';
import { reviveBody } from './reviveValidation';

/** /characters/{uuid}/revive : 버전(426)과 인증은 createCharacterRouter의 /characters 공통 미들웨어가 처리한다 */
export function createReviveRouter(): Router {
  const r = Router();
  const lim = (name: string) => rateLimit({ name: `revive-${name}`, limit: () => 5, windowMs: 1000, key: charKey });
  r.get('/characters/:uuid/revive', lim('status'), validate({ params: charParams }), c.status);
  r.post('/characters/:uuid/revive', lim('use'), validate({ params: charParams, body: reviveBody }), c.revive);
  return r;
}
