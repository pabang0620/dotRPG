import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './starshopService';
import type { ExchangeBody, PullBody } from './starshopValidation';

type P = { uuid: string };
const acc = (res: Response) => getAccount(res.locals).id;

export async function summary(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.summary(acc(res), params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function pull(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PullBody, P>(res);
    sendStored(res, await service.pull(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function exchange(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ExchangeBody, P>(res);
    sendStored(res, await service.exchange(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}
