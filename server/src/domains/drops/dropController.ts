import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './dropService';
import type { ClaimBody } from './dropValidation';

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimBody, { uuid: string }>(res);
    sendStored(res, await service.claimDrops(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
