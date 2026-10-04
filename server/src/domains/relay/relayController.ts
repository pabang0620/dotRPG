import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import { fallbackTransport } from '../transport/fallbackService';
import * as service from './relayService';
import type { FallbackBody, RoomParams, TransportBody } from './relayValidation';

export async function ticket(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, RoomParams>(res);
    const a = getAccount(res.locals);
    successResponse(res, await service.issueTicket(a.id, a.uuid, params.uuid, params.kind, params.id));
  } catch (err) {
    next(err);
  }
}

export async function transport(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<TransportBody, RoomParams>(res);
    sendStored(
      res,
      await fallbackTransport(getAccount(res.locals).id, params.uuid, params.kind, params.id, {
        requestId: body.request_id,
        epoch: body.epoch,
        ...(typeof body.failed === 'string' ? { failed: body.failed } : {}),
        reason: body.reason ?? 'connect_failed',
      }),
    );
  } catch (err) {
    next(err);
  }
}

export async function fallback(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<FallbackBody, RoomParams>(res);
    sendStored(
      res,
      await fallbackTransport(getAccount(res.locals).id, params.uuid, params.kind, params.id, {
        requestId: body.request_id,
        epoch: body.observed_epoch,
        reason: body.reason,
      }),
    );
  } catch (err) {
    next(err);
  }
}
