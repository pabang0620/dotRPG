import fs from 'node:fs';
import path from 'node:path';
import { createApp } from './app';
import { initConfig } from './config/env';
import { purgeExpiredRequestLogs } from './db/idempotency';
import { closePool, getPool } from './db/pool';
import { purgeChatData } from './domains/chat/chatRepository';
import { attachRealtime } from './domains/chat/wsServer';
import { startAuctionTicker } from './domains/auction/auctionTicker';
import { runMatchTick } from './domains/match/matchService';
import { runSettleTick } from './domains/partyruns/partySettle';
import { initGameData } from './gamedata/loader';
import { logger } from './utils/logger';

async function main(): Promise<void> {
  const envFile = path.resolve(process.cwd(), '.env');
  if (fs.existsSync(envFile)) process.loadEnvFile(envFile);

  // 설정·게임 데이터가 틀리면 여기서 던져서 기동이 멈춘다
  const cfg = initConfig();
  const data = initGameData(cfg.gameDataDir);
  await getPool().query('SELECT 1');

  const app = createApp();
  const server = app.listen(cfg.port, () => {
    logger.info({ port: cfg.port, dataVersion: data.dataVersion }, 'server started');
  });

  const realtime = await attachRealtime(server);

  const purge = setInterval(() => {
    purgeChatData(cfg.social.chatRetentionDays, cfg.social.reportRetentionDays).catch((err: unknown) =>
      logger.error({ err }, 'chat purge failed'),
    );
    purgeExpiredRequestLogs(getPool(), cfg.requestLogTtlDays).catch((err: unknown) =>
      logger.error({ err }, 'request_log purge failed'),
    );
  }, 60 * 60 * 1000);
  purge.unref();

  // 자동 매칭 틱(1초). 대기열은 메모리라 한 대 서버 전제
  const tick = setInterval(() => {
    runMatchTick().catch((err: unknown) => logger.error({ err }, 'match tick failed'));
    runSettleTick().catch((err: unknown) => logger.error({ err }, 'settle tick failed'));
  }, 1000);
  tick.unref();

  // 경매 마감 정산 틱(기동 직후 즉시 1회 + 주기). AUCTION_TICK_ENABLED=false면 쓰지 않는다
  const stopAuction = cfg.auction.tickEnabled ? startAuctionTicker() : () => undefined;

  const shutdown = (): void => {
    clearInterval(purge);
    stopAuction();
    clearInterval(tick);
    // 새 업그레이드를 막고 모든 연결에 bye(1001)를 보낸 뒤 HTTP 서버를 닫는다(열린 WebSocket이 close를 막지 않게)
    realtime.close().finally(() => {
      server.close(() => {
        closePool().finally(() => process.exit(0));
      });
    });
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

main().catch((err: unknown) => {
  console.error('서버를 시작할 수 없습니다:', err instanceof Error ? err.message : err);
  process.exit(1);
});
