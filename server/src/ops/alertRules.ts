// 감시 규칙(phase7_ops.md 8.3). 임계값은 시작값이고 운영하며 조정한다: 상수는 이 파일 한 곳.
import type { Snapshot } from './snapshot';

export type AlertLevel = 'critical' | 'warning';

export interface Alert {
  key: string;
  level: AlertLevel;
  title: string;
  detail: string;
}

export const THRESHOLDS = {
  http5xxRatio: 0.02,
  http5xxMinRequests: 20,
  poolWaitingCritical: 5,
  diskCritical: 90,
  diskWarning: 80,
  p95WarningMs: 1000,
  eventLoopP99WarningMs: 200,
  dbConnWarningRatio: 0.8,
  rssWarningRatio: 0.8,
  auctionLagWarning: 180,
  auctionLagCritical: 600,
  wsWarningRatio: 0.8,
  slowConsumerPerMin: 10,
  relayCapacityRatio: 0.8,
  peerTimeoutRatio: 0.3,
  peerTimeoutMinCloses: 10,
  relayFlushLagP99Ms: 60,
  relayEgressBudgetRatio: 0.8,
  relayRttP95Ms: 250,
  relayRttHighSeconds: 600,
  relayAbuse10m: 5,
  steamToRelay10m: 5,
  relayUnavailableSeconds: 300,
  loginFailures5m: 100,
  heldRunAgeS: 24 * 3600,
  openReportAgeS: 48 * 3600,
  jobStaleHours: { 'purge-hourly': 3, 'purge-daily': 30, 'integrity-nightly': 30, 'stale-runs': 1 } as Record<string, number>,
} as const;

export interface RuleInput {
  snap: Snapshot;
  /** 최근 integrity-nightly 결과의 불일치 수(없으면 null) */
  integrityMismatches: number | null;
  /** 컨테이너 메모리 제한(MB), 모르면 null */
  memLimitMb: number | null;
  /** ready 점검이 이번 분에 실패했는가(연속 실패 횟수) */
  readyFailStreak: number;
  slowConsumerCloses1m: number;
}

export function evaluate(i: RuleInput, now: Date = new Date()): Alert[] {
  const s = i.snap;
  const T = THRESHOLDS;
  const out: Alert[] = [];
  const add = (key: string, level: AlertLevel, title: string, detail: string): void => {
    out.push({ key, level, title, detail });
  };
  if (i.readyFailStreak >= 2) add('ready_fail', 'critical', '서비스 중단 의심', `ready 점검이 ${i.readyFailStreak}회 연속 실패했습니다.`);
  if (s.http.requests_5m >= T.http5xxMinRequests && s.http.status_5xx_5m / s.http.requests_5m > T.http5xxRatio) {
    add('http_5xx', 'critical', '5xx 비율 높음', `최근 5분 ${s.http.status_5xx_5m}/${s.http.requests_5m}건이 5xx입니다. 새 배포·DB 문제를 확인하세요.`);
  }
  if (s.db.pool.waiting >= T.poolWaitingCritical) add('pool_waiting', 'critical', 'DB 풀 포화', `풀 대기 ${s.db.pool.waiting}건입니다.`);
  else if (s.db.pool.waiting > 0) add('pool_waiting_warn', 'warning', 'DB 풀 대기', `풀 대기 ${s.db.pool.waiting}건입니다.`);
  if (i.integrityMismatches !== null && i.integrityMismatches >= 1) {
    add('integrity', 'critical', '정합성 점검 불일치', `불일치 ${i.integrityMismatches}건. 재화 복사·원장 우회를 즉시 조사하세요.`);
  }
  if (s.disk_used_pct !== null) {
    if (s.disk_used_pct >= T.diskCritical) add('disk', 'critical', '디스크 부족', `사용률 ${s.disk_used_pct}%입니다.`);
    else if (s.disk_used_pct >= T.diskWarning) add('disk_warn', 'warning', '디스크 사용량 높음', `사용률 ${s.disk_used_pct}%입니다.`);
  }
  if (s.http.p95_5m_ms > T.p95WarningMs) add('p95', 'warning', '응답 느림', `최근 5분 p95 ${s.http.p95_5m_ms}ms입니다.`);
  if (s.process.eventloop_delay_p99_ms > T.eventLoopP99WarningMs) {
    add('eventloop', 'warning', '이벤트 루프 지연', `p99 ${s.process.eventloop_delay_p99_ms}ms입니다.`);
  }
  if (s.db.connections !== null && s.db.max_connections && s.db.connections > s.db.max_connections * T.dbConnWarningRatio) {
    add('db_conn', 'warning', 'DB 연결 많음', `${s.db.connections}/${s.db.max_connections}`);
  }
  if (i.memLimitMb && s.process.rss_mb > i.memLimitMb * T.rssWarningRatio) add('rss', 'warning', '메모리 사용량 높음', `RSS ${s.process.rss_mb}MB / 제한 ${i.memLimitMb}MB`);
  if (s.auction.lag_seconds > T.auctionLagCritical) add('auction_lag', 'critical', '경매 정산 지연', `${s.auction.lag_seconds}초 지연입니다.`);
  else if (s.auction.lag_seconds > T.auctionLagWarning) add('auction_lag_warn', 'warning', '경매 정산 지연', `${s.auction.lag_seconds}초 지연입니다.`);
  for (const j of s.jobs) {
    const hours = T.jobStaleHours[j.name];
    if (hours === undefined) continue;
    const ref = j.last_ok_at ? Date.parse(j.last_ok_at) : null;
    // 아직 한 번도 안 돈 작업은 기동 직후일 수 있어 가동 시간이 기준을 넘었을 때만 경고한다
    const stale = ref === null ? s.process.uptime_s > hours * 3600 : now.getTime() - ref > hours * 3_600_000;
    if (stale) add(`job_${j.name}`, 'warning', '정리 작업 정지', `${j.name} 마지막 성공이 ${hours}시간을 넘었습니다.`);
  }
  if (s.websocket.sessions >= s.websocket.max * T.wsWarningRatio) add('ws_cap', 'warning', 'WebSocket 접속 많음', `${s.websocket.sessions}/${s.websocket.max}`);
  if (i.slowConsumerCloses1m > T.slowConsumerPerMin) add('slow_consumer', 'warning', '느린 소비자 끊김 많음', `최근 1분 ${i.slowConsumerCloses1m}건`);
  if (s.security.login_failures_5m > T.loginFailures5m) add('login_fail', 'warning', '로그인 실패 급증', `최근 5분 ${s.security.login_failures_5m}건(크리덴셜 스터핑 의심)`);
  if (s.queues.oldest_held_age_s !== null && s.queues.oldest_held_age_s > T.heldRunAgeS) add('held_age', 'warning', '보류 판 검토 지연', `가장 오래된 보류 판이 24시간을 넘었습니다.`);
  if (s.queues.oldest_open_report_age_s !== null && s.queues.oldest_open_report_age_s > T.openReportAgeS) add('report_age', 'warning', '신고 처리 지연', `가장 오래된 열린 신고가 48시간을 넘었습니다.`);
  // ---- 8단계: 전투 중계·Steam (phase8_api.md 4.12) ----
  const r = s.relay;
  if (r) {
    if (r.conns >= r.max_conns * T.relayCapacityRatio || r.rooms >= r.max_rooms * T.relayCapacityRatio) {
      add('relay_capacity', 'warning', '중계 용량 80% 도달', `연결 ${r.conns}/${r.max_conns}, 방 ${r.rooms}/${r.max_rooms}. Steam 우선 전환·분리를 검토하세요.`);
    }
    const slow = r.close_codes_1m['4008'] ?? 0;
    if (slow > T.slowConsumerPerMin) add('relay_slow_consumer', 'warning', '중계 느린 소비자 끊김 많음', `최근 1분 4008 ${slow}건`);
    const closes = Object.values(r.close_codes_1m).reduce((a, b) => a + b, 0);
    const timeouts = r.close_codes_1m['4013'] ?? 0;
    if (closes >= T.peerTimeoutMinCloses && timeouts / closes > T.peerTimeoutRatio) {
      add('relay_peer_timeout', 'warning', '중계 PEER_TIMEOUT 비율 높음', `최근 1분 종료 ${closes}건 중 4013 ${timeouts}건`);
    }
    if (r.flush_lag_p99_ms > T.relayFlushLagP99Ms) add('relay_flush_lag', 'warning', '중계 플러시 틱 지연', `p99 ${r.flush_lag_p99_ms}ms`);
    if (r.egress_budget_gb !== null && r.egress_24h_gb > r.egress_budget_gb * T.relayEgressBudgetRatio) {
      add('relay_egress', 'warning', '중계 송신량 예산 80% 도달', `24시간 ${r.egress_24h_gb}GB / 예산 ${r.egress_budget_gb}GB`);
    }
    if (r.rtt_p95_ms > T.relayRttP95Ms && r.rtt_high_for_s >= T.relayRttHighSeconds) {
      add('relay_rtt', 'warning', '중계 RTT 높음', `p95 ${r.rtt_p95_ms}ms가 ${Math.round(r.rtt_high_for_s / 60)}분 지속`);
    }
    if (r.abuse_10m >= T.relayAbuse10m) add('relay_abuse', 'warning', '중계 남용 시도', `최근 10분 ${r.abuse_10m}건`);
    if (r.steam_to_relay_10m >= T.steamToRelay10m) add('transport_fallback', 'warning', 'Steam 경로 장애 의심', `최근 10분 steam -> relay 전환 ${r.steam_to_relay_10m}건`);
    if (r.unavailable_for_s >= T.relayUnavailableSeconds) add('relay_unavailable', 'critical', '중계 정지', `RELAY_UNAVAILABLE 응답이 ${Math.round(r.unavailable_for_s / 60)}분째 이어집니다.`);
  }
  if (s.steam?.misconfigured_recent) add('steam_auth_misconfigured', 'critical', 'Steam 로그인 설정 오류', 'Steam Web API가 키·앱 ID를 거절했습니다. 키 만료 또는 STEAM_APP_ID 오설정을 확인하세요.');
  if (s.steam?.breaker_open) add('steam_breaker', 'warning', 'Steam 인증 회로 차단', 'Steam 호출이 연속 실패해 잠시 차단되었습니다.');
  return out;
}
