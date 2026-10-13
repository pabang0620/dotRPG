import type { Response } from 'express';
import { langOf, translate } from './i18n';

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
    message: translate(langOf(res), message),
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
  const lang = langOf(res);
  const body: { success: false; message: string; errors?: Record<string, unknown> } = {
    success: false,
    message: translate(lang, message),
  };
  if (errors) body.errors = translateFields(lang, errors);
  return res.status(statusCode).json(body);
}

/** 검증 실패 항목(fields[].message)도 같은 표로 바꾼다. */
function translateFields(lang: string | undefined, errors: Record<string, unknown>): Record<string, unknown> {
  if (!lang || !Array.isArray(errors.fields)) return errors;
  const fields = (errors.fields as unknown[]).map((f) =>
    f && typeof f === 'object' && typeof (f as { message?: unknown }).message === 'string'
      ? { ...(f as Record<string, unknown>), message: translate(lang, (f as { message: string }).message) }
      : f,
  );
  return { ...errors, fields };
}

/** 멱등성 재전송: 처음 응답 본문을 같은 상태 코드로 돌려준다. */
export function replayResponse(res: Response, statusCode: number, storedBody: unknown): Response {
  res.setHeader('Idempotent-Replay', 'true');
  return res.status(statusCode).json(storedBody);
}
