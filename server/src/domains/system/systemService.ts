import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { CHARACTER_LIMIT } from '../auth/authService';
import { NAME_MAX, NAME_MIN } from '../characters/characterValidation';
import * as repo from './systemRepository';

const startedAt = Date.now();

export async function getHealth(): Promise<{ ok: boolean; data: Record<string, unknown> }> {
  const dbOk = await repo.pingDb();
  if (!dbOk) return { ok: false, data: { status: 'degraded', db: 'down' } };
  return {
    ok: true,
    data: { status: 'ok', db: 'ok', uptime_sec: Math.floor((Date.now() - startedAt) / 1000) },
  };
}

export function getMeta(now = new Date()) {
  const cfg = getConfig();
  const b = resetBoundaries(now);
  return {
    min_client_version: cfg.minClientVersion,
    data_version: getGameData().dataVersion,
    server_time: now.toISOString(),
    reset: { next_daily_at: b.nextDailyAt, next_weekly_at: b.nextWeeklyAt },
    auth: { dev_login_enabled: cfg.authDevEnabled },
    character: { max_per_account: CHARACTER_LIMIT, name_min: NAME_MIN, name_max: NAME_MAX },
  };
}
