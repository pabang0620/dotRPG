import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './heldRunsService';
import type { ReviewBody } from './heldRunsValidation';

type P = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.list());
  } catch (err) {
    next(err);
  }
}

export async function detail(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.detail(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function release(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ReviewBody, P>(res);
    sendAction(res, await service.release(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function reject(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ReviewBody, P>(res);
    sendAction(res, await service.reject(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}
