import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './inheritController';
import { inheritBody } from './inheritValidation';

// 장비 계승 단일 경로(Lv10->20->30->40, 챕터 3의 40->50 포함)
export function createInheritRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/inherit',
    rateLimit({ name: 'inherit', limit: (c) => c.rate.enhancePerSec, windowMs: 1000, key: charKey }),
    validate({ params: charParams, body: inheritBody }),
    controller.inherit,
  );
  return r;
}
