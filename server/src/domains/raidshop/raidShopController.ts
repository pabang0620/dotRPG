import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './raidShopService';
import type { BuyBody } from './raidShopValidation';

type P = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.listShop(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function buy(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<BuyBody, P>(res);
    sendStored(res, await service.buy(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
