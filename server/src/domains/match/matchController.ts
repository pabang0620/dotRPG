import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './matchService';
import type { QueueBody, RequestOnlyBody } from './matchValidation';

type P = { uuid: string };

export async function enqueue(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<QueueBody, P>(res);
    sendStored(res, await service.enqueue(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.cancelQueue(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function fillAi(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RequestOnlyBody, P>(res);
    successResponse(res, await service.fillAi(getAccount(res.locals).id, params.uuid, body), '', 201);
  } catch (err) {
    next(err);
  }
}
