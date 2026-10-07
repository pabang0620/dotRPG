// 9단계 데이터: 소득 상한 표(income_caps.json, Tools/balance/theory_income.py가 만든다), 운영 예약어(reserved_names.json),
// 전직 시련 값(careers.json의 trials, Unity 내보내기가 채운다). 모두 파일이 없거나 깨져도 서버가 죽지 않게 읽는다.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { getConfig } from '../config/env';
import { logger } from '../utils/logger';

const bandSchema = z.looseObject({
  minLevel: z.number().int().min(1),
  maxLevel: z.number().int().min(1),
  perHour: z.looseObject({ xp: z.number(), goldEq: z.number(), ore: z.number(), essence: z.number(), epicPlus: z.number() }),
  perDay: z.looseObject({ dungeonXp: z.number(), dungeonGoldEq: z.number(), raidMidXp: z.number(), raidMidGoldEq: z.number() }),
  perWeek: z.looseObject({ raidFinalXp: z.number(), raidFinalGoldEq: z.number() }),
});

const incomeCapsSchema = z.looseObject({
  schema: z.literal(1),
  killsPerMinute: z.number().positive(),
  bands: z.array(bandSchema).min(1),
  uniquePlus: z.looseObject({ perDay: z.number(), perHour: z.number() }),
});
export type IncomeCaps = z.infer<typeof incomeCapsSchema>;
export type IncomeBand = z.infer<typeof bandSchema>;

const reservedSchema = z.looseObject({
  substring: z.array(z.string()).default([]),
  token: z.array(z.string()).default([]),
  allow: z.array(z.string()).default([]),
});
export type ReservedNames = z.infer<typeof reservedSchema>;

const trialsSchema = z.record(
  z.string(),
  z.looseObject({ minSeconds: z.number().min(0), requiredNodes: z.array(z.string()).default([]) }),
);
export type CareerTrials = z.infer<typeof trialsSchema>;

interface Cached<T> {
  mtimeMs: number;
  checkedAt: number;
  value: T;
}

const CHECK_EVERY_MS = 5000;
const caches = new Map<string, Cached<unknown>>();

/** 파일을 읽어 검증한다. 수정 시각이 바뀌면 다시 읽는다(5초에 한 번만 확인). 실패하면 fallback */
function readJson<T>(file: string, parse: (raw: unknown) => T, fallback: T): T {
  const full = path.join(getConfig().gameDataDir, file);
  const hit = caches.get(full) as Cached<T> | undefined;
  const now = Date.now();
  if (hit && now - hit.checkedAt < CHECK_EVERY_MS) return hit.value;
  try {
    const st = fs.statSync(full);
    if (hit && hit.mtimeMs === st.mtimeMs) {
      hit.checkedAt = now;
      return hit.value;
    }
    const value = parse(JSON.parse(fs.readFileSync(full, 'utf8')));
    caches.set(full, { mtimeMs: st.mtimeMs, checkedAt: now, value });
    return value;
  } catch (err) {
    if ((err as { code?: string }).code !== 'ENOENT') logger.warn({ err, file }, 'anti_abuse.data_unreadable');
    caches.set(full, { mtimeMs: -1, checkedAt: now, value: fallback });
    return fallback;
  }
}

/** 테스트용: 캐시를 비운다 */
export function resetAntiAbuseDataCache(): void {
  caches.clear();
}

export function getIncomeCaps(): IncomeCaps | null {
  return readJson<IncomeCaps | null>('income_caps.json', (raw) => incomeCapsSchema.parse(raw), null);
}

export function getReservedNames(): ReservedNames {
  return readJson<ReservedNames>('reserved_names.json', (raw) => reservedSchema.parse(raw), { substring: [], token: [], allow: [] });
}

/** careers.json의 trials. 항목이 없으면 null(서버는 AWAKEN_TRIAL_MIN_SECONDS와 "필수 노드 검사 건너뜀"으로 동작) */
export function getCareerTrials(): CareerTrials | null {
  return readJson<CareerTrials | null>(
    'careers.json',
    (raw) => {
      const t = (raw as { trials?: unknown } | null)?.trials;
      return t === undefined ? null : trialsSchema.parse(t);
    },
    null,
  );
}
