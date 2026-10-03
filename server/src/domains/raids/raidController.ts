import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './raidService';

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await service.listRaids(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}
