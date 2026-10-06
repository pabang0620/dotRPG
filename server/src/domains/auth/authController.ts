import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import type { CredentialsBody, RefreshBody, SteamBody } from './authValidation';
import * as service from './authService';
import type { AccessMeta } from './authService';

/** 접속 신호(기기·클라이언트 버전). IP는 service가 req.ip로 따로 받는다 */
function meta(req: Request, device: AccessMeta['device']): AccessMeta {
  return { device, clientVersion: req.header('x-client-version') };
}

export async function register(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CredentialsBody>(res);
    const data = await service.register(body.login_id, body.password, req.ip ?? 'unknown', meta(req, body.device));
    successResponse(res, data, '', 201);
  } catch (err) {
    next(err);
  }
}

export async function login(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CredentialsBody>(res);
    successResponse(res, await service.login(body.login_id, body.password, req.ip ?? 'unknown', meta(req, body.device)));
  } catch (err) {
    next(err);
  }
}

export async function refresh(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<RefreshBody>(res);
    successResponse(res, await service.refresh(body.refresh_token, meta(req, body.device), req.ip ?? ''));
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

export async function steamLogin(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<SteamBody>(res);
    const data = await service.steamLogin(body.ticket, meta(req, body.device), req.ip ?? '');
    successResponse(res, data, '', data.created ? 201 : 200);
  } catch (err) {
    next(err);
  }
}

export async function steamLink(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<SteamBody>(res);
    successResponse(res, await service.steamLink(getAccount(res.locals).id, body.ticket));
  } catch (err) {
    next(err);
  }
}
