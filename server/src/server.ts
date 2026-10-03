import fs from 'node:fs';
import path from 'node:path';
import { createApp } from './app';
import { initConfig } from './config/env';
import { purgeExpiredRequestLogs } from './db/idempotency';
import { closePool, getPool } from './db/pool';
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

  const purge = setInterval(() => {
    purgeExpiredRequestLogs(getPool(), cfg.requestLogTtlDays).catch((err: unknown) =>
      logger.error({ err }, 'request_log purge failed'),
    );
  }, 60 * 60 * 1000);
  purge.unref();

  const shutdown = (): void => {
    clearInterval(purge);
    server.close(() => {
      closePool().finally(() => process.exit(0));
    });
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

main().catch((err: unknown) => {
  console.error('서버를 시작할 수 없습니다:', err instanceof Error ? err.message : err);
  process.exit(1);
});
