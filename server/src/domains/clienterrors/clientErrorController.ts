import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import * as service from './clientErrorService';
import type { ClientErrorBody } from './clientErrorValidation';

export async function report(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<ClientErrorBody, unknown>(res);
    const account = getAccount(res.locals);
    await service.report(account.id, account.uuid, body);
    res.status(204).end();
  } catch (err) {
    next(err);
  }
}
