import type { Queryable } from '../../db/pool';

export async function recentRuns(db: Queryable, perJob: number) {
  const r = await db.query<{
    job: string; status: string; started_by: string; started_at: Date; finished_at: Date | null; rows_affected: number; error: string | null; detail: unknown;
  }>(
    `SELECT job, status, started_by, started_at, finished_at, rows_affected, error, detail FROM (
       SELECT j.*, row_number() OVER (PARTITION BY job ORDER BY started_at DESC) AS rn FROM job_runs j) t
      WHERE rn <= $1 ORDER BY job, started_at DESC`,
    [perJob],
  );
  return r.rows;
}

export async function lastOk(db: Queryable) {
  const r = await db.query<{ job: string; at: Date }>(`SELECT job, max(finished_at) AS at FROM job_runs WHERE status = 'ok' GROUP BY job`);
  return new Map(r.rows.map((x) => [x.job, x.at] as const));
}

export async function lastIntegrity(db: Queryable) {
  const r = await db.query<{ started_at: Date; detail: unknown }>(
    `SELECT started_at, detail FROM job_runs WHERE job = 'integrity-nightly' AND status = 'ok' ORDER BY started_at DESC LIMIT 1`,
  );
  return r.rows[0] ?? null;
}
