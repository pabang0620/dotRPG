import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './questService';
import type { ClaimBody, ClaimParams } from './questValidation';

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimBody, ClaimParams>(res);
    sendStored(res, await service.claimQuest(getAccount(res.locals).id, params.uuid, params.quest_id, body));
  } catch (err) {
    next(err);
  }
}
