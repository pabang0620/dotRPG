import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './shopService';
import type { BuyBody, SellBody } from './shopValidation';

type P = { uuid: string };

export async function buy(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<BuyBody, P>(res);
    sendStored(res, await service.buy(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function sell(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SellBody, P>(res);
    sendStored(res, await service.sell(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
