import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './sweepService';
import type { SweepBuyBody, SweepClaimBody, SweepRunBody } from './sweepValidation';

type CharP = { uuid: string };

export async function status(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharP>(res);
    successResponse(res, await service.sweepStatus(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export const run =
  (all: boolean) =>
  async (_req: Request, res: Response, next: NextFunction): Promise<void> => {
    try {
      const { body, params } = getValidated<SweepRunBody, CharP>(res);
      sendStored(res, await service.runSweep(getAccount(res.locals).id, params.uuid, body, all));
    } catch (err) {
      next(err);
    }
  };

export async function buy(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SweepBuyBody, CharP>(res);
    sendStored(res, await service.buyTickets(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function claimWeekly(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SweepClaimBody, CharP>(res);
    sendStored(res, await service.claimWeekly(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
