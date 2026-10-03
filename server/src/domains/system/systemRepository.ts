import { query } from '../../db/pool';

export async function pingDb(): Promise<boolean> {
  try {
    await Promise.race([
      query('SELECT 1'),
      new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), 2000).unref()),
    ]);
    return true;
  } catch {
    return false;
  }
}

/** 적용된 마이그레이션 수(schema_migrations 행 수) */
export async function appliedMigrationCount(): Promise<number> {
  const r = await query<{ n: string }>('SELECT count(*) AS n FROM schema_migrations');
  return Number((r.rows[0] as { n: string }).n);
}
