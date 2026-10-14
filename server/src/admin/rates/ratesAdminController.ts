import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './ratesAdminService';
import type { IdParamsT, ListQueryT } from './ratesAdminValidation';

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, ListQueryT>(res);
    const r = await service.list(query);
    successResponse(res, { items: r.items }, '', 200, { next_before: r.next_before });
  } catch (err) {
    next(err);
  }
}

export async function show(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, IdParamsT>(res);
    successResponse(res, await service.show(params.id));
  } catch (err) {
    next(err);
  }
}
