// 서버 상태 스냅샷(phase7_ops.md 8.2): 관리자 OP1, ops.snapshot 로그, 감시자 규칙이 같은 값을 쓴다.
import fs from 'node:fs';
import { getConfig } from '../config/env';
import { getPool, poolStats } from '../db/pool';
import { registry } from '../domains/chat/realtimeNotifier';
import { getQueueStore } from '../domains/match/queueStore';
import { steamStats } from '../domains/auth/steamProvider';
import { relayHub } from '../domains/relay/relayHub';
import { relayMetrics } from '../domains/relay/relayMetrics';
import { getGameData } from '../gamedata/loader';
import { getNow } from '../utils/clock';
import { inFlightCount, isShuttingDown } from './lifecycle';
import { maintPhase, getMaintenance } from './maintenanceState';
import { metrics } from './metrics';
import { listJobs } from './jobRunner';

export interface Snapshot {
  at: string;
  process: { uptime_s: number; rss_mb: number; heap_mb: number; eventloop_delay_p99_ms: number; version: string; data_version: string | null };
  http: ReturnType<typeof metrics.http> & { in_flight: number; p95_5m_ms: number; status_5xx_5m: number; requests_5m: number };
  websocket: { sessions: number; max: number; close_reasons: Record<string, number>; handshake_rejected: number };
  db: { pool: ReturnType<typeof poolStats>; connections: number | null; max_connections: number | null; size_mb: number | null };
  ticks: Record<string, { last_at: string; ms: number }>;
  auction: { lag_seconds: number };
  jobs: { name: string; last_status: string | null; last_started_at: string | null; last_ok_at: string | null }[];
  queues: {
    match_waiting: number;
    open_reports: number;
    oldest_open_report_age_s: number | null;
    held_runs: number;
    oldest_held_age_s: number | null;
    unnotified_sanctions: number;
  };
  security: { login_failures_5m: number };
  maintenance: { phase: string; remaining_s: number | null };
  /** 8단계: 전투 중계(phase8_api.md 4.12) */
  relay?: {
    rooms: number;
    max_rooms: number;
    conns: number;
    max_conns: number;
    bytes_in_1m: number;
    bytes_out_1m: number;
    frames_in_1m: number;
    frames_out_1m: number;
    dropped_unreliable_1m: number;
    close_codes_1m: Record<string, number>;
    reconnects_1m: number;
    ticket_rejects_1m: Record<string, number>;
    rtt_p50_ms: number;
    rtt_p95_ms: number;
    rtt_high_for_s: number;
    flush_lag_p99_ms: number;
    egress_24h_gb: number;
    egress_budget_gb: number | null;
    unavailable_for_s: number;
    abuse_10m: number;
    fallbacks_10m: number;
    steam_to_relay_10m: number;
    host_changes_1m: number;
  };
  field?: { sessions_active: number; avg_members: number };
  steam?: ReturnType<typeof steamStats>;
  shutting_down: boolean;
  disk_used_pct: number | null;
}

const startedAt = Date.now();

function diskUsedPct(): number | null {
  try {
    const s = fs.statfsSync('/');
    return Math.round((1 - s.bavail / s.blocks) * 100);
  } catch {
    return null;
  }
}

const n = (v: unknown): number => Number(v ?? 0);

export async function collectSnapshot(): Promise<Snapshot> {
  const now = getNow();
  const mem = process.memoryUsage();
  const cfg = getConfig();
  const db = getPool();
  let dbInfo: Snapshot['db'] = { pool: poolStats(), connections: null, max_connections: null, size_mb: null };
  let queues: Snapshot['queues'] = {
    match_waiting: 0,
    open_reports: 0,
    oldest_open_report_age_s: null,
    held_runs: 0,
    oldest_held_age_s: null,
    unnotified_sanctions: 0,
  };
  let jobs: Snapshot['jobs'] = [];
  try {
    const c = await db.query<{ conn: string; max: string; size: string }>(
      `SELECT (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()) AS conn,
              current_setting('max_connections') AS max,
              pg_database_size(current_database()) AS size`,
    );
    const r = c.rows[0] as { conn: string; max: string; size: string };
    dbInfo = { pool: poolStats(), connections: n(r.conn), max_connections: n(r.max), size_mb: Math.round(n(r.size) / 1048576) };
    const q = await db.query<Record<string, string | null>>(
      `SELECT (SELECT count(*) FROM reports WHERE state IN ('open', 'reviewing')) AS open_reports,
              (SELECT extract(epoch FROM now() - min(created_at)) FROM reports WHERE state IN ('open', 'reviewing')) AS oldest_report,
              (SELECT count(*) FROM dungeon_runs d WHERE d.state = 'held'
                  AND NOT EXISTS (SELECT 1 FROM held_run_reviews h WHERE h.dungeon_run_id = d.id)) AS held_runs,
              (SELECT extract(epoch FROM now() - min(d.ended_at)) FROM dungeon_runs d WHERE d.state = 'held'
                  AND NOT EXISTS (SELECT 1 FROM held_run_reviews h WHERE h.dungeon_run_id = d.id)) AS oldest_held,
              (SELECT count(*) FROM account_sanctions WHERE notified_at IS NULL AND revoked_at IS NULL) AS unnotified`,
    );
    const x = q.rows[0] as Record<string, string | null>;
    const store = getQueueStore();
    queues = {
      match_waiting: store.keys().reduce((a, k) => a + store.listByKey(k.dungeonId, k.difficulty).length, 0),
      open_reports: n(x.open_reports),
      oldest_open_report_age_s: x.oldest_report === null ? null : Math.round(n(x.oldest_report)),
      held_runs: n(x.held_runs),
      oldest_held_age_s: x.oldest_held === null ? null : Math.round(n(x.oldest_held)),
      unnotified_sanctions: n(x.unnotified),
    };
    const jr = await db.query<{ job: string; status: string; started_at: Date; ok_at: Date | null }>(
      `SELECT DISTINCT ON (job) job, status, started_at,
              (SELECT max(finished_at) FROM job_runs j2 WHERE j2.job = j1.job AND j2.status = 'ok') AS ok_at
         FROM job_runs j1 ORDER BY job, started_at DESC`,
    );
    const byName = new Map(jr.rows.map((r) => [r.job, r]));
    jobs = listJobs().map((d) => {
      const r = byName.get(d.name);
      return {
        name: d.name,
        last_status: r?.status ?? null,
        last_started_at: r?.started_at.toISOString() ?? null,
        last_ok_at: r?.ok_at?.toISOString() ?? null,
      };
    });
  } catch {
    // DB가 내려가 있으면 DB 항목은 비워 둔다(헬스 체크가 알린다)
  }
  const hub = relayHub().counts();
  const rtt = relayMetrics.rttPercentiles();
  let fieldInfo: NonNullable<Snapshot['field']> = { sessions_active: 0, avg_members: 0 };
  try {
    const f = await db.query<{ n: string; m: string | null }>(
      `SELECT count(*) AS n, avg((SELECT count(*) FROM field_session_members x WHERE x.session_id = s.id AND x.state <> 'left')) AS m
         FROM field_sessions s WHERE s.state = 'active'`,
    );
    fieldInfo = { sessions_active: n(f.rows[0]?.n), avg_members: Math.round(n(f.rows[0]?.m) * 10) / 10 };
  } catch {
    // DB가 내려가 있으면 비워 둔다
  }
  const m1 = 60_000;
  const h1 = metrics.http(60_000);
  const h5 = metrics.http(5 * 60_000);
  const w = getMaintenance();
  let g: string | null = null;
  try {
    g = getGameData().dataVersion;
  } catch {
    g = null;
  }
  const phase = maintPhase(now);
  const ticks: Snapshot['ticks'] = {};
  for (const [k, v] of metrics.ticks) ticks[k] = { last_at: new Date(v.lastAt).toISOString(), ms: v.ms };
  return {
    at: now.toISOString(),
    process: {
      uptime_s: Math.floor((Date.now() - startedAt) / 1000),
      rss_mb: Math.round(mem.rss / 1048576),
      heap_mb: Math.round(mem.heapUsed / 1048576),
      eventloop_delay_p99_ms: metrics.eventLoopP99Ms(),
      version: cfg.ops.imageVersion,
      data_version: g,
    },
    http: { ...h1, in_flight: inFlightCount(), p95_5m_ms: h5.p95_ms, status_5xx_5m: h5.status_5xx, requests_5m: h5.requests },
    websocket: {
      sessions: registry.size(),
      max: cfg.social.wsMaxConnections,
      close_reasons: Object.fromEntries(metrics.wsClose),
      handshake_rejected: metrics.wsHandshakeRejected,
    },
    db: dbInfo,
    ticks,
    auction: { lag_seconds: Math.round(metrics.auctionLagSeconds) },
    jobs,
    queues,
    security: { login_failures_5m: metrics.loginFailures.length },
    maintenance: {
      phase,
      remaining_s: phase !== 'none' && w ? Math.max(0, Math.round((w.endsAt.getTime() - now.getTime()) / 1000)) : null,
    },
    relay: {
      rooms: hub.rooms,
      max_rooms: cfg.relay.maxRooms,
      conns: hub.conns,
      max_conns: cfg.relay.maxConnections,
      bytes_in_1m: relayMetrics.bytesIn.sum(m1),
      bytes_out_1m: relayMetrics.bytesOut.sum(m1),
      frames_in_1m: relayMetrics.framesIn.sum(m1),
      frames_out_1m: relayMetrics.framesOut.sum(m1),
      dropped_unreliable_1m: relayMetrics.droppedUnreliable.sum(m1),
      close_codes_1m: relayMetrics.closesSince(m1),
      reconnects_1m: relayMetrics.reconnects.sum(m1),
      ticket_rejects_1m: relayMetrics.rejectsSince(m1),
      rtt_p50_ms: rtt.p50,
      rtt_p95_ms: rtt.p95,
      rtt_high_for_s: Math.round(relayMetrics.rttHighForMs() / 1000),
      flush_lag_p99_ms: relayMetrics.flushLagP99(),
      egress_24h_gb: Math.round((relayMetrics.bytesOut.sum(24 * 3_600_000) / 1e9) * 1000) / 1000,
      egress_budget_gb: cfg.relay.egressDailyBudgetGb,
      unavailable_for_s: relayMetrics.unavailableSince === 0 ? 0 : Math.round((Date.now() - relayMetrics.unavailableSince) / 1000),
      abuse_10m: relayMetrics.abuse.sum(10 * m1),
      fallbacks_10m: relayMetrics.fallbacks.sum(10 * m1),
      steam_to_relay_10m: relayMetrics.steamToRelay.sum(10 * m1),
      host_changes_1m: relayMetrics.hostChanges.sum(m1),
    },
    field: fieldInfo,
    steam: steamStats(),
    shutting_down: isShuttingDown(),
    disk_used_pct: diskUsedPct(),
  };
}
