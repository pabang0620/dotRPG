import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './inheritService';
import type { InheritBody } from './inheritValidation';

export async function inherit(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<InheritBody, { uuid: string }>(res);
    sendStored(res, await service.inherit(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
