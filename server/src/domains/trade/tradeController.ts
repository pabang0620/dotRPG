import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import { charParams } from '../economy/economyValidation';
import type { z } from 'zod';
import * as service from './tradeService';
import type { ConfirmBody, CreateBody, LockBody, OfferBody, PlainBody, TradeParams } from './tradeValidation';

type CharP = z.infer<typeof charParams>;

export async function current(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharP>(res);
    successResponse(res, await service.currentTrade(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function create(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<CreateBody, CharP>(res);
    sendStored(res, await service.createTrade(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

type Plain = (accountId: number, characterUuid: string, tradeUuid: string, body: PlainBody) => ReturnType<typeof service.acceptTrade>;

const plain =
  (fn: Plain) =>
  async (_req: Request, res: Response, next: NextFunction): Promise<void> => {
    try {
      const { body, params } = getValidated<PlainBody, TradeParams>(res);
      sendStored(res, await fn(getAccount(res.locals).id, params.uuid, params.trade_id, body));
    } catch (err) {
      next(err);
    }
  };

export const accept = plain(service.acceptTrade);
export const decline = plain(service.declineTrade);
export const cancel = plain(service.cancelTrade);

export async function offer(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<OfferBody, TradeParams>(res);
    sendStored(res, await service.putOffer(getAccount(res.locals).id, params.uuid, params.trade_id, body));
  } catch (err) {
    next(err);
  }
}

export async function lock(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<LockBody, TradeParams>(res);
    sendStored(res, await service.lockTrade(getAccount(res.locals).id, params.uuid, params.trade_id, body));
  } catch (err) {
    next(err);
  }
}

export async function confirm(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ConfirmBody, TradeParams>(res);
    sendStored(res, await service.confirmTrade(getAccount(res.locals).id, params.uuid, params.trade_id, body));
  } catch (err) {
    next(err);
  }
}
