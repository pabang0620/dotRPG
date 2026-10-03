import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './partyInvitesService';
import type { InviteBodyIn, InviteParams, RespondBody } from './partyInvitesValidation';

const acc = (res: Response) => getAccount(res.locals).id;

export async function send(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<InviteBodyIn, { uuid: string }>(res);
    sendStored(res, await service.sendInvite(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function respond(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RespondBody, InviteParams>(res);
    sendStored(res, await service.respondInvite(acc(res), params.uuid, params, body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, InviteParams>(res);
    successResponse(res, await service.cancelInvite(acc(res), params.uuid, params));
  } catch (err) {
    next(err);
  }
}
