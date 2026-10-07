import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './reviveService';
import type { ReviveBody } from './reviveValidation';

type CharP = { uuid: string };

export async function status(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharP>(res);
    successResponse(res, await service.reviveStatus(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function revive(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ReviveBody, CharP>(res);
    successResponse(res, await service.revive(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
