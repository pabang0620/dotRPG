import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { sendStored } from '../economy/economyController';
import * as service from './inventoryService';
import type { EquipBody, StorageBody, UnequipBody, UseBody } from './inventoryValidation';

type P = { uuid: string };

export async function use(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<UseBody, P>(res);
    sendStored(res, await service.useItem(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function moveStorage(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<StorageBody, P>(res);
    sendStored(res, await service.moveStorage(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function equip(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<EquipBody, P>(res);
    sendStored(res, await service.equip(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function unequip(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<UnequipBody, P>(res);
    sendStored(res, await service.unequip(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
