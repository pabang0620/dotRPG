import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './promoteService';
import type { PromoteBody } from './promoteValidation';

export async function promote(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PromoteBody, { uuid: string }>(res);
    sendStored(res, await service.promote(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
