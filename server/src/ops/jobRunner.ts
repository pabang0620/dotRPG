// 프로세스 안 작은 스케줄러(phase7_ops.md 6.1). 작업마다 pg_try_advisory_lock으로 한 곳에서만 돌고 job_runs에 기록한다.
import type { PoolClient } from 'pg';
import { getConfig } from '../config/env';
import { getPool } from '../db/pool';
import { getNow } from '../utils/clock';
import { logger } from '../utils/logger';
import { nextKstDailyAt } from '../utils/resetBoundaries';
import { metrics } from './metrics';

export interface JobCtx {
  /** 수동 실행 옵션(정합성 점검의 전체 점검 등) */
  opts: { full?: boolean };
  /** 정지 신호(종료 중)나 최대 실행 시간(JOB_MAX_SECONDS)이 지났으면 true: 배치 사이에서 확인한다 */
  shouldStop(): boolean;
}

export interface JobResult {
  rows: number;
  detail: Record<string, unknown>;
  /** 정합성 점검처럼 "결과가 이상"이어도 작업 자체는 ok인 경우를 위해 감시자가 detail을 본다 */
}

export type JobSchedule =
  | { kind: 'every'; minutes: number }
  | { kind: 'hourly'; minute: number }
  | { kind: 'daily_kst'; hour: number; minute: number };

export interface JobDef {
  name: string;
  schedule: JobSchedule;
  run(ctx: JobCtx): Promise<JobResult>;
}

export type RunOutcome =
  | { status: 'ok' | 'failed'; runId: number; rows: number; detail: Record<string, unknown>; error?: string }
  | { status: 'skipped'; reason: 'running' | 'locked' };

const registry = new Map<string, JobDef>();
const runningHere = new Set<string>();
let stopping = false;

export function registerJob(def: JobDef): void {
  registry.set(def.name, def);
}
export const getJob = (name: string): JobDef | undefined => registry.get(name);
export const listJobs = (): JobDef[] => [...registry.values()];
export const isJobRunning = (name: string): boolean => runningHere.has(name);
export const runningJobCount = (): number => runningHere.size;

/** 작업 한 번 실행. 같은 작업이 이 프로세스나 다른 프로세스에서 돌고 있으면 기록 없이 건너뛴다 */
export async function runJob(
  name: string,
  startedBy: 'schedule' | 'manual' | 'startup',
  triggeredBy: number | null = null,
  opts: { full?: boolean } = {},
): Promise<RunOutcome> {
  const def = registry.get(name);
  if (!def) throw new Error(`알 수 없는 작업: ${name}`);
  if (runningHere.has(name)) return { status: 'skipped', reason: 'running' };
  runningHere.add(name);
  let lockClient: PoolClient | null = null;
  try {
    lockClient = await getPool().connect();
    const got = await lockClient.query<{ ok: boolean }>(`SELECT pg_try_advisory_lock(hashtext('job:' || $1)) AS ok`, [name]);
    if (!got.rows[0]?.ok) return { status: 'skipped', reason: 'locked' };
    const ins = await getPool().query<{ id: string }>(
      `INSERT INTO job_runs (job, started_by, triggered_by) VALUES ($1, $2, $3) RETURNING id`,
      [name, startedBy, triggeredBy],
    );
    const runId = Number((ins.rows[0] as { id: string }).id);
    const t0 = Date.now();
    const deadline = t0 + getConfig().purge.jobMaxSeconds * 1000;
    try {
      const res = await def.run({ opts, shouldStop: () => stopping || Date.now() > deadline });
      const detail = { ...res.detail, ms: Date.now() - t0 };
      await getPool().query(
        `UPDATE job_runs SET status = 'ok', finished_at = now(), rows_affected = $2, detail = $3::jsonb WHERE id = $1`,
        [runId, res.rows, JSON.stringify(detail)],
      );
      metrics.recordTick(`job:${name}`, Date.now() - t0);
      return { status: 'ok', runId, rows: res.rows, detail };
    } catch (err) {
      const message = (err as Error).message.slice(0, 900);
      logger.error({ err, job: name }, 'job.failed');
      await getPool().query(
        `UPDATE job_runs SET status = 'failed', finished_at = now(), error = $2 WHERE id = $1`,
        [runId, message],
      );
      return { status: 'failed', runId, rows: 0, detail: {}, error: message };
    }
  } finally {
    if (lockClient) {
      await lockClient.query(`SELECT pg_advisory_unlock(hashtext('job:' || $1))`, [name]).catch(() => undefined);
      lockClient.release();
    }
    runningHere.delete(name);
  }
}

/** 기동할 때 running으로 남은 행(서버가 죽어 끝내지 못한 작업)을 failed(interrupted)로 닫는다 */
export async function closeInterruptedRuns(): Promise<number> {
  const r = await getPool().query(
    `UPDATE job_runs SET status = 'failed', finished_at = now(), error = 'interrupted' WHERE status = 'running'`,
  );
  return r.rowCount ?? 0;
}

/** 다음 실행 시각 */
export function nextRunAt(s: JobSchedule, from: Date): Date {
  if (s.kind === 'every') return new Date(from.getTime() + s.minutes * 60_000);
  if (s.kind === 'hourly') {
    const t = new Date(from);
    t.setUTCMinutes(s.minute, 0, 0);
    if (t.getTime() <= from.getTime()) t.setUTCHours(t.getUTCHours() + 1);
    return t;
  }
  return nextKstDailyAt(from, s.hour, s.minute);
}

/** 종료 신호: 진행 중인 작업은 현재 배치만 끝내고 멈추고, 새 작업은 시작하지 않는다 */
export function requestJobStop(): void {
  stopping = true;
}

/** 테스트 전용: 정지 신호를 되돌린다 */
export function resetJobStop(): void {
  stopping = false;
}

let loop: NodeJS.Timeout | null = null;

/** 스케줄 루프 시작. 모든 작업은 시작 시각에 0~60초 무작위 지연을 둔다(여러 대가 될 때 겹침 방지). 돌려주는 함수는 정지 */
export function startJobRunner(jitterMaxMs = 60_000): () => Promise<void> {
  stopping = false;
  const due = new Map<string, number>();
  const arm = (def: JobDef, from: Date): void => {
    due.set(def.name, nextRunAt(def.schedule, from).getTime() + Math.floor(Math.random() * jitterMaxMs));
  };
  for (const def of registry.values()) arm(def, getNow());
  let busy = false;
  loop = setInterval(() => {
    if (busy || stopping) return;
    const now = Date.now();
    const ready = [...registry.values()].filter((d) => (due.get(d.name) ?? Infinity) <= now);
    if (ready.length === 0) return;
    busy = true;
    (async () => {
      for (const def of ready) {
        if (stopping) break;
        arm(def, new Date());
        await runJob(def.name, 'schedule');
      }
    })()
      .catch((err: unknown) => logger.error({ err }, 'job loop failed'))
      .finally(() => {
        busy = false;
      });
  }, 15_000);
  loop.unref();
  return async () => {
    stopping = true;
    if (loop) clearInterval(loop);
    loop = null;
    const end = Date.now() + 10_000;
    while (runningHere.size > 0 && Date.now() < end) await new Promise((r) => setTimeout(r, 50));
  };
}
