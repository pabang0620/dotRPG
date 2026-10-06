// 서버 상태 스냅샷(phase7_ops.md 8.2): 관리자 OP1, ops.snapshot 로그, 감시자 규칙이 같은 값을 쓴다.
import fs from 'node:fs';
import { getConfig } from '../config/env';
import { getPool, poolStats } from '../db/pool';
import { registry } from '../domains/chat/realtimeNotifier';
import { getQueueStore } from '../domains/match/queueStore';
import { steamStats } from '../domains/auth/steamProvider';
import { payPartnerStats } from '../domains/payments/steamPartner';
import { flushStats } from '../domains/antiabuse/incomeMeter';
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
  /** 9단계: 프레즌스·경제 속도 감시 지표(12.10) */
  presence?: { online: number; device_limit_hits: number; presence_required: number };
  economy?: { holds_active: number; holds_shadow_24h: number; holds_active_24h: number; income_flush_ms: number };
  /** 10단계: 소탕·운영 우편 캠페인 지표(설계 11절) */
  sweep?: { runs_1h: number; tickets_outstanding: number; buy_limit_hits: number };
  campaign?: { active: number; delivered_1h: number; cap_reached: number; revoke_pending: number };
  /** 11단계: 결제 지표(설계 12.5). 경보 규칙(12.4)이 쓰는 보조 값을 함께 둔다 */
  payments?: {
    enabled: boolean;
    orders_1h: number;
    granted_1h: number;
    failed_1h: number;
    expired_1h: number;
    open_orders: number;
    stuck_orders: number;
    finalized_ungranted: number;
    refunds_24h: number;
    granted_24h: number;
    chargebacks_24h: number;
    mismatch_24h: number;
    review_open: number;
    review_oldest_age_s: number | null;
    holds_payment_active: number;
    debt_accounts: number;
    unknown_orders_open: number;
    limit_hits_1h_total: number;
    limit_hits_1h_max_account: number;
    grants_pending_oldest_age_s: number | null;
    steam_breaker_open: boolean;
    key_rejected_recent: boolean;
  };
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
  let presence: NonNullable<Snapshot['presence']> = { online: 0, device_limit_hits: metrics.presenceDeviceLimitHits, presence_required: metrics.presenceRequired };
  let economy: NonNullable<Snapshot['economy']> = { holds_active: 0, holds_shadow_24h: 0, holds_active_24h: 0, income_flush_ms: flushStats().avg_ms };
  try {
    const p = await db.query<{ online: string; active: string; shadow24: string; active24: string }>(
      `SELECT (SELECT count(*) FROM online_sessions WHERE ended_at IS NULL AND last_seen_at > now() - ($1::int * interval '1 second')) AS online,
              (SELECT count(*) FROM economy_holds WHERE state IN ('active', 'clawed_back')) AS active,
              (SELECT count(*) FROM economy_holds WHERE state = 'shadow' AND created_at > now() - interval '24 hours') AS shadow24,
              (SELECT count(*) FROM economy_holds WHERE state = 'active' AND created_at > now() - interval '24 hours') AS active24`,
      [cfg.aa.presence.onlineSeconds],
    );
    const x = p.rows[0] as { online: string; active: string; shadow24: string; active24: string };
    presence = { ...presence, online: n(x.online) };
    economy = { ...economy, holds_active: n(x.active), holds_shadow_24h: n(x.shadow24), holds_active_24h: n(x.active24) };
  } catch {
    // DB가 내려가 있으면 비워 둔다
  }
  let sweepInfo: NonNullable<Snapshot['sweep']> = { runs_1h: 0, tickets_outstanding: 0, buy_limit_hits: metrics.sweepBuyLimitHits };
  let campaignInfo: NonNullable<Snapshot['campaign']> = { active: 0, delivered_1h: 0, cap_reached: 0, revoke_pending: 0 };
  try {
    const sw = await db.query<{ runs: string; outstanding: string; active: string; delivered: string; capped: string; revoke: string }>(
      `SELECT (SELECT count(*) FROM dungeon_sweeps WHERE created_at > now() - interval '1 hour') AS runs,
              (SELECT coalesce(sum(remaining), 0) FROM sweep_ticket_lots WHERE remaining > 0 AND (expires_at IS NULL OR expires_at > now())) AS outstanding,
              (SELECT count(*) FROM mail_campaigns WHERE status = 'active') AS active,
              (SELECT count(*) FROM mail_campaign_deliveries WHERE created_at > now() - interval '1 hour') AS delivered,
              (SELECT count(*) FROM mail_campaigns WHERE status IN ('active', 'ended') AND issued_count >= cap_count) AS capped,
              (SELECT count(*) FROM mail_campaigns WHERE revoke_requested AND revoke_done_at IS NULL) AS revoke`,
    );
    const y = sw.rows[0] as { runs: string; outstanding: string; active: string; delivered: string; capped: string; revoke: string };
    sweepInfo = { ...sweepInfo, runs_1h: n(y.runs), tickets_outstanding: n(y.outstanding) };
    campaignInfo = { active: n(y.active), delivered_1h: n(y.delivered), cap_reached: n(y.capped), revoke_pending: n(y.revoke) };
  } catch {
    // DB가 내려가 있으면 비워 둔다
  }
  const partner = payPartnerStats();
  let payments: NonNullable<Snapshot['payments']> = {
    enabled: cfg.pay.enabled,
    orders_1h: 0,
    granted_1h: 0,
    failed_1h: 0,
    expired_1h: 0,
    open_orders: 0,
    stuck_orders: 0,
    finalized_ungranted: 0,
    refunds_24h: 0,
    granted_24h: 0,
    chargebacks_24h: 0,
    mismatch_24h: 0,
    review_open: 0,
    review_oldest_age_s: null,
    holds_payment_active: 0,
    debt_accounts: 0,
    unknown_orders_open: 0,
    limit_hits_1h_total: 0,
    limit_hits_1h_max_account: 0,
    grants_pending_oldest_age_s: null,
    steam_breaker_open: partner.breaker_open,
    key_rejected_recent: partner.key_rejected_recent,
  };
  try {
    const q = await db.query<Record<string, string | null>>(
      `SELECT (SELECT count(*) FROM star_orders WHERE created_at > now() - interval '1 hour') AS orders_1h,
              (SELECT count(*) FROM star_orders WHERE created_at > now() - interval '1 hour' AND granted_at IS NOT NULL) AS granted_1h,
              (SELECT count(*) FROM star_orders WHERE created_at > now() - interval '1 hour' AND state = 'failed') AS failed_1h,
              (SELECT count(*) FROM star_orders WHERE created_at > now() - interval '1 hour' AND state = 'expired') AS expired_1h,
              (SELECT count(*) FROM star_orders WHERE state IN ('pending_init', 'created', 'authorized', 'finalized')) AS open_orders,
              (SELECT count(*) FROM star_orders WHERE (state = 'pending_init' AND created_at < now() - interval '10 minutes')
                  OR (state IN ('created', 'authorized') AND expires_at < now() - interval '10 minutes')
                  OR (state = 'finalized' AND finalized_at < now() - interval '5 minutes')) AS stuck,
              (SELECT count(*) FROM star_orders WHERE state = 'finalized' AND finalized_at < now() - interval '5 minutes') AS fin_stuck,
              (SELECT count(*) FROM star_orders WHERE state = 'refunded' AND reversed_at > now() - interval '24 hours') AS refunds_24h,
              (SELECT count(*) FROM star_orders WHERE granted_at > now() - interval '24 hours') AS granted_24h,
              (SELECT count(*) FROM star_orders WHERE state = 'chargeback' AND reversed_at > now() - interval '24 hours') AS cb_24h,
              (SELECT count(*) FROM payment_flags WHERE kind IN ('amount_mismatch', 'steamid_mismatch', 'appid_mismatch') AND created_at > now() - interval '24 hours') AS mismatch_24h,
              (SELECT count(*) FROM payment_flags WHERE state = 'open') AS review_open,
              (SELECT extract(epoch FROM now() - min(created_at)) FROM payment_flags WHERE state = 'open') AS review_oldest,
              (SELECT count(*) FROM economy_holds WHERE kind = 'payment' AND state IN ('active', 'clawed_back')) AS holds_payment,
              (SELECT count(*) FROM star_wallets WHERE debt > 0) AS debt_accounts,
              ((SELECT count(*) FROM payment_flags WHERE kind = 'unknown_steam_order' AND state = 'open')
               + coalesce((SELECT (detail->>'unknown_orders')::int FROM job_runs WHERE job = 'payment-report' AND status = 'ok' ORDER BY started_at DESC LIMIT 1), 0)) AS unknown_open,
              (SELECT count(*) FROM payment_flags WHERE kind = 'limit_exceeded' AND created_at > now() - interval '1 hour') AS limit_total,
              (SELECT coalesce(max(c), 0) FROM (SELECT count(*) AS c FROM payment_flags WHERE kind = 'limit_exceeded' AND created_at > now() - interval '1 hour' GROUP BY account_id) t) AS limit_max,
              (SELECT extract(epoch FROM now() - min(created_at)) FROM star_admin_grants WHERE state = 'pending') AS grant_oldest`,
    );
    const x = q.rows[0] as Record<string, string | null>;
    payments = {
      ...payments,
      orders_1h: n(x.orders_1h),
      granted_1h: n(x.granted_1h),
      failed_1h: n(x.failed_1h),
      expired_1h: n(x.expired_1h),
      open_orders: n(x.open_orders),
      stuck_orders: n(x.stuck),
      finalized_ungranted: n(x.fin_stuck),
      refunds_24h: n(x.refunds_24h),
      granted_24h: n(x.granted_24h),
      chargebacks_24h: n(x.cb_24h),
      mismatch_24h: n(x.mismatch_24h),
      review_open: n(x.review_open),
      review_oldest_age_s: x.review_oldest === null ? null : Math.round(n(x.review_oldest)),
      holds_payment_active: n(x.holds_payment),
      debt_accounts: n(x.debt_accounts),
      unknown_orders_open: n(x.unknown_open),
      limit_hits_1h_total: n(x.limit_total),
      limit_hits_1h_max_account: n(x.limit_max),
      grants_pending_oldest_age_s: x.grant_oldest === null ? null : Math.round(n(x.grant_oldest)),
    };
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
    presence,
    economy,
    sweep: sweepInfo,
    campaign: campaignInfo,
    payments,
    steam: steamStats(),
    shutting_down: isShuttingDown(),
    disk_used_pct: diskUsedPct(),
  };
}
