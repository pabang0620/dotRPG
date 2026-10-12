import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './starPassService';
import type { DailyBody } from './starPassValidation';

export async function status(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.status(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function daily(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<DailyBody, { uuid: string }>(res);
    sendStored(res, await service.claimDaily(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
