import express, { type Express, type RequestHandler } from 'express';
import { getConfig } from './config/env';
import { errorHandler, notFoundHandler } from './middleware/errorHandler';
import { ipKey, MINUTE, rateLimit } from './middleware/rateLimiter';
import { createRouter } from './routes';
import { logger } from './utils/logger';

const requestLog: RequestHandler = (req, res, next) => {
  // /health는 로드밸런서가 계속 부르므로 로그에서 뺀다
  if (req.path === '/health') {
    next();
    return;
  }
  const start = Date.now();
  res.on('finish', () => {
    logger.info({ method: req.method, path: req.path, status: res.statusCode, ms: Date.now() - start }, 'request');
  });
  next();
};

const generalLimit = rateLimit({
  name: 'general-ip',
  limit: (c) => c.rate.generalIp,
  windowMs: MINUTE,
  key: ipKey,
});

/** 게임 데이터(initGameData)와 설정(initConfig)이 준비된 뒤에 호출한다. */
export function createApp(): Express {
  const app = express();
  app.disable('x-powered-by');
  app.set('trust proxy', getConfig().trustProxy);
  app.use(requestLog);
  app.use((req, res, next) => (req.path === '/health' ? next() : generalLimit(req, res, next)));
  app.use(express.json({ limit: '64kb' }));
  app.use(createRouter());
  app.use(notFoundHandler);
  app.use(errorHandler);
  return app;
}
