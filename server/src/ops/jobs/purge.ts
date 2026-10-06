// 보관 기간 정리(phase7_ops.md 6.2, 6.3). 한 문장 DELETE ... LIMIT를 각각 자기 트랜잭션으로 반복한다.
// 원장·경매 체결·소각·dungeon_runs 같은 "지우지 않는 것"은 여기 없다(6.3 표).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import type { JobCtx, JobResult } from '../jobRunner';

interface PurgeStats {
  rows: number;
  batches: number;
  skipped: boolean;
}

const sleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));

/** sql의 $1은 배치 크기, 나머지 파라미터는 params. 잠금 시간 초과(55P03)는 그 표를 건너뛰고 다음 실행에서 이어간다 */
async function purgeTable(ctx: JobCtx, sql: string, params: unknown[] = []): Promise<PurgeStats> {
  const { batch, batchSleepMs } = getConfig().purge;
  const st: PurgeStats = { rows: 0, batches: 0, skipped: false };
  while (!ctx.shouldStop()) {
    const client = await getPool().connect();
    let n = 0;
    try {
      await client.query('BEGIN');
      await client.query("SET LOCAL statement_timeout = '30s'");
      await client.query("SET LOCAL lock_timeout = '2s'");
      const r = await client.query(sql, [batch, ...params]);
      await client.query('COMMIT');
      n = r.rowCount ?? 0;
    } catch (err) {
      await client.query('ROLLBACK').catch(() => undefined);
      if ((err as { code?: string }).code === '55P03') {
        st.skipped = true;
        return st;
      }
      throw err;
    } finally {
      client.release();
    }
    st.rows += n;
    st.batches++;
    if (n < batch) break;
    if (batchSleepMs > 0) await sleep(batchSleepMs);
  }
  return st;
}

async function runAll(ctx: JobCtx, steps: [string, string, unknown[]?][]): Promise<JobResult> {
  const tables: Record<string, PurgeStats> = {};
  let rows = 0;
  for (const [name, sql, params] of steps) {
    const st = await purgeTable(ctx, sql, params ?? []);
    tables[name] = st;
    rows += st.rows;
  }
  return { rows, detail: { tables, stopped_early: ctx.shouldStop() } };
}

const del = (table: string, where: string, from = table): string =>
  `DELETE FROM ${table} WHERE id IN (SELECT id FROM ${from} WHERE ${where} ORDER BY id LIMIT $1)`;

/** 매시 17분: 짧은 보관 표. drops(자식)를 먼저, kill_log(부모)를 나중에(CASCADE가 drops_kill_idx로 빠르게 동작) */
export function purgeHourly(ctx: JobCtx): Promise<JobResult> {
  const cfg = getConfig();
  const days = (n: number): string => `now() - (${n}::int * interval '1 day')`;
  return runAll(ctx, [
    ['request_log', del('request_log', `created_at < ${days(cfg.requestLogTtlDays)}`)],
    ['chat_messages', del('chat_messages', `created_at < ${days(cfg.social.chatRetentionDays)}`)],
    ['party_invites', del('party_invites', `created_at < ${days(7)}`)],
    ['drops', del('drops', `expires_at < ${days(cfg.purge.dropDays)}`)],
    ['kill_log', del('kill_log', `created_at < ${days(cfg.purge.killLogDays)}`)],
  ]);
}

/** 매일 KST 04:10 */
export function purgeDaily(ctx: JobCtx): Promise<JobResult> {
  const cfg = getConfig();
  const p = cfg.purge;
  const days = (n: number): string => `now() - (${n}::int * interval '1 day')`;
  return runAll(ctx, [
    [
      'anomaly_log',
      del(
        'anomaly_log',
        `(severity = 1 AND created_at < ${days(p.anomalyDays)}) OR (severity >= 2 AND created_at < ${days(p.anomalySevereDays)})`,
      ),
    ],
    ['friendships', del('friendships', `ended_at IS NOT NULL AND ended_at < ${days(90)}`)],
    ['blocks', del('blocks', `deleted_at IS NOT NULL AND deleted_at < ${days(90)}`)],
    [
      'report_lines',
      `DELETE FROM report_lines WHERE id IN (
         SELECT l.id FROM report_lines l JOIN reports r ON r.id = l.report_id
          WHERE r.state IN ('actioned', 'dismissed') AND r.handled_at < ${days(cfg.social.reportRetentionDays)}
          ORDER BY l.id LIMIT $1)`,
    ],
    [
      'party_applications',
      del('party_applications', `state <> 'pending' AND coalesce(responded_at, expires_at) < ${days(p.partyRecordDays)}`),
    ],
    ['party_members', del('party_members', `left_at IS NOT NULL AND left_at < ${days(p.partyRecordDays)}`)],
    [
      'refresh_tokens',
      del('refresh_tokens', `expires_at < ${days(p.refreshTokenDays)} OR revoked_at < ${days(p.refreshTokenDays)}`),
    ],
    [
      'mails',
      del(
        'mails',
        `claimed_at IS NOT NULL AND claimed_at < ${days(p.mailClaimedDays)} AND kind <> 'system'
         AND NOT EXISTS (SELECT 1 FROM auction_sinks s WHERE s.mail_id = mails.id)`,
      ),
    ],
    [
      'admin_sessions',
      del('admin_sessions', `expires_at < ${days(p.adminSessionDays)} OR revoked_at < ${days(p.adminSessionDays)}`),
    ],
    ['job_runs', del('job_runs', `status <> 'running' AND started_at < ${days(p.jobRunDays)}`)],
    // 8단계: 종료 후 FIELD_RECORD_RETENTION_DAYS(30일)가 지난 필드 세션(멤버가 자식이라 먼저). kill_log가 가리키는 세션은 남긴다
    [
      'field_session_members',
      `DELETE FROM field_session_members WHERE (session_id, character_id) IN (
         SELECT m.session_id, m.character_id FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id
          WHERE s.state = 'ended' AND s.ended_at < ${days(cfg.field.retentionDays)}
            AND NOT EXISTS (SELECT 1 FROM kill_log k WHERE k.field_session_id = s.id)
          LIMIT $1)`,
    ],
    [
      'field_sessions',
      del(
        'field_sessions',
        `state = 'ended' AND ended_at < ${days(cfg.field.retentionDays)} AND NOT EXISTS (SELECT 1 FROM kill_log k WHERE k.field_session_id = field_sessions.id)
         AND NOT EXISTS (SELECT 1 FROM field_session_members m WHERE m.session_id = field_sessions.id)`,
      ),
    ],
    ['relay_room_stats', del('relay_room_stats', `ended_at < ${days(cfg.relay.retentionDays)}`)],
    // 9단계 15절: 기기·IP 개인정보는 보관 기간 뒤 삭제한다(login_events 90일, 집계 표는 마지막 관측 후 180일)
    // 10단계 5.4: 지난 주 계정 주간 카운터(60일). 복합 키 표라 (계정, 주, 종류)로 지운다
    [
      'account_week_counters',
      `DELETE FROM account_week_counters WHERE (account_id, week_start, kind) IN (
         SELECT account_id, week_start, kind FROM account_week_counters WHERE week_start < ${days(60)} LIMIT $1)`,
    ],
    ['login_events', del('login_events', `created_at < ${days(cfg.aa.loginEventDays)}`)],
    [
      'account_devices',
      `DELETE FROM account_devices WHERE (account_id, device_hash) IN (
         SELECT account_id, device_hash FROM account_devices WHERE last_seen_at < ${days(cfg.aa.accountDeviceDays)} LIMIT $1)`,
    ],
    [
      'account_ips',
      `DELETE FROM account_ips WHERE (account_id, ip) IN (
         SELECT account_id, ip FROM account_ips WHERE last_seen_at < ${days(cfg.aa.accountDeviceDays)} LIMIT $1)`,
    ],
    // 끝난 프레즌스 행 7일, 활동 시간·소득 집계 35일(PLAY_TIME_RETENTION_DAYS)
    [
      'online_sessions',
      `DELETE FROM online_sessions WHERE account_id IN (
         SELECT account_id FROM online_sessions WHERE ended_at IS NOT NULL AND ended_at < ${days(7)} LIMIT $1)`,
    ],
    [
      'play_time_hourly',
      `DELETE FROM play_time_hourly WHERE (character_id, hour_start) IN (
         SELECT character_id, hour_start FROM play_time_hourly WHERE hour_start < ${days(cfg.aa.presence.retentionDays)} LIMIT $1)`,
    ],
    [
      'income_hourly',
      `DELETE FROM income_hourly WHERE (character_id, hour_start) IN (
         SELECT character_id, hour_start FROM income_hourly WHERE hour_start < ${days(cfg.aa.presence.retentionDays)} LIMIT $1)`,
    ],
  ]);
}
