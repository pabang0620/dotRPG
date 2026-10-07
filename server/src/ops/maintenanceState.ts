// 점검 상태(phase7_ops.md 4절): maintenance_windows의 열린 행 하나와 서버 시각으로 단계를 계산한다.
// 서버는 MAINT_POLL_SECONDS마다 DB를 읽어 메모리에 두고, 같은 프로세스의 관리자 변경은 즉시 반영한다.
import { randomInt } from 'node:crypto';
import { getConfig } from '../config/env';
import { query } from '../db/pool';
import { getNow } from '../utils/clock';
import { logger } from '../utils/logger';

export interface MaintWindow {
  uuid: string;
  notice: string;
  blockLoginAt: Date;
  startsAt: Date;
  endsAt: Date;
}

export type MaintPhase = 'none' | 'scheduled' | 'pre_block' | 'active';

let current: MaintWindow | null = null;


export function setMaintenance(w: MaintWindow | null): void {
  current = w;
}

export const getMaintenance = (): MaintWindow | null => current;

/** 열린 창이 있어도 종료 예정 시각(ends_at)이 지났으면 점검이 풀린 것으로 본다(maintenance-close 작업이 행을 닫는다) */
export function maintPhase(now: Date = getNow()): MaintPhase {
  const w = current;
  if (!w) return 'none';
  const t = now.getTime();
  if (t >= w.endsAt.getTime()) return 'none';
  if (t >= w.startsAt.getTime()) return 'active';
  if (t >= w.blockLoginAt.getTime()) return 'pre_block';
  return 'scheduled';
}

/** bye(MAINTENANCE)의 retry_after_ms: 남은 시간(30초~10분)에 0~30초 무작위를 더한다(재접속이 한꺼번에 몰리지 않게) */
export function maintRetryAfterMs(now: Date = getNow()): number {
  const left = current ? current.endsAt.getTime() - now.getTime() : 0;
  return Math.max(30_000, Math.min(600_000, left)) + randomInt(0, 30_001);
}

export interface MaintView {
  phase: Exclude<MaintPhase, 'none'>;
  block_login_at: string;
  starts_at: string;
  ends_at: string;
  notice: string;
}

/** /meta와 503 응답에 싣는 모양(없으면 null) */
export function maintView(now: Date = getNow()): MaintView | null {
  const phase = maintPhase(now);
  const w = current;
  if (phase === 'none' || !w) return null;
  return {
    phase,
    block_login_at: w.blockLoginAt.toISOString(),
    starts_at: w.startsAt.toISOString(),
    ends_at: w.endsAt.toISOString(),
    notice: w.notice,
  };
}

interface Row {
  uuid: string;
  notice: string;
  block_login_at: Date;
  starts_at: Date;
  ends_at: Date;
}

export const toWindow = (r: Row): MaintWindow => ({
  uuid: r.uuid,
  notice: r.notice,
  blockLoginAt: r.block_login_at,
  startsAt: r.starts_at,
  endsAt: r.ends_at,
});

/** DB의 열린(scheduled) 창을 읽어 메모리에 둔다 */
export async function loadMaintenanceFromDb(): Promise<void> {
  const r = await query<Row>(
    `SELECT uuid, notice, block_login_at, starts_at, ends_at FROM maintenance_windows WHERE state = 'scheduled' LIMIT 1`,
  );
  setMaintenance(r.rows[0] ? toWindow(r.rows[0]) : null);
}

export function startMaintenancePolling(): () => void {
  const run = (): void => {
    loadMaintenanceFromDb().catch((err: unknown) => logger.error({ err }, 'maintenance poll failed'));
  };
  run();
  const t = setInterval(run, getConfig().maint.pollSeconds * 1000);
  t.unref();
  return () => clearInterval(t);
}
