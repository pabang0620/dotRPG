import type { NextFunction, Request, Response } from 'express';
import { successResponse } from '../../utils/response';
import * as service from './systemService';

export async function health(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { ok, data } = await service.getHealth();
    successResponse(res, data, '', ok ? 200 : 503);
  } catch (err) {
    next(err);
  }
}

export function meta(_req: Request, res: Response, next: NextFunction): void {
  try {
    res.setHeader('Cache-Control', 'no-store');
    successResponse(res, service.getMeta());
  } catch (err) {
    next(err);
  }
}
