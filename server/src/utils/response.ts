import type { Response } from 'express';

export interface ApiMeta {
  [key: string]: unknown;
}

export function successResponse<T>(
  res: Response,
  data: T,
  message = '',
  statusCode = 200,
  meta?: ApiMeta,
): Response {
  const body: { success: true; message: string; data: T; meta?: ApiMeta } = {
    success: true,
    message,
    data,
  };
  if (meta) body.meta = meta;
  return res.status(statusCode).json(body);
}

export function errorResponse(
  res: Response,
  message: string,
  statusCode: number,
  errors?: Record<string, unknown>,
): Response {
  const body: { success: false; message: string; errors?: Record<string, unknown> } = {
    success: false,
    message,
  };
  if (errors) body.errors = errors;
  return res.status(statusCode).json(body);
}

/** 멱등성 재전송: 처음 응답 본문을 같은 상태 코드로 돌려준다. */
export function replayResponse(res: Response, statusCode: number, storedBody: unknown): Response {
  res.setHeader('Idempotent-Replay', 'true');
  return res.status(statusCode).json(storedBody);
}
