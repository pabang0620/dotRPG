import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as live from './partyRunLiveService';
import * as service from './partyRunService';
import type { ClaimBody, HeartbeatBody, HostReportBody, JoinBody, RequestOnlyBody, RunParams, StartBody } from './partyRunValidation';

type P = { uuid: string };
const acc = (res: Response) => getAccount(res.locals).id;

export async function start(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<StartBody, P>(res);
    sendStored(res, await service.startRun(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function get(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, RunParams>(res);
    successResponse(res, await service.getRun(acc(res), params.uuid, params.id));
  } catch (err) {
    next(err);
  }
}

export async function join(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<JoinBody, RunParams>(res);
    sendStored(res, await service.joinRun(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function begin(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RequestOnlyBody, RunParams>(res);
    sendStored(res, await service.beginRunByHost(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function heartbeat(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<HeartbeatBody, RunParams>(res);
    successResponse(res, await live.heartbeat(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimBody, RunParams>(res);
    sendStored(res, await live.claimHost(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function hostReport(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<HostReportBody, RunParams>(res);
    sendStored(res, await live.hostReport(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function leave(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RequestOnlyBody, RunParams>(res);
    sendStored(res, await live.leaveRun(acc(res), params.uuid, params.id, body));
  } catch (err) {
    next(err);
  }
}
