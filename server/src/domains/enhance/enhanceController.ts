import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './enhanceService';
import type { EnhanceBody, TicketBody } from './enhanceValidation';

export async function enhance(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<EnhanceBody, { uuid: string }>(res);
    sendStored(res, await service.enhance(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function ticket(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<TicketBody, { uuid: string }>(res);
    sendStored(res, await service.applyTicket(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
