import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './starshopService';
import * as synthService from './starshopSynth';
import type { ClaimBody, CollectionBody, DismantleBody, ExchangeBody, PullBody, SynthBody } from './starshopValidation';

type P = { uuid: string };
const acc = (res: Response) => getAccount(res.locals).id;

export async function summary(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.summary(acc(res), params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function pull(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PullBody, P>(res);
    sendStored(res, await service.pull(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimBody, P>(res);
    sendStored(res, await service.claim(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function exchange(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ExchangeBody, P>(res);
    sendStored(res, await service.exchange(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function synth(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<SynthBody, P>(res);
    sendStored(res, await synthService.synth(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function dismantle(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<DismantleBody, P>(res);
    sendStored(res, await synthService.dismantle(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function collection(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<CollectionBody, P>(res);
    sendStored(res, await synthService.registerCollection(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}
