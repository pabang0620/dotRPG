import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './skillStyleService';
import type { StyleBuyBody } from './skillStyleValidation';

export async function view(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.view(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function buy(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<StyleBuyBody>(res);
    successResponse(res, await service.buy(getAccount(res.locals).id, body));
  } catch (err) {
    next(err);
  }
}
