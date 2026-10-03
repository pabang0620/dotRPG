import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './friendsService';
import type { IdParams, RespondBody, SendRequestBody } from './friendsValidation';

const acc = (res: Response) => getAccount(res.locals).id;

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.listFriends(acc(res)));
  } catch (err) {
    next(err);
  }
}

export async function sendRequest(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<SendRequestBody>(res);
    sendStored(res, await service.sendRequest(acc(res), body));
  } catch (err) {
    next(err);
  }
}

export async function respond(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RespondBody, IdParams>(res);
    sendStored(res, await service.respond(acc(res), params.id, body));
  } catch (err) {
    next(err);
  }
}

export async function cancelRequest(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, IdParams>(res);
    successResponse(res, await service.cancelRequest(acc(res), params.id));
  } catch (err) {
    next(err);
  }
}

export async function removeFriend(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, IdParams>(res);
    successResponse(res, await service.removeFriend(acc(res), params.id));
  } catch (err) {
    next(err);
  }
}
