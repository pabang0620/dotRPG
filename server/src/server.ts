import fs from 'node:fs';
import path from 'node:path';
import { startAdminServer } from './admin/adminServer';
import { createApp } from './app';
import { initConfig } from './config/env';
import { closePool, getPool } from './db/pool';
import { startAuctionTicker } from './domains/auction/auctionTicker';
import { startHoldWorker } from './domains/antiabuse/holdSweep';
import { attachRealtime } from './domains/chat/wsServer';
import { setRelayState } from './domains/relay/relayHub';
import { attachRelay } from './domains/relay/relayServer';
import { steamSelfCheck } from './domains/auth/steamProvider';
import { runMatchTick } from './domains/match/matchService';
import { runSettleTick } from './domains/partyruns/partySettle';
import { runCardAutoPickTick } from './domains/dungeons/dungeonService';
import { initGameData } from './gamedata/loader';
import { beginShutdown, inFlightCount, isShuttingDown, setWsState, waitForInFlight } from './ops/lifecycle';
import { closeInterruptedRuns, requestJobStop, runJob, startJobRunner } from './ops/jobRunner';
import { registerAllJobs } from './ops/jobs';
import { startAnnouncer } from './ops/maintenanceAnnouncer';
import { loadMaintenanceFromDb, startMaintenancePolling } from './ops/maintenanceState';
import { metrics } from './ops/metrics';
import { startWatchdog } from './ops/watchdog';
import { logger } from './utils/logger';

const sleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));

async function main(): Promise<void> {
  const envFile = path.resolve(process.cwd(), '.env');
  if (fs.existsSync(envFile)) process.loadEnvFile(envFile);

  // 설정·게임 데이터가 틀리면 여기서 던져서 기동이 멈춘다(운영 가드 G1~G7 포함)
  const cfg = initConfig();
  const data = initGameData(cfg.gameDataDir);
  await getPool().query('SELECT 1');

  registerAllJobs();
  const interrupted = await closeInterruptedRuns();
  if (interrupted > 0) logger.warn({ interrupted }, 'job_runs interrupted rows closed');
  await loadMaintenanceFromDb();

  const app = createApp();
  setWsState('pending');
  if (cfg.relay.enabled) setRelayState('pending');
  const server = app.listen(cfg.port, () => {
    logger.info({ port: cfg.port, dataVersion: data.dataVersion }, 'server started');
  });
  const realtime = await attachRealtime(server);
  setWsState('attached');
  const relay = await attachRelay(server);
  // Steam 로그인 자가 점검(키·앱 ID가 AuthenticateUserTicket 에 통하는지). 실패해도 서버는 뜬다
  if (cfg.steam.mode === 'web_api') steamSelfCheck().catch((err: unknown) => logger.error({ err }, 'steam.selfcheck failed'));
  const adminServer = cfg.admin.enabled ? await startAdminServer() : null;

  const stops: (() => void)[] = [];
  stops.push(startMaintenancePolling(), startAnnouncer(), startWatchdog());
  const stopJobs = cfg.ops.jobsEnabled ? startJobRunner() : async () => undefined;
  if (cfg.ops.jobsEnabled) runJob('maintenance-close', 'startup').catch((err: unknown) => logger.error({ err }, 'startup job failed'));

  // 자동 매칭 틱(1초). 대기열은 메모리라 한 대 서버 전제
  let tickCount = 0;
  const tick = setInterval(() => {
    if (isShuttingDown()) return;
    // 고르지 않은 던전 카드 자동 지급은 30초마다면 충분하다
    if (++tickCount % 30 === 0) metrics.track('cards', runCardAutoPickTick).catch((err: unknown) => logger.error({ err }, 'card tick failed'));
    metrics.track('match', runMatchTick).catch((err: unknown) => logger.error({ err }, 'match tick failed'));
    metrics.track('settle', runSettleTick).catch((err: unknown) => logger.error({ err }, 'settle tick failed'));
  }, 1000);
  tick.unref();

  // 9단계: 경제 속도 감시 더티 워커(ECONOMY_HOLD_CHECK_SECONDS마다 소득이 바뀐 캐릭터만 평가). 전체 훑기·원장 대조는 작업 스케줄러가 돈다
  const stopHoldWorker = startHoldWorker();

  // 경매 마감 정산 틱(기동 직후 즉시 1회 + 주기). AUCTION_TICK_ENABLED=false면 쓰지 않는다
  const stopAuction = cfg.auction.tickEnabled ? startAuctionTicker() : () => undefined;

  // 운영에서 개발용 로그인이 켜져 있으면(Steam 연동 전 시험 기간) 경고를 매시간 남긴다
  if (cfg.nodeEnv === 'production' && cfg.authDevEnabled) {
    const banner = (): void => logger.warn('DEV AUTH ENABLED IN PRODUCTION: 개발용 로그인이 켜져 있습니다(Steam 연동 전 시험 기간 전용)');
    banner();
    const t = setInterval(banner, 3_600_000);
    t.unref();
  }

  // 정상 종료(phase7_ops.md 4.5): 전체 제한 SHUTDOWN_GRACE_SECONDS, 넘으면 exit(1)
  let stopping = false;
  const shutdown = async (signal: string): Promise<void> => {
    if (stopping) return;
    stopping = true;
    const t0 = Date.now();
    const timing: Record<string, number> = {};
    const mark = (k: string, since: number): void => {
      timing[k] = Date.now() - since;
    };
    const force = setTimeout(() => {
      logger.error({ timing }, 'shutdown.timeout');
      process.exit(1);
    }, cfg.shutdownGraceSeconds * 1000);
    try {
      // 1. 종료 중 플래그: ready 503, 새 REST 요청 503 SHUTTING_DOWN, 새 WebSocket 거절
      beginShutdown();
      // 2. 틱·작업 정지 신호
      clearInterval(tick);
      stopAuction();
      stopHoldWorker();
      requestJobStop();
      for (const s of stops) s();
      // 3. 진행 중인 요청 대기(최대 10초)
      let t = Date.now();
      const drained = await waitForInFlight(10_000);
      mark('in_flight_ms', t);
      // 4. 모든 WebSocket에 bye, 채팅 쓰기 큐 비우기
      t = Date.now();
      await relay.close();
      await realtime.close();
      mark('realtime_ms', t);
      // 5. 진행 중인 틱·작업 대기(최대 10초)
      t = Date.now();
      await stopJobs();
      const end = Date.now() + 10_000;
      while (metrics.runningTotal() > 0 && Date.now() < end) await sleep(50);
      mark('ticks_ms', t);
      // 6. HTTP 서버 닫기, 풀 닫기
      t = Date.now();
      await new Promise<void>((resolve) => {
        server.close(() => resolve());
        server.closeIdleConnections();
        setTimeout(() => server.closeAllConnections(), 2000).unref();
      });
      if (adminServer) {
        await new Promise<void>((resolve) => {
          adminServer.close(() => resolve());
          adminServer.closeAllConnections();
        });
      }
      await closePool();
      mark('close_ms', t);
      logger.info({ signal, total_ms: Date.now() - t0, drained, in_flight_left: inFlightCount(), ...timing }, 'shutdown.done');
      clearTimeout(force);
      process.exit(0);
    } catch (err) {
      logger.error({ err, timing }, 'shutdown failed');
      process.exit(1);
    }
  };
  process.on('SIGINT', () => void shutdown('SIGINT'));
  process.on('SIGTERM', () => void shutdown('SIGTERM'));
}

main().catch((err: unknown) => {
  console.error('서버를 시작할 수 없습니다:', err instanceof Error ? err.message : err);
  process.exit(1);
});
