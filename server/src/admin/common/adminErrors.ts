import type { ErrorRequestHandler, RequestHandler } from 'express';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { errorResponse } from '../../utils/response';
import { auditFailure } from './audit';

export const adminNotFound: RequestHandler = (_req, res) => {
  errorResponse(res, '찾을 수 없는 경로입니다.', 404, { code: 'NOT_FOUND' });
};

interface BodyErr {
  type?: string;
}

/** 관리자 오류 처리기: 감사 로그에 한 줄을 남기고(별도 트랜잭션) 응답한다. 본문 문장은 중앙에서만 정한다 */
export const adminErrorHandler: ErrorRequestHandler = async (err: unknown, req, res, next) => {
  if (res.headersSent) {
    next(err);
    return;
  }
  await auditFailure(req, res, err);
  if (err instanceof AppError) {
    const retry = err.extra?.retry_after_sec;
    if (err.status === 429 && typeof retry === 'number') res.setHeader('Retry-After', String(retry));
    errorResponse(res, err.message, err.status, { code: err.code ?? 'ERROR', ...err.extra });
    return;
  }
  const e = err as BodyErr | null;
  if (e && (e.type === 'entity.parse.failed' || e.type === 'entity.too.large')) {
    errorResponse(res, '요청 본문을 읽을 수 없습니다.', 400, { code: 'VALIDATION' });
    return;
  }
  logger.error({ err }, 'admin unhandled error');
  errorResponse(res, '서버 내부 오류가 발생했습니다.', 500, { code: 'INTERNAL' });
};
