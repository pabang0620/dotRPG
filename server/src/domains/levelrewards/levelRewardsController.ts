import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './levelRewardsService';
import type { ClaimBody } from './levelRewardsValidation';

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.list(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<ClaimBody>(res);
    successResponse(res, await service.claim(getAccount(res.locals).id, body));
  } catch (err) {
    next(err);
  }
}
