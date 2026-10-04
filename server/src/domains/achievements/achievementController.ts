import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './achievementService';
import type { CharParams, TitleBody } from './achievementValidation';

const acc = (res: Response) => getAccount(res.locals).id;

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharParams>(res);
    successResponse(res, await service.listAchievements(acc(res), params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function equip(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<TitleBody, CharParams>(res);
    successResponse(res, await service.equipTitle(acc(res), params.uuid, body.achievement_id));
  } catch (err) {
    next(err);
  }
}
