// 확률표 공개 기록: 서버 시작 때 1회와 1시간마다(ops/jobs) 지금 공개 중인 확률 문서의 지문을 남긴다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { buildRatesPublication } from './ratesDisclosure';
import * as repo from './ratesPublicationRepository';

export async function recordRatesPublication(): Promise<{ fingerprint: string; created: boolean }> {
  const doc = buildRatesPublication(getConfig().gameDataDir);
  const created = await repo.upsertPublication(getPool(), doc.fingerprint, doc.versions, doc.content);
  return { fingerprint: doc.fingerprint, created };
}
