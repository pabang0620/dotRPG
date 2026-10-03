import type { Server } from 'node:http';
import { getConfig } from '../config/env';
import { logger } from '../utils/logger';
import { createAdminApp } from './adminApp';

/** 관리자 listener(기본 127.0.0.1:3001)를 연다. 어떤 포트로도 공개하지 않는다 */
export function startAdminServer(): Promise<Server> {
  const cfg = getConfig().admin;
  const server = createAdminApp().listen(cfg.port, cfg.bind);
  return new Promise((resolve, reject) => {
    server.once('listening', () => {
      logger.info({ bind: cfg.bind, port: cfg.port }, 'admin listener started');
      resolve(server);
    });
    server.once('error', reject);
  });
}
