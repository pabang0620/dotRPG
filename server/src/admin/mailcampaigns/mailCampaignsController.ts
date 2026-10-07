import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import { sendAction } from '../common/adminResponse';
import * as service from './mailCampaignsService';
import type { ApproveBody, CancelBody, CreateBody, DeliveriesQuery, ListQuery } from './mailCampaignsValidation';

type P = { uuid: string };

export async function create(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CreateBody>(res);
    sendAction(res, await service.createCampaign(getAdmin(res.locals), req.ip ?? '', body));
  } catch (err) {
    next(err);
  }
}

export async function list(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { query } = getValidated<unknown, unknown, ListQuery>(res);
    successResponse(res, await service.listCampaigns(getAdmin(res.locals), req.ip ?? '', query));
  } catch (err) {
    next(err);
  }
}

export async function show(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, P>(res);
    successResponse(res, await service.showCampaign(getAdmin(res.locals), req.ip ?? '', params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function approve(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<ApproveBody, P>(res);
    sendAction(res, await service.approveCampaign(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, body } = getValidated<CancelBody, P>(res);
    sendAction(res, await service.cancelCampaign(getAdmin(res.locals), req.ip ?? '', params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function deliveries(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query } = getValidated<unknown, P, DeliveriesQuery>(res);
    successResponse(res, await service.campaignDeliveries(getAdmin(res.locals), req.ip ?? '', params.uuid, query));
  } catch (err) {
    next(err);
  }
}
