// MN1~MN7: 점검 창 예약·취소·연장·종료, 드레인 상태, 즉시 방송. 단계 계산은 ops/maintenanceState가 한다.
import { getConfig } from '../../config/env';
import { afterCommit, getPool, isUniqueViolation } from '../../db/pool';
import { getNotifier, registry } from '../../domains/chat/realtimeNotifier';
import { consumeOrThrow } from '../../middleware/rateLimiter';
import { computeMarks } from '../../ops/maintenanceAnnouncer';
import { loadMaintenanceFromDb, maintPhase, toWindow } from '../../ops/maintenanceState';
import { inFlightCount } from '../../ops/lifecycle';
import { runningJobCount } from '../../ops/jobRunner';
import { metrics } from '../../ops/metrics';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './maintenanceRepository';
import type { BroadcastBody, ExtendBody, ScheduleBody } from './maintenanceValidation';

const view = (w: repo.WindowRow) => ({
  id: w.uuid,
  state: w.state,
  notice: w.notice,
  block_login_at: w.block_login_at.toISOString(),
  starts_at: w.starts_at.toISOString(),
  ends_at: w.ends_at.toISOString(),
  created_at: w.created_at.toISOString(),
  closed_at: w.closed_at ? w.closed_at.toISOString() : null,
});

/** MN1 */
export async function status() {
  const db = getPool();
  const [open, recent] = await Promise.all([repo.openWindow(db), repo.recent(db, 10)]);
  return { phase: maintPhase(), window: open ? view(open) : null, recent: recent.map(view), server_time: getNow().toISOString() };
}

/** MN2: 예약 또는 즉시 시작(start_in_minutes=0: block_login_at = starts_at = 지금) */
export function schedule(admin: AdminCtx, ip: string, body: ScheduleBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'maintenance.schedule',
    targetType: 'maintenance',
    requestId: body.request_id,
    params: {
      ...(body.start_in_minutes !== undefined ? { start_in_minutes: body.start_in_minutes } : {}),
      ...(body.starts_at ? { starts_at: body.starts_at } : {}),
      duration_minutes: body.duration_minutes,
      notice_length: (body.notice ?? '').length,
    },
    handler: async (client) => {
      const now = getNow();
      const cfg = getConfig().maint;
      const startsAt = body.starts_at ? new Date(body.starts_at) : new Date(now.getTime() + (body.start_in_minutes as number) * 60_000);
      if (startsAt.getTime() < now.getTime() - 1000) throw new AppError(422, '시작 시각이 이미 지났습니다.', 'BAD_TIME');
      const startsFixed = startsAt.getTime() < now.getTime() ? now : startsAt;
      const open = await repo.openWindow(client);
      if (open) throw new AppError(409, '이미 예약되었거나 진행 중인 점검이 있습니다.', 'MAINTENANCE_EXISTS', { id: open.uuid });
      const blockAt = new Date(Math.max(now.getTime(), startsFixed.getTime() - cfg.preBlockMinutes * 60_000));
      let w: repo.WindowRow;
      try {
        w = await repo.insert(client, {
          notice: body.notice ?? '',
          blockLoginAt: blockAt,
          startsAt: startsFixed,
          endsAt: new Date(startsFixed.getTime() + body.duration_minutes * 60_000),
          adminId: admin.id,
        });
      } catch (err) {
        if (isUniqueViolation(err)) throw new AppError(409, '이미 예약되었거나 진행 중인 점검이 있습니다.', 'MAINTENANCE_EXISTS');
        throw err;
      }
      afterCommit(client, () => loadMaintenanceFromDb());
      const marks = computeMarks(toWindow({ uuid: w.uuid, notice: w.notice, block_login_at: w.block_login_at, starts_at: w.starts_at, ends_at: w.ends_at }), cfg.announceMinutes);
      return { status: 201, data: { window: view(w), marks: marks.map((m) => m.at.toISOString()) }, targetUuid: w.uuid };
    },
  });
}

async function lockOpen(client: Parameters<typeof repo.lockByUuid>[0], uuid: string): Promise<repo.WindowRow> {
  const w = await repo.lockByUuid(client, uuid);
  if (!w) throw new AppError(404, '점검 창을 찾을 수 없습니다.', 'MAINTENANCE_NOT_FOUND');
  if (w.state !== 'scheduled') throw new AppError(409, '이미 닫힌 점검 창입니다.', 'MAINTENANCE_CLOSED');
  return w;
}

/** MN3: 시작 전만 취소할 수 있다 */
export function cancel(admin: AdminCtx, ip: string, uuid: string, requestId: string): Promise<ActionResult> {
  return runAdminAction({
    admin, ip, action: 'maintenance.cancel', targetType: 'maintenance', targetUuid: uuid, requestId, params: {},
    handler: async (client) => {
      const w = await lockOpen(client, uuid);
      const now = getNow();
      if (now.getTime() >= w.starts_at.getTime()) throw new AppError(409, '이미 시작한 점검은 종료(end)로 닫으세요.', 'MAINTENANCE_STARTED');
      await repo.close(client, w.id, 'cancelled', admin.id, now);
      afterCommit(client, () => loadMaintenanceFromDb());
      return { status: 200, data: { window: { id: uuid, state: 'cancelled' } } };
    },
  });
}

/** MN4 */
export function extend(admin: AdminCtx, ip: string, uuid: string, body: ExtendBody): Promise<ActionResult> {
  return runAdminAction({
    admin, ip, action: 'maintenance.extend', targetType: 'maintenance', targetUuid: uuid, requestId: body.request_id,
    params: { extend_minutes: body.extend_minutes },
    handler: async (client) => {
      const w = await lockOpen(client, uuid);
      const endsAt = new Date(w.ends_at.getTime() + body.extend_minutes * 60_000);
      await repo.extend(client, w.id, endsAt);
      afterCommit(client, () => loadMaintenanceFromDb());
      return { status: 200, data: { window: { id: uuid, ends_at: endsAt.toISOString() } } };
    },
  });
}

/** MN5: 점검 종료(즉시 none) */
export function end(admin: AdminCtx, ip: string, uuid: string, requestId: string): Promise<ActionResult> {
  return runAdminAction({
    admin, ip, action: 'maintenance.end', targetType: 'maintenance', targetUuid: uuid, requestId, params: {},
    handler: async (client) => {
      const w = await lockOpen(client, uuid);
      const now = getNow();
      if (now.getTime() < w.starts_at.getTime()) throw new AppError(409, '시작 전 점검은 취소(cancel)로 닫으세요.', 'MAINTENANCE_NOT_STARTED');
      await repo.close(client, w.id, 'ended', admin.id, now);
      afterCommit(client, () => loadMaintenanceFromDb());
      return { status: 200, data: { window: { id: uuid, state: 'ended' } } };
    },
  });
}

/** MN6: 서버를 내려도 되는가 */
export async function drain() {
  const db = getPool();
  const stale = getConfig().policy.runStaleSeconds;
  const phase = maintPhase();
  const [playing, active] = await Promise.all([repo.playingRuns(db, new Date(getNow().getTime() - stale * 1000)), repo.activeQueries(db)]);
  const ticks = { match: metrics.running.get('match') ?? 0, settle: metrics.running.get('settle') ?? 0, auction: metrics.running.get('auction') ?? 0, jobs: runningJobCount() };
  const inFlight = inFlightCount();
  const ws = registry.size();
  const ready = phase === 'active' && inFlight === 0 && ws === 0 && Object.values(ticks).every((n) => n === 0) && playing === 0;
  return { phase, in_flight_requests: inFlight, ws_sessions: ws, running_ticks: ticks, db_active_queries: active, playing_runs: playing, ready_to_stop: ready };
}

/** MN7: 점검과 무관한 긴급 방송. 10초에 한 번(관리자별) */
export function broadcast(admin: AdminCtx, ip: string, body: BroadcastBody): Promise<ActionResult> {
  return runAdminAction({
    admin, ip, action: 'server.broadcast', targetType: 'server', requestId: body.request_id,
    params: { text_length: body.text.length },
    handler: async (client) => {
      consumeOrThrow(`admin-broadcast:${admin.id}`, 1, 10_000);
      const delivered = registry.size();
      afterCommit(client, () => {
        getNotifier().systemBroadcast(body.text);
      });
      return { status: 200, data: { delivered } };
    },
  });
}
