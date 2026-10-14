import { recordRatesPublication } from '../../domains/policies/ratesPublicationService';
import type { JobResult } from '../jobRunner';

/** 1시간마다. 지금 공개 중인 확률표의 지문을 rates_publications 에 upsert 한다(같은 지문이면 last_seen_at 만 갱신) */
export async function ratesPublicationJob(): Promise<JobResult> {
  const r = await recordRatesPublication();
  return { rows: r.created ? 1 : 0, detail: { fingerprint: r.fingerprint.slice(0, 12), created: r.created } };
}
