import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendAction } from '../common/adminResponse';
import { getAdmin } from '../common/adminAuth';
import * as service from './adminsService';
import type { AdminActionBody, AuditQuery, CreateAdminBody } from './adminsValidation';

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.listAdmins());
  } catch (err) {
    next(err);
  }
}

export async function create(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CreateAdminBody>(res);
    sendAction(res, await service.createAdmin(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}

export async function action(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<AdminActionBody, { uuid: string }>(res);
    sendAction(res, await service.adminAction(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function audit(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, AuditQuery>(res);
    const r = await service.queryAudit(query);
    successResponse(res, { entries: r.entries }, '', 200, { next_before: r.next_before });
  } catch (err) {
    next(err);
  }
}
