import { Router } from 'express';
import type { AppConfig } from '../../config/env';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './fieldController';
import { charParams, claimBody, enterBody, getQuery, heartbeatBody, observeBody, requestOnlyBody, sessionParams } from './fieldValidation';

export function createFieldRouter(): Router {
  const r = Router();
  const per = (name: string, windowMs: number, pick: (c: AppConfig) => number) =>
    rateLimit({ name: `field-${name}`, limit: pick, windowMs, key: charKey });
  const base = '/characters/:uuid';

  r.post(
    `${base}/field-sessions/enter`,
    per('enter-s', 1000, (c) => c.rate.p8.fieldEnterSec),
    per('enter-m', MINUTE, (c) => c.rate.p8.fieldEnterMin),
    validate({ params: charParams, body: enterBody }),
    controller.enter,
  );
  r.get(`${base}/field-session`, per('me', 1000, (c) => c.rate.p8.fieldMeSec), validate({ params: charParams }), controller.mine);
  r.get(`${base}/field-sessions/:id`, per('get', 1000, (c) => c.rate.p8.fieldGetSec), validate({ params: sessionParams, query: getQuery }), controller.get);
  r.post(`${base}/field-sessions/:id/heartbeat`, per('hb', 2000, (c) => c.rate.heartbeatPer2Sec), validate({ params: sessionParams, body: heartbeatBody }), controller.heartbeat);
  r.post(`${base}/field-sessions/:id/leave`, per('leave', 1000, (c) => c.rate.p8.fieldLeaveSec), validate({ params: sessionParams, body: requestOnlyBody }), controller.leave);
  r.post(`${base}/field-sessions/:id/host/claim`, per('claim', 1000, (c) => c.rate.p8.fieldClaimSec), validate({ params: sessionParams, body: claimBody }), controller.claim);
  r.post(`${base}/field-sessions/:id/observe`, per('observe', 5000, (c) => c.rate.p8.fieldObserve5s), validate({ params: sessionParams, body: observeBody }), controller.observe);
  return r;
}
