import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './playersService';
import type { DevCreateBody, LedgerQuery, NoteBody } from './playersValidation';

type P = { uuid: string };

export async function find(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, { q: string }>(res);
    successResponse(res, await service.findAccounts(query.q));
  } catch (err) {
    next(err);
  }
}

export async function account(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.accountDetail(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function character(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.characterDetail(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function ledger(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, P, LedgerQuery>(res);
    const r = await service.ledger(getAdmin(res.locals), req.ip ?? '', params.uuid, query);
    successResponse(res, r.data, '', 200, { next_before: r.next_before });
  } catch (err) {
    next(err);
  }
}

export async function note(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<NoteBody, P>(res);
    sendAction(res, await service.addNote(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function kick(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<{ request_id: string }, P>(res);
    sendAction(res, await service.kick(getAdmin(res.locals), req.ip ?? '', params.uuid, body.request_id));
  } catch (err) {
    next(err);
  }
}

export async function devCreate(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<DevCreateBody>(res);
    sendAction(res, await service.createDevAccount(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}

export async function devReset(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<{ request_id: string }, P>(res);
    sendAction(res, await service.resetDevPassword(getAdmin(res.locals), req.ip ?? '', params.uuid, body.request_id));
  } catch (err) {
    next(err);
  }
}
