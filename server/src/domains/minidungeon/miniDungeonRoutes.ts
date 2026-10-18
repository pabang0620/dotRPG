import { Router, type NextFunction, type Request, type Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { getValidated, validate } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as service from './miniDungeonService';
import { enterBody, killBody, leaveBody, requestBody, runParams, type EnterBody, type KillBody, type LeaveBody, type RequestBody } from './miniDungeonValidation';

export function createMiniDungeonRouter(): Router {
  const r = Router();
  const limit = (name: string) => rateLimit({ name: `mini-${name}`, limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey });
  r.get('/characters/:uuid/mini-dungeon', limit('status'), validate({ params: charParams }), async (_req: Request, res: Response, next: NextFunction) => {
    try { const { params } = getValidated<unknown, { uuid: string }>(res); successResponse(res, await service.status(getAccount(res.locals).id, params.uuid)); }
    catch (err) { next(err); }
  });
  r.post('/characters/:uuid/mini-dungeon/enter', limit('enter'), validate({ params: charParams, body: enterBody }), async (_req: Request, res: Response, next: NextFunction) => {
    try { const { params, body } = getValidated<EnterBody, { uuid: string }>(res); sendStored(res, await service.enter(getAccount(res.locals).id, params.uuid, body)); }
    catch (err) { next(err); }
  });
  const base = '/characters/:uuid/mini-dungeon/runs/:run_id';
  r.post(`${base}/pulse`, limit('pulse'), validate({ params: runParams, body: requestBody }), async (_req: Request, res: Response, next: NextFunction) => {
    try { const { params, body } = getValidated<RequestBody, { uuid: string; run_id: string }>(res); sendStored(res, await service.pulse(getAccount(res.locals).id, params.uuid, params.run_id, body)); }
    catch (err) { next(err); }
  });
  r.post(`${base}/kills`, limit('kills'), validate({ params: runParams, body: killBody }), async (_req: Request, res: Response, next: NextFunction) => {
    try { const { params, body } = getValidated<KillBody, { uuid: string; run_id: string }>(res); sendStored(res, await service.kill(getAccount(res.locals).id, params.uuid, params.run_id, body)); }
    catch (err) { next(err); }
  });
  r.post(`${base}/leave`, limit('leave'), validate({ params: runParams, body: leaveBody }), async (_req: Request, res: Response, next: NextFunction) => {
    try { const { params, body } = getValidated<LeaveBody, { uuid: string; run_id: string }>(res); sendStored(res, await service.leave(getAccount(res.locals).id, params.uuid, params.run_id, body)); }
    catch (err) { next(err); }
  });
  return r;
}
