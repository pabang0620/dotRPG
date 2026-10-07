import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './levelRewardsService';
import { sendStored } from '../economy/economyController';
import * as passService from './growthPassService';
import type { ClaimBody, PassBuyBody, PassClaimBody } from './levelRewardsValidation';

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

export async function passBuy(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<PassBuyBody>(res);
    successResponse(res, await passService.buy(getAccount(res.locals).id, body));
  } catch (err) {
    next(err);
  }
}

export async function passClaim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PassClaimBody, { uuid: string }>(res);
    sendStored(res, await passService.claim(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
