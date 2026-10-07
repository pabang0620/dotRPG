import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './careerGrantService';
import type { AdvanceBody, PromoteBody, TrialFinishBody, TrialStartBody } from './careerGrantValidation';

export async function promote(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PromoteBody, { uuid: string }>(res);
    sendStored(res, await service.promote(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function trialStart(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<TrialStartBody, { uuid: string }>(res);
    sendStored(res, await service.trialStart(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function trialFinish(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<TrialFinishBody, { uuid: string }>(res);
    sendStored(res, await service.trialFinish(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function advance(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<AdvanceBody, { uuid: string }>(res);
    sendStored(res, await service.advance(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
