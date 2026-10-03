import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import * as service from './mailService';
import type { ClaimAllBody, ClaimBody, ListQuery, MailParams, SummaryQuery } from './mailValidation';

type CharP = { uuid: string };

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, CharP, ListQuery>(res);
    const r = await service.listMail(getAccount(res.locals).id, params.uuid, query);
    successResponse(res, r.data, '', 200, r.meta);
  } catch (err) {
    next(err);
  }
}

export async function summary(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, CharP, SummaryQuery>(res);
    successResponse(res, await service.mailSummary(getAccount(res.locals).id, params.uuid, query));
  } catch (err) {
    next(err);
  }
}

export async function claim(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimBody, MailParams>(res);
    sendStored(res, await service.claimMail(getAccount(res.locals).id, params.uuid, params.mail_id, body));
  } catch (err) {
    next(err);
  }
}

export async function claimAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ClaimAllBody, CharP>(res);
    sendStored(res, await service.claimAll(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
