import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './maintenanceAdminService';
import type { BroadcastBody, ExtendBody, ScheduleBody } from './maintenanceValidation';

type P = { uuid: string };

export async function status(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.status());
  } catch (err) {
    next(err);
  }
}

export async function schedule(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<ScheduleBody>(res);
    sendAction(res, await service.schedule(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<{ request_id: string }, P>(res);
    sendAction(res, await service.cancel(getAdmin(res.locals), req.ip ?? '', params.uuid, body.request_id));
  } catch (err) {
    next(err);
  }
}

export async function extend(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ExtendBody, P>(res);
    sendAction(res, await service.extend(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function end(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<{ request_id: string }, P>(res);
    sendAction(res, await service.end(getAdmin(res.locals), req.ip ?? '', params.uuid, body.request_id));
  } catch (err) {
    next(err);
  }
}

export async function drain(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.drain());
  } catch (err) {
    next(err);
  }
}

export async function broadcast(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<BroadcastBody>(res);
    sendAction(res, await service.broadcast(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}
