import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './withdrawalService';
import type { CancelBody, WithdrawBody } from './withdrawalValidation';

export async function info(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.getInfo(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function request(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<WithdrawBody>(res);
    sendStored(res, await service.requestWithdrawal(getAccount(res.locals).id, req.ip ?? 'unknown', body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CancelBody>(res);
    successResponse(res, await service.cancelWithdrawalByCredentials(req.ip ?? 'unknown', body));
  } catch (err) {
    next(err);
  }
}
