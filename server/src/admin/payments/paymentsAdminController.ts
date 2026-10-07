import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './paymentsAdminService';
import * as grants from './starGrantService';
import * as revoke from './revokeOutcomesService';
import type { BlockBody, FlagListQuery, GrantActionBody, GrantCreateBody, GrantListQuery, OrderListQuery, RecheckBody, ResolveBody, RevokeBody, UnblockBody } from './paymentsAdminValidation';

type P = { uuid: string };
const ip = (req: Request): string => req.ip ?? '';

export async function listOrders(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, OrderListQuery>(res);
    const r = await service.listOrders(query);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function orderDetail(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.orderDetail(getAdmin(res.locals), ip(req), params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function recheck(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<RecheckBody, P>(res);
    sendAction(res, await service.recheck(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function accountPayments(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.accountPayments(getAdmin(res.locals), ip(req), params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function block(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<BlockBody, P>(res);
    sendAction(res, await service.block(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function unblock(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<UnblockBody, P>(res);
    sendAction(res, await service.unblock(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function listFlags(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, FlagListQuery>(res);
    const r = await service.listFlags(query);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function resolveFlag(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ResolveBody, P>(res);
    sendAction(res, await service.resolveFlag(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function createGrant(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<GrantCreateBody>(res);
    sendAction(res, await grants.createGrant(getAdmin(res.locals), ip(req), body));
  } catch (err) {
    next(err);
  }
}

export async function listGrants(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, GrantListQuery>(res);
    const r = await grants.listGrants(query);
    successResponse(res, { items: r.items }, '', 200, { next_cursor: r.next_cursor });
  } catch (err) {
    next(err);
  }
}

export async function approveGrant(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<GrantActionBody, P>(res);
    sendAction(res, await grants.approveGrant(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function cancelGrant(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<GrantActionBody, P>(res);
    sendAction(res, await grants.cancelGrant(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function revokeOutcomes(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<RevokeBody, P>(res);
    sendAction(res, await revoke.revokeOutcomes(getAdmin(res.locals), ip(req), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function reconcile(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.reconcileStatus(getAdmin(res.locals), ip(req)));
  } catch (err) {
    next(err);
  }
}
