import { Router } from 'express';
import type { AppConfig } from '../../config/env';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import * as controller from './partyRunController';
import { charParams, claimBody, heartbeatBody, hostReportBody, joinBody, requestOnlyBody, runParams, startBody } from './partyRunValidation';

export function createPartyRunRouter(): Router {
  const r = Router();
  const per = (name: string, windowMs: number, pick: (c: AppConfig) => number) =>
    rateLimit({ name: `prun-${name}`, limit: pick, windowMs, key: charKey });
  const once = per('once', 1000, (c) => c.rate.partyRunPerSec);
  const base = '/characters/:uuid';

  r.post(`${base}/party/start`, once, validate({ params: charParams, body: startBody }), controller.start);
  r.get(`${base}/party-runs/:id`, per('get', 1000, (c) => c.rate.partyPollPerSec), validate({ params: runParams }), controller.get);
  r.post(`${base}/party-runs/:id/join`, per('join', 1000, (c) => c.rate.partyRunPerSec), validate({ params: runParams, body: joinBody }), controller.join);
  r.post(`${base}/party-runs/:id/begin`, per('begin', 1000, (c) => c.rate.partyRunPerSec), validate({ params: runParams, body: requestOnlyBody }), controller.begin);
  r.post(`${base}/party-runs/:id/heartbeat`, per('hb', 2000, (c) => c.rate.heartbeatPer2Sec), validate({ params: runParams, body: heartbeatBody }), controller.heartbeat);
  r.post(`${base}/party-runs/:id/host/claim`, per('claim', 1000, (c) => c.rate.partyRunPerSec), validate({ params: runParams, body: claimBody }), controller.claim);
  r.post(`${base}/party-runs/:id/host-report`, per('report', 1000, (c) => c.rate.partyRunPerSec), validate({ params: runParams, body: hostReportBody }), controller.hostReport);
  r.post(`${base}/party-runs/:id/leave`, per('leave', 1000, (c) => c.rate.partyRunPerSec), validate({ params: runParams, body: requestOnlyBody }), controller.leave);
  return r;
}
