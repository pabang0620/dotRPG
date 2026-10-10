import { Router } from 'express';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './dailyController';
import { slotBody } from './dailyValidation';

/** 14단계: 일일 의뢰(Docs/server/phase14_daily_quests.md 4절). 버전·인증은 /characters 공통 미들웨어가 처리한다 */
export function createDailyRouter(): Router {
  const r = Router();
  const read = rateLimit({ name: 'dailies-read', limit: () => 30, windowMs: MINUTE, key: charKey });
  const write = rateLimit({ name: 'dailies-write', limit: () => 20, windowMs: MINUTE, key: charKey });
  r.get('/characters/:uuid/dailies', read, validate({ params: charParams }), controller.list);
  r.post('/characters/:uuid/dailies/accept', write, validate({ params: charParams, body: slotBody }), controller.accept);
  r.post('/characters/:uuid/dailies/claim', write, validate({ params: charParams, body: slotBody }), controller.claim);
  return r;
}
