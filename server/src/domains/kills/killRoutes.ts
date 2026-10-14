import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as controller from './killController';
import { killBody } from './killValidation';

/** 버전·인증은 characterRoutes의 /characters 공통 미들웨어가 먼저 처리한다(routes/index.ts 순서) */
export function createKillRouter(): Router {
  const r = Router();
  r.post(
    '/characters/:uuid/kills',
    rateLimit({ name: 'kill-sec', limit: (c) => c.rate.killPerSec, windowMs: 1000, key: charKey }),
    rateLimit({ name: 'kill-min', limit: (c) => c.rate.killPerMin, windowMs: 60_000, key: charKey }),
    validate({ params: charParams, body: killBody }),
    controller.report,
  );
  // 필드 보스 다음 출현 시각(서버 시각 기준). 맵에 들어갈 때 한 번 읽는다
  r.get(
    '/characters/:uuid/field-bosses',
    rateLimit({ name: 'field-boss-min', limit: () => 60, windowMs: 60_000, key: charKey }),
    validate({ params: charParams }),
    controller.fieldBosses,
  );
  return r;
}
