import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { AppError } from '../../utils/AppError';
import { successResponse } from '../../utils/response';
import { clearConsentCache } from './consentGate';
import { policyPageHtml } from './policyPages';
import * as service from './policyService';
import type { ConsentBody, PageParams } from './policyValidation';
import { isPolicyDoc, resolveLang } from './policyVersions';

export function index(_req: Request, res: Response, next: NextFunction): void {
  try {
    successResponse(res, service.policyIndex());
  } catch (err) {
    next(err);
  }
}

export function page(_req: Request, res: Response, next: NextFunction): void {
  try {
    const { params } = getValidated<unknown, PageParams>(res);
    if (!isPolicyDoc(params.doc)) throw new AppError(404, '찾을 수 없는 문서입니다.', 'NOT_FOUND');
    const html = policyPageHtml(resolveLang(params.lang), params.doc);
    if (!html) throw new AppError(404, '찾을 수 없는 문서입니다.', 'NOT_FOUND');
    res.setHeader('Cache-Control', 'public, max-age=300');
    res.setHeader('Content-Security-Policy', "default-src 'none'; style-src 'unsafe-inline'; img-src data:");
    res.type('html').send(html);
  } catch (err) {
    next(err);
  }
}

export async function status(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.consentStatus(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function agree(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<ConsentBody>(res);
    const account = getAccount(res.locals);
    const result = await service.agree(account.id, body);
    clearConsentCache(account.uuid);
    successResponse(res, result);
  } catch (err) {
    next(err);
  }
}
