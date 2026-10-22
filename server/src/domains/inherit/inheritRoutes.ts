import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './inheritController';
import { inheritBody } from './inheritValidation';

// 출시용(wonho) 경로. ch3의 /promote/inherit와는 ch3을 합칠 때 하나로 정리한다
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
