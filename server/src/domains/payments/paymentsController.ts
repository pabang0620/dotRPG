import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './paymentsService';
import type { CreateOrderBody, ListQuery } from './paymentsValidation';

const acc = (res: Response) => getAccount(res.locals);

export async function products(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const a = acc(res);
    successResponse(res, await service.listProducts(a.id, a.uuid));
  } catch (err) {
    next(err);
  }
}

export async function createOrder(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CreateOrderBody>(res);
    const a = acc(res);
    const r = await service.createOrder(a.id, a.uuid, body, { ip: req.ip ?? null });
    successResponse(res, r.data, '', r.status);
  } catch (err) {
    next(err);
  }
}

export async function sync(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await service.syncOrder(acc(res).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function getOrder(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await service.getOrder(acc(res).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function listOrders(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, ListQuery>(res);
    const r = await service.listOrders(acc(res).id, query.limit, query.cursor);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function reconcile(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.reconcile(acc(res).id));
  } catch (err) {
    next(err);
  }
}
