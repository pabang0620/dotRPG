import { Router, type NextFunction, type Request, type Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { getValidated, validate } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import type { BossKind } from './bossRushRules';
import * as service from './bossRushService';
import { attemptParams, finishBody, kindParams, startBody, type FinishBody, type StartBody } from './bossRushValidation';

/** 월드보스·길드보스(Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 7절). /characters 공통 버전·인증 미들웨어 뒤에 붙는다 */
export function createBossRushRouter(): Router {
  const r = Router();
  const limit = (name: string) => rateLimit({ name: `boss-${name}`, limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey });
  r.get('/characters/:uuid/bosses', limit('status'), validate({ params: charParams }), async (_req: Request, res: Response, next: NextFunction) => {
    try { const { params } = getValidated<unknown, { uuid: string }>(res); successResponse(res, await service.status(getAccount(res.locals).id, params.uuid)); }
    catch (err) { next(err); }
  });
  r.post('/characters/:uuid/bosses/:kind/attempts', limit('start'), validate({ params: kindParams, body: startBody }), async (_req: Request, res: Response, next: NextFunction) => {
    try {
      const { params, body } = getValidated<StartBody, { uuid: string; kind: BossKind }>(res);
      sendStored(res, await service.start(getAccount(res.locals).id, params.uuid, params.kind, body));
    } catch (err) { next(err); }
  });
  r.post('/characters/:uuid/bosses/:kind/attempts/:attemptId/finish', limit('finish'), validate({ params: attemptParams, body: finishBody }), async (_req: Request, res: Response, next: NextFunction) => {
    try {
      const { params, body } = getValidated<FinishBody, { uuid: string; kind: BossKind; attemptId: string }>(res);
      sendStored(res, await service.finish(getAccount(res.locals).id, params.uuid, params.kind, params.attemptId, body));
    } catch (err) { next(err); }
  });
  return r;
}
