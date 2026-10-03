import type { NextFunction, Request, Response } from 'express';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { getAdmin } from '../common/adminAuth';
import * as service from './adminAuthService';
import type { LoginBody, PasswordBody, TotpConfirmBody } from './adminAuthValidation';

export async function login(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<LoginBody>(res);
    const note = (req.header('user-agent') ?? '').slice(0, 100) || null;
    successResponse(res, await service.login(body, req.ip ?? '', note));
  } catch (err) {
    next(err);
  }
}

export async function logout(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.logout(getAdmin(res.locals), req.ip ?? ''));
  } catch (err) {
    next(err);
  }
}

export function me(_req: Request, res: Response, next: NextFunction): void {
  try {
    successResponse(res, service.me(getAdmin(res.locals)));
  } catch (err) {
    next(err);
  }
}

export async function password(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<PasswordBody>(res);
    successResponse(res, await service.changePassword(getAdmin(res.locals), body, req.ip ?? ''));
  } catch (err) {
    next(err);
  }
}

export async function totpStart(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.totpStart(getAdmin(res.locals), req.ip ?? ''));
  } catch (err) {
    next(err);
  }
}

export async function totpConfirm(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<TotpConfirmBody>(res);
    successResponse(res, await service.totpConfirm(getAdmin(res.locals), body, req.ip ?? ''));
  } catch (err) {
    next(err);
  }
}
