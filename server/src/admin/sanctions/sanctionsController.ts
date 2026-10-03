import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './sanctionsService';
import type { CreateSanctionBody, RevokeBody } from './sanctionsValidation';

export async function create(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<CreateSanctionBody, { uuid: string }>(res);
    sendAction(res, await service.createSanction(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function revoke(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<RevokeBody, { uuid: string }>(res);
    sendAction(res, await service.revokeSanction(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}
