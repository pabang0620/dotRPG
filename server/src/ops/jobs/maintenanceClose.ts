// `maintenance-close`(1분): 종료 예정 시각이 지난 점검 창을 state='ended'로 닫는다(closed_by NULL = 자동 종료)
import { getPool } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { loadMaintenanceFromDb } from '../maintenanceState';
import type { JobResult } from '../jobRunner';

export async function maintenanceCloseJob(): Promise<JobResult> {
  const r = await getPool().query(
    `UPDATE maintenance_windows SET state = 'ended', closed_at = now() WHERE state = 'scheduled' AND ends_at <= $1`,
    [getNow()],
  );
  await loadMaintenanceFromDb();
  return { rows: r.rowCount ?? 0, detail: {} };
}
