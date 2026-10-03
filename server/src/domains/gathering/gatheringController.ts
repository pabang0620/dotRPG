import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './gatheringService';
import type { ChestBody, DeliveryBody, GatherBody, NodesParams } from './gatheringValidation';

type P = { uuid: string };

export async function gather(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<GatherBody, P>(res);
    sendStored(res, await service.gather(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function nodes(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, NodesParams>(res);
    successResponse(res, await service.listCoolingNodes(getAccount(res.locals).id, params.uuid, params.map_id));
  } catch (err) {
    next(err);
  }
}

export async function openChest(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ChestBody, P>(res);
    sendStored(res, await service.openChest(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function deliver(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<DeliveryBody, P>(res);
    sendStored(res, await service.deliver(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
