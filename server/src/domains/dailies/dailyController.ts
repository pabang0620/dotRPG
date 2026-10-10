import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './dailyService';
import type { SlotBody } from './dailyValidation';

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await service.list(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function accept(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SlotBody, { uuid: string }>(res);
    sendStored(res, await service.accept(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SlotBody, { uuid: string }>(res);
    sendStored(res, await service.claim(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
