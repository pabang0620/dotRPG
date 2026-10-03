import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './opsService';
import type { RunJobBody } from './opsValidation';

export async function status(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.status());
  } catch (err) {
    next(err);
  }
}

export async function jobs(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.jobs());
  } catch (err) {
    next(err);
  }
}

export async function run(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<RunJobBody, { name: string }>(res);
    sendAction(res, await service.runJobManually(getAdmin(res.locals), req.ip ?? '', params.name, body.request_id, body.full === true));
  } catch (err) {
    next(err);
  }
}
