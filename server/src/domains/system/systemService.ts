import fs from 'node:fs';
import { getConfig } from '../../config/env';
import { MIGRATIONS_DIR } from '../../db/migrate';
import { getGameData } from '../../gamedata/loader';
import { isShuttingDown, isWsReady } from '../../ops/lifecycle';
import { maintView } from '../../ops/maintenanceState';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { CHARACTER_LIMIT } from '../auth/authService';
import { NAME_MAX, NAME_MIN } from '../characters/characterValidation';
import * as repo from './systemRepository';

const SCHEMA_CACHE_MS = 60_000;
let schemaCache: { at: number; ok: boolean } | null = null;

/** 이미지의 마이그레이션 파일 수와 schema_migrations 행 수가 같은가(60초 캐시). 새 코드가 마이그레이션 전에 뜨는 사고를 막는다 */
async function schemaMatches(): Promise<boolean> {
  const now = Date.now();
  if (schemaCache && now - schemaCache.at < SCHEMA_CACHE_MS) return schemaCache.ok;
  let ok = false;
  try {
    const files = fs.readdirSync(MIGRATIONS_DIR).filter((f) => /^\d+_.+\.sql$/.test(f)).length;
    ok = files === (await repo.appliedMigrationCount());
  } catch {
    ok = false;
  }
  schemaCache = { at: now, ok };
  return ok;
}

/** 테스트 전용 */
export function resetSchemaCache(): void {
  schemaCache = null;
}

/** GET /health/live: 이벤트 루프가 응답하는가(DB를 보지 않는다) */
export function getLive(): { status: 'ok' } {
  return { status: 'ok' };
}

/** GET /health/ready(= /health): DB, 마이그레이션 일치, 게임 데이터, WebSocket, 종료 중 아님 */
export async function getReady(): Promise<{ ok: boolean; data: Record<string, unknown> }> {
  const db = await repo.pingDb();
  const schema = db ? await schemaMatches() : false;
  let gamedata = true;
  try {
    getGameData();
  } catch {
    gamedata = false;
  }
  const shuttingDown = isShuttingDown();
  const ok = db && schema && gamedata && isWsReady() && !shuttingDown;
  if (ok) return { ok, data: { status: 'ok', db: 'ok' } };
  return {
    ok,
    data: {
      status: 'degraded',
      db: db ? 'ok' : 'down',
      checks: { db, schema, gamedata, websocket: isWsReady(), shutting_down: shuttingDown },
    },
  };
}

export function getMeta(now = getNow()) {
  const cfg = getConfig();
  const b = resetBoundaries(now);
  return {
    min_client_version: cfg.minClientVersion,
    data_version: getGameData().dataVersion,
    server_time: now.toISOString(),
    reset: { next_daily_at: b.nextDailyAt, next_weekly_at: b.nextWeeklyAt },
    auth: { dev_login_enabled: cfg.authDevEnabled, dev_register_enabled: cfg.authDevRegisterEnabled },
    maintenance: maintView(now),
    character: { max_per_account: CHARACTER_LIMIT, name_min: NAME_MIN, name_max: NAME_MAX },
  };
}
