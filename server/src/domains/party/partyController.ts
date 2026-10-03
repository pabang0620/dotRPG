import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './partyService';
import type {
  ApplicationParams, CreatePartyBody, ListQuery, PartyParams, PatchPartyBody, PollQuery, ReadyBody, RequestOnlyBody, RespondBody, TargetBody,
} from './partyValidation';

type P = { uuid: string };
const acc = (res: Response) => getAccount(res.locals).id;

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, P, ListQuery>(res);
    const r = await service.listParties(acc(res), params.uuid, query);
    successResponse(res, r.data, '', 200, r.meta);
  } catch (err) {
    next(err);
  }
}

export async function create(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<CreatePartyBody, P>(res);
    sendStored(res, await service.createParty(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function mine(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, P, PollQuery>(res);
    successResponse(res, await service.getMyParty(acc(res), params.uuid, query));
  } catch (err) {
    next(err);
  }
}

export async function patch(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<PatchPartyBody, P>(res);
    successResponse(res, await service.patchParty(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function apply(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RequestOnlyBody, PartyParams>(res);
    sendStored(res, await service.applyToParty(acc(res), params.uuid, params.party_id, body));
  } catch (err) {
    next(err);
  }
}

export async function cancelApplication(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, ApplicationParams>(res);
    successResponse(res, await service.cancelApplication(acc(res), params.uuid, params));
  } catch (err) {
    next(err);
  }
}

export async function respond(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RespondBody, ApplicationParams>(res);
    sendStored(res, await service.respondToApplication(acc(res), params.uuid, params, body));
  } catch (err) {
    next(err);
  }
}

export async function ready(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ReadyBody, P>(res);
    successResponse(res, await service.setReady(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function leave(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<RequestOnlyBody, P>(res);
    sendStored(res, await service.leaveParty(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function kick(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<TargetBody, P>(res);
    sendStored(res, await service.kickMember(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function leader(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<TargetBody, P>(res);
    sendStored(res, await service.passLeader(acc(res), params.uuid, body));
  } catch (err) {
    next(err);
  }
}
