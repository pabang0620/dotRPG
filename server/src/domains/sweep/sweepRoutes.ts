import { Router } from 'express';
import type { AppConfig } from '../../config/env';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as c from './sweepController';
import { sweepBuyBody, sweepClaimBody, sweepRunBody } from './sweepValidation';

/** /characters/{uuid}/sweep... : 버전(426)과 인증은 createCharacterRouter의 /characters 공통 미들웨어가 처리한다 */
export function createSweepRouter(): Router {
  const r = Router();
  const lim = (name: string, pick: (x: AppConfig) => number) =>
    rateLimit({ name: `sweep-${name}`, limit: pick, windowMs: 1000, key: charKey });
  r.get('/characters/:uuid/sweep', lim('status', (x) => x.sweep.rate.statusPerSec), validate({ params: charParams }), c.status);
  r.post('/characters/:uuid/sweep/run', lim('run', (x) => x.sweep.rate.runPerSec), validate({ params: charParams, body: sweepRunBody }), c.run(false));
  r.post('/characters/:uuid/sweep/run-all', lim('run-all', (x) => x.sweep.rate.allPerSec), validate({ params: charParams, body: sweepRunBody }), c.run(true));
  r.post('/characters/:uuid/sweep/tickets/buy', lim('buy', (x) => x.sweep.rate.buyPerSec), validate({ params: charParams, body: sweepBuyBody }), c.buy);
  r.post('/characters/:uuid/sweep/weekly/claim', lim('claim', (x) => x.sweep.rate.claimPerSec), validate({ params: charParams, body: sweepClaimBody }), c.claimWeekly);
  return r;
}
