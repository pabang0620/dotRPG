// 관리자 listener의 Express 앱(phase7_ops.md 5.2). 공개 앱의 versionCheck·게임 속도 제한을 지나지 않고
// 출처 CIDR, 세션 토큰, 역할, 속도 제한, 감사 미들웨어를 쓴다.
import express, { type Express } from 'express';
import { registerAllJobs } from '../ops/jobs';
import { createAdminsRouter } from './admins/adminsRoutes';
import { createAdminAuthRouter } from './auth/adminAuthRoutes';
import { originGuard } from './common/adminAuth';
import { adminErrorHandler, adminNotFound } from './common/adminErrors';
import { createEconomyAdminRouter } from './economy/economyAdminRoutes';
import { createEconomyHoldsRouter } from './economyholds/economyHoldsRoutes';
import { createHeldRunsRouter } from './heldruns/heldRunsRoutes';
import { createMailCampaignsRouter } from './mailcampaigns/mailCampaignsRoutes';
import { createMaintenanceAdminRouter } from './maintenance/maintenanceAdminRoutes';
import { createOpsRouter } from './ops/opsRoutes';
import { createPlayersRouter } from './players/playersRoutes';
import { createReportsAdminRouter } from './reports/reportsRoutes';
import { createSanctionsRouter } from './sanctions/sanctionsRoutes';
import { createWatchRouter } from './watch/watchRoutes';

export function createAdminApp(): Express {
  // OP2·OP3가 작업 목록을 쓴다(여러 번 불러도 한 번만 등록)
  registerAllJobs();
  const app = express();
  app.disable('x-powered-by');
  app.use((_req, res, next) => {
    res.setHeader('Cache-Control', 'no-store');
    next();
  });
  app.use(originGuard);
  app.use(express.json({ limit: '16kb' }));
  app.use(createAdminAuthRouter());
  app.use(createAdminsRouter());
  app.use(createPlayersRouter());
  app.use(createSanctionsRouter());
  app.use(createReportsAdminRouter());
  app.use(createHeldRunsRouter());
  app.use(createWatchRouter());
  app.use(createEconomyAdminRouter());
  app.use(createEconomyHoldsRouter());
  app.use(createMailCampaignsRouter());
  app.use(createMaintenanceAdminRouter());
  app.use(createOpsRouter());
  app.use(adminNotFound);
  app.use(adminErrorHandler);
  return app;
}
