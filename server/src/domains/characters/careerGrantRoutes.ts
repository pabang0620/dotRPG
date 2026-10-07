import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './careerGrantController';
import { advanceBody, charParams, promoteBody, trialFinishBody, trialStartBody } from './careerGrantValidation';

/** 버전·인증은 characterRoutes의 /characters 공통 미들웨어가 먼저 처리한다(routes/index.ts 순서). 속도 제한은 캐릭터당 RATE_CAREER_PER_SEC */
export function createCareerGrantRouter(): Router {
  const r = Router();
  const limit = rateLimit({ name: 'career', limit: (c) => c.aa.career.ratePerSec, windowMs: 1000, key: charKey });
  r.post('/characters/:uuid/career/promote', limit, validate({ params: charParams, body: promoteBody }), controller.promote);
  r.post('/characters/:uuid/career/awakening/trial/start', limit, validate({ params: charParams, body: trialStartBody }), controller.trialStart);
  r.post('/characters/:uuid/career/awakening/trial/finish', limit, validate({ params: charParams, body: trialFinishBody }), controller.trialFinish);
  r.post('/characters/:uuid/career/awakening/advance', limit, validate({ params: charParams, body: advanceBody }), controller.advance);
  return r;
}
