import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './testToolsController';
import { levelBody, mainQuestBody } from './testToolsValidation';

/** 시험 도구(시험 서버 전용). routes/index.ts가 DEPLOY_STAGE=test 일 때만 등록한다 */
export function createTestToolsRouter(): Router {
  const r = Router();
  const limit = rateLimit({ name: 'test-tools', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey });
  r.post('/characters/:uuid/test-tools/main-quest', limit, validate({ params: charParams, body: mainQuestBody }), controller.mainQuest);
  r.post('/characters/:uuid/test-tools/level', limit, validate({ params: charParams, body: levelBody }), controller.level);
  return r;
}
