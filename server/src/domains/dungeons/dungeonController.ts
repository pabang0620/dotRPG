import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './dungeonService';
import { settleRun } from '../partyruns/partySettle';
import type { EnterBody, PickBody, ResultBody, RunParams, SettleBody } from './dungeonValidation';

type P = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.listDungeons(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function enter(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<EnterBody, P>(res);
    sendStored(res, await service.enter(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function result(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ResultBody, RunParams>(res);
    sendStored(res, await service.reportResult(getAccount(res.locals).id, params.uuid, params.run_id, body));
  } catch (err) {
    next(err);
  }
}

export async function pick(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PickBody, RunParams>(res);
    sendStored(res, await service.pickCard(getAccount(res.locals).id, params.uuid, params.run_id, body));
  } catch (err) {
    next(err);
  }
}

export async function settle(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SettleBody, RunParams>(res);
    sendStored(res, await settleRun(getAccount(res.locals).id, params.uuid, params.run_id, body));
  } catch (err) {
    next(err);
  }
}

export async function getRun(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, RunParams>(res);
    successResponse(res, await service.getRun(getAccount(res.locals).id, params.uuid, params.run_id));
  } catch (err) {
    next(err);
  }
}
