// 정책 동의 상태와 기록. 필요한 버전(policyVersions.REQUIRED)과 정확히 같은 것만 받는다.
import { getPool } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import * as repo from './policyRepository';
import { DOCS, LANGUAGES, REQUIRED } from './policyVersions';
import type { ConsentBody } from './policyValidation';

export interface ConsentStatus {
  required: { id: string; version: string; agreed: boolean }[];
  needs: boolean;
}

export function statusFrom(rows: repo.ConsentRow[]): ConsentStatus {
  const has = new Set(rows.map((r) => `${r.doc_id}@${r.version}`));
  const required = REQUIRED.map((r) => ({ id: r.id, version: r.version, agreed: has.has(`${r.id}@${r.version}`) }));
  return { required, needs: required.some((r) => !r.agreed) };
}

export function policyIndex() {
  return { required: REQUIRED.map((r) => ({ id: r.id, version: r.version })), docs: [...DOCS], languages: [...LANGUAGES] };
}

export async function consentStatus(accountId: number): Promise<ConsentStatus> {
  return statusFrom(await repo.agreedOf(getPool(), accountId));
}

export async function agree(accountId: number, body: ConsentBody): Promise<ConsentStatus> {
  const bad = body.agree.filter((a) => !REQUIRED.some((r) => r.id === a.id && r.version === a.version));
  if (bad.length > 0) {
    throw new AppError(422, '동의할 수 없는 문서 버전입니다.', 'VALIDATION', {
      fields: bad.map((b) => ({ path: 'agree', message: `${b.id}@${b.version}` })),
    });
  }
  await repo.insertConsents(getPool(), accountId, body.agree.map((a) => ({ doc_id: a.id, version: a.version })));
  return consentStatus(accountId);
}
