import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './testToolsService';
import type { LevelBody, MainQuestBody } from './testToolsValidation';

export async function mainQuest(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<MainQuestBody, { uuid: string }>(res);
    sendStored(res, await service.pushMainQuest(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function level(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<LevelBody, { uuid: string }>(res);
    sendStored(res, await service.pushLevel(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
