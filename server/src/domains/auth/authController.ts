import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import type { CredentialsBody, RefreshBody } from './authValidation';
import * as service from './authService';

export async function register(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CredentialsBody>(res);
    const data = await service.register(body.login_id, body.password, req.ip ?? 'unknown');
    successResponse(res, data, '', 201);
  } catch (err) {
    next(err);
  }
}

export async function login(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CredentialsBody>(res);
    successResponse(res, await service.login(body.login_id, body.password, req.ip ?? 'unknown'));
  } catch (err) {
    next(err);
  }
}

export async function refresh(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<RefreshBody>(res);
    successResponse(res, await service.refresh(body.refresh_token));
  } catch (err) {
    next(err);
  }
}

export async function logout(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<RefreshBody>(res);
    successResponse(res, await service.logout(body.refresh_token));
  } catch (err) {
    next(err);
  }
}

export async function me(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.getMe(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}
