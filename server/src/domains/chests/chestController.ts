import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './chestService';
import type { ChestBody } from './chestValidation';

type P = { uuid: string };

export async function openChest(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ChestBody, P>(res);
    sendStored(res, await service.openChest(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
