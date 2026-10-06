import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { holdStatusOf } from './holds';
import * as service from './presenceService';
import type { PresenceBody } from './presenceValidation';

export async function presence(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PresenceBody, { uuid: string }>(res);
    successResponse(res, await service.sendPresence(getAccount(res.locals).id, params.uuid, body, req.ip ?? '', req.header('x-client-version')));
  } catch (err) {
    next(err);
  }
}

export async function leave(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await service.leavePresence(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

/** P3: 내 경제 정지 여부(수치 없음) */
export async function economyHold(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await holdStatusOf(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}
