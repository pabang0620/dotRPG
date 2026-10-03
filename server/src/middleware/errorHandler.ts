import type { ErrorRequestHandler, RequestHandler } from 'express';
import { AppError } from '../utils/AppError';
import { errorResponse } from '../utils/response';
import { logger } from '../utils/logger';

export const notFoundHandler: RequestHandler = (_req, res) => {
  res.locals.errCode = 'NOT_FOUND';
  errorResponse(res, '찾을 수 없는 경로입니다.', 404, { code: 'NOT_FOUND' });
};

interface HttpishError {
  type?: string;
  status?: number;
}

export const errorHandler: ErrorRequestHandler = (err: unknown, _req, res, next) => {
  if (res.headersSent) {
    next(err);
    return;
  }
  if (err instanceof AppError) {
    res.locals.errCode = err.code ?? 'ERROR';
    const retry = err.extra?.retry_after_sec;
    if ((err.status === 429 || err.status === 503) && typeof retry === 'number') {
      res.setHeader('Retry-After', String(retry));
    }
    errorResponse(res, err.message, err.status, { code: err.code ?? 'ERROR', ...err.extra });
    return;
  }
  // body-parser 에러 (본문 크기, JSON 문법)
  const e = err as HttpishError;
  if (e && e.type === 'entity.too.large') {
    errorResponse(res, '요청 본문이 너무 큽니다.', 413, { code: 'PAYLOAD_TOO_LARGE' });
    return;
  }
  if (e && (e.type === 'entity.parse.failed' || e.type === 'encoding.unsupported')) {
    errorResponse(res, '요청 본문을 읽을 수 없습니다.', 400, {
      code: 'VALIDATION',
      fields: [{ path: '', message: '올바른 JSON이 아닙니다' }],
    });
    return;
  }
  logger.error({ err }, 'unhandled error');
  errorResponse(res, '서버 내부 오류가 발생했습니다.', 500, { code: 'INTERNAL' });
};
