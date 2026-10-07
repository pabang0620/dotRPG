import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './sealedBoxService';
import type { OpenBody, PullBody } from './sealedBoxValidation';

type P = { uuid: string };

export async function summary(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.summary(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function pull(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PullBody, P>(res);
    sendStored(res, await service.pull(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function open(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<OpenBody, P>(res);
    sendStored(res, await service.openItem(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
