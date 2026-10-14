import { getPool } from '../../db/pool';
import * as repo from '../../domains/policies/ratesPublicationRepository';
import { AppError } from '../../utils/AppError';
import type { ListQueryT } from './ratesAdminValidation';

/** 최신순 목록(content 제외) */
export async function list(q: ListQueryT) {
  const rows = await repo.listPublications(getPool(), q.limit, q.before ? Number(q.before) : null);
  const page = rows.slice(0, q.limit);
  return { items: page, next_before: rows.length > q.limit ? (page[page.length - 1] as { id: string }).id : null };
}

/** 단건(content 포함) */
export async function show(id: string) {
  const row = await repo.getPublication(getPool(), Number(id));
  if (!row) throw new AppError(404, '찾을 수 없는 확률표 기록입니다.', 'NOT_FOUND');
  return row;
}
