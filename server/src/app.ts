import express, { type Express, type RequestHandler } from 'express';
import { getConfig } from './config/env';
import { errorHandler, notFoundHandler } from './middleware/errorHandler';
import { maintenanceGuard } from './middleware/maintenanceGuard';
import { ipKey, MINUTE, rateLimit } from './middleware/rateLimiter';
import { lifecycleGuard } from './ops/lifecycle';
import { createRouter } from './routes';
import { logger } from './utils/logger';

const HEALTH_PATH = /^\/health(\/live|\/ready)?$/;

const requestLog: RequestHandler = (req, res, next) => {
  // /health*는 로드밸런서가 계속 부르므로 로그에서 뺀다
  if (HEALTH_PATH.test(req.path)) {
    next();
    return;
  }
  const start = Date.now();
  res.on('finish', () => {
    const account = (res.locals.account as { uuid?: string } | undefined)?.uuid;
    logger.info(
      {
        req_id: res.locals.reqId,
        ...(account ? { account } : {}),
        method: req.method,
        path: req.path,
        status: res.statusCode,
        ms: Date.now() - start,
      },
      'request',
    );
  });
  next();
};

const generalLimit = rateLimit({
  name: 'general-ip',
  limit: (c) => c.rate.generalIp,
  windowMs: MINUTE,
  key: ipKey,
});

// 3단계(경제) 경로(/characters/{uuid}/... 중 state 제외)는 전투 중 보고가 잦아 IP 한도를 따로 둔다
const ECONOMY_PATH = /^\/characters\/[^/]+\/(?!state(?:\/|$))[^/]+/;
const economyLimit = rateLimit({
  name: 'economy-ip',
  limit: (c) => c.rate.economyIp,
  windowMs: MINUTE,
  key: ipKey,
});

// 5단계: 친구·차단·신고(계정 단위 경로)는 같은 공유기에서 여러 명이 접속해도 걸리지 않게 IP 한도를 따로 둔다
const SOCIAL_PATH = /^\/(friends|blocks|reports)(?:\/|$)/;
const socialLimit = rateLimit({
  name: 'social-ip',
  limit: (c) => c.rate.socialIp,
  windowMs: MINUTE,
  key: ipKey,
});

/** 게임 데이터(initGameData)와 설정(initConfig)이 준비된 뒤에 호출한다. */
export function createApp(): Express {
  const app = express();
  app.disable('x-powered-by');
  app.set('trust proxy', getConfig().trustProxy);
  app.use(lifecycleGuard);
  app.use(requestLog);
  app.use((req, res, next) => {
    if (HEALTH_PATH.test(req.path)) next();
    else if (ECONOMY_PATH.test(req.path)) economyLimit(req, res, next);
    else if (SOCIAL_PATH.test(req.path)) socialLimit(req, res, next);
    else generalLimit(req, res, next);
  });
  app.use(express.json({ limit: '64kb' }));
  app.use(maintenanceGuard);
  app.use(createRouter());
  app.use(notFoundHandler);
  app.use(errorHandler);
  return app;
}
