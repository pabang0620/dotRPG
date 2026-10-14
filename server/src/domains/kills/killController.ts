import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './killService';
import { fieldBossStatus } from './fieldBossService';
import { successResponse } from '../../utils/response';
import type { KillBody } from './killValidation';

export async function report(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<KillBody, { uuid: string }>(res);
    sendStored(res, await service.reportKill(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function fieldBosses(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await fieldBossStatus(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}
