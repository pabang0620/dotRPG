import type { RequestHandler, Response } from 'express';
import { z } from 'zod';
import { AppError } from '../utils/AppError';

export interface FieldError {
  path: string;
  message: string;
  code?: string;
}

export interface Validated {
  body?: unknown;
  params?: unknown;
  query?: unknown;
}

interface Schemas {
  body?: z.ZodType;
  params?: z.ZodType;
  query?: z.ZodType;
}

/** zod 이슈를 { path, message, code? } 로 바꾼다. 허용되지 않는 키는 키마다 한 줄. */
export function toFieldErrors(error: z.ZodError, prefix = ''): FieldError[] {
  const out: FieldError[] = [];
  for (const issue of error.issues) {
    const base = [...(prefix ? [prefix] : []), ...issue.path.map(String)];
    if (issue.code === 'unrecognized_keys') {
      for (const key of issue.keys) {
        out.push({ path: [...base, key].join('.'), message: '허용되지 않는 필드' });
      }
      continue;
    }
    const params = (issue as { params?: { reason?: string } }).params;
    const field: FieldError = { path: base.join('.'), message: issue.message };
    if (params?.reason) field.code = params.reason;
    out.push(field);
  }
  return out;
}

export function validationError(fields: FieldError[]): AppError {
  return new AppError(400, '입력값이 올바르지 않습니다.', 'VALIDATION', { fields });
}

/** 설계 문서 0.1: 검증 실패는 400 VALIDATION, errors.fields = [{path, message}] */
export const validate =
  (schemas: Schemas): RequestHandler =>
  (req, res, next) => {
    const fields: FieldError[] = [];
    const validated: Validated = {};
    if (schemas.body) {
      const r = schemas.body.safeParse(req.body ?? {});
      if (r.success) validated.body = r.data;
      else fields.push(...toFieldErrors(r.error));
    }
    if (schemas.params) {
      const r = schemas.params.safeParse(req.params);
      if (r.success) validated.params = r.data;
      else fields.push(...toFieldErrors(r.error));
    }
    if (schemas.query) {
      const r = schemas.query.safeParse(req.query);
      if (r.success) validated.query = r.data;
      else fields.push(...toFieldErrors(r.error));
    }
    if (fields.length > 0) {
      next(validationError(fields));
      return;
    }
    res.locals.validated = validated;
    next();
  };

export function getValidated<B = unknown, P = unknown, Q = unknown>(res: Response): { body: B; params: P; query: Q } {
  const v = res.locals.validated as Validated | undefined;
  return { body: v?.body as B, params: v?.params as P, query: v?.query as Q };
}
