import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './economyAdminService';
import type { GrantBody, GrantListQuery } from './economyAdminValidation';

export async function daily(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, { day?: string }>(res);
    successResponse(res, await service.daily(query.day));
  } catch (err) {
    next(err);
  }
}

export async function grant(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<GrantBody>(res);
    sendAction(res, await service.createGrant(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}

export async function grants(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, GrantListQuery>(res);
    const r = await service.listGrants(query);
    successResponse(res, { items: r.items }, '', 200, { next_before: r.next_before });
  } catch (err) {
    next(err);
  }
}
