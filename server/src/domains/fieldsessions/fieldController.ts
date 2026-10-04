import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './fieldService';
import type { ClaimBody, EnterBody, GetQuery, HeartbeatBody, ObserveBody, RequestOnlyBody, SessionParams } from './fieldValidation';

type P = { uuid: string };
const acc = (res: Response) => getAccount(res.locals).id;

export async function enter(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<EnterBody, P>(res);
    sendStored(res, await service.enterField(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function get(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, SessionParams, GetQuery>(res);
    successResponse(res, await service.getSession(acc(res), params.uuid, params.id, query));
  } catch (err) {
    next(err);
  }
}

export async function mine(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.mySession(acc(res), params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function heartbeat(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<HeartbeatBody, SessionParams>(res);
    successResponse(res, await service.heartbeat(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function leave(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RequestOnlyBody, SessionParams>(res);
    sendStored(res, await service.leaveField(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimBody, SessionParams>(res);
    sendStored(res, await service.claimHost(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function observe(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ObserveBody, SessionParams>(res);
    sendStored(res, await service.observe(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}
