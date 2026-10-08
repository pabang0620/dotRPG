import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './withdrawalsService';
import type { AnonymizeNowBody, CancelBody, HoldBody, ListQuery, ReleaseBody, StartBody, TombstoneQuery } from './withdrawalsValidation';

type U = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, ListQuery>(res);
    const r = await service.listWithdrawals(query);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function accountDetail(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, U>(res);
    successResponse(res, await service.accountWithdrawal(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function start(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<StartBody, U>(res);
    sendAction(res, await service.startForAccount(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<CancelBody, U>(res);
    sendAction(res, await service.cancelByAdmin(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function anonymizeNow(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<AnonymizeNowBody, U>(res);
    sendAction(res, await service.anonymizeNow(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function hold(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<HoldBody, U>(res);
    sendAction(res, await service.setHold(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function findTombstone(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, TombstoneQuery>(res);
    successResponse(res, await service.findTombstone(getAdmin(res.locals), req.ip ?? '', query));
  } catch (err) {
    next(err);
  }
}

export async function releaseTombstone(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ReleaseBody, U>(res);
    sendAction(res, await service.releaseTombstone(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}
