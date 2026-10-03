import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './reportsService';
import type { ReportBody } from './reportsValidation';

export async function submit(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<ReportBody>(res);
    sendStored(res, await service.submitReport(getAccount(res.locals).id, body));
  } catch (err) {
    next(err);
  }
}
