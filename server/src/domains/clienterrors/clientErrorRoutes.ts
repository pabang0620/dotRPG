import { Router, type RequestHandler } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { MINUTE, rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { AppError } from '../../utils/AppError';
import * as controller from './clientErrorController';
import { CLIENT_ERROR_MAX_BYTES, clientErrorBody } from './clientErrorValidation';

/** 본문이 4KB를 넘으면 읽지 않고 거절한다(Content-Length 기준, 없으면 파싱된 크기로 다시 본다) */
const sizeGuard: RequestHandler = (req, _res, next) => {
  const declared = Number(req.header('content-length') ?? 0);
  const parsed = req.body === undefined ? 0 : Buffer.byteLength(JSON.stringify(req.body));
  if (declared > CLIENT_ERROR_MAX_BYTES || parsed > CLIENT_ERROR_MAX_BYTES) {
    next(new AppError(413, '보고 내용이 너무 깁니다.', 'PAYLOAD_TOO_LARGE'));
    return;
  }
  next();
};

/**
 * 15단계 G8: 클라이언트 예외 보고. 버전이 달라도 받는다(옛 클라이언트의 오류가 더 필요하다). 계정 단위 분당 한도.
 * 응답 204(본문 없음).
 */
export function createClientErrorRouter(): Router {
  const r = Router();
  const key = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);
  r.post(
    '/client-errors',
    requireAuth,
    rateLimit({ name: 'client-errors', limit: (c) => c.load.clientErrorPerMin, windowMs: MINUTE, key }),
    sizeGuard,
    validate({ body: clientErrorBody }),
    controller.report,
  );
  return r;
}
