import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './economyHoldsService';
import type { ClawbackBody, HoldListQuery, LinksQuery, ManualHoldBody, ReleaseBody, TradeFlagsQuery } from './economyHoldsValidation';

type P = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, HoldListQuery>(res);
    const r = await service.listHolds(query);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function detail(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.holdDetail(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function release(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ReleaseBody, P>(res);
    sendAction(res, await service.release(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function clawback(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ClawbackBody, P>(res);
    sendAction(res, await service.clawback(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function create(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<ManualHoldBody>(res);
    sendAction(res, await service.manualHold(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}

export async function links(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, P, LinksQuery>(res);
    successResponse(res, await service.accountLinks(getAdmin(res.locals), req.ip ?? '', params.uuid, query));
  } catch (err) {
    next(err);
  }
}

export async function tradeFlags(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, TradeFlagsQuery>(res);
    const r = await service.tradeFlags(query);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function incomeCaps(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, service.incomeCaps());
  } catch (err) {
    next(err);
  }
}

export async function velocity(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.characterVelocity(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}
