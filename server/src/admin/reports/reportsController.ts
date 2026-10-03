import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './reportsService';
import type { ListQuery, ResolveBody } from './reportsValidation';

type P = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, ListQuery>(res);
    successResponse(res, await service.list(query));
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

export async function take(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<{ request_id: string }, P>(res);
    sendAction(res, await service.take(getAdmin(res.locals), req.ip ?? '', params.uuid, body.request_id));
  } catch (err) {
    next(err);
  }
}

export async function resolve(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ResolveBody, P>(res);
    sendAction(res, await service.resolve(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}
