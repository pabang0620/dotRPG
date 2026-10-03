import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './watchService';
import type { AnomalyQueryT, FlagQueryT } from './watchValidation';

export async function anomalies(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, AnomalyQueryT>(res);
    const r = await service.anomalies(query);
    successResponse(res, { items: r.items }, '', 200, { next_before: r.next_before });
  } catch (err) {
    next(err);
  }
}

export async function flags(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, FlagQueryT>(res);
    const r = await service.auctionFlags(query);
    successResponse(res, { items: r.items }, '', 200, { next_before: r.next_before });
  } catch (err) {
    next(err);
  }
}

export async function watchlist(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.watchlist());
  } catch (err) {
    next(err);
  }
}
