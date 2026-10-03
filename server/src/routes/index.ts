import { Router } from 'express';
import { createAuthRouter } from '../domains/auth/authRoutes';
import { createCharacterRouter } from '../domains/characters/characterRoutes';
import { createDropRouter } from '../domains/drops/dropRoutes';
import { createDungeonRouter } from '../domains/dungeons/dungeonRoutes';
import { createEnhanceRouter } from '../domains/enhance/enhanceRoutes';
import { createGatheringRouter } from '../domains/gathering/gatheringRoutes';
import { createInventoryRouter } from '../domains/inventory/inventoryRoutes';
import { createKillRouter } from '../domains/kills/killRoutes';
import { createQuestRouter } from '../domains/quests/questRoutes';
import { createShopRouter } from '../domains/shop/shopRoutes';
import { createSystemRouter } from '../domains/system/systemRoutes';

export function createRouter(): Router {
  const r = Router();
  r.use(createSystemRouter());
  r.use(createAuthRouter());
  // 버전(426)과 인증은 createCharacterRouter의 /characters 공통 미들웨어가 처리한다.
  // /characters/{uuid}/... 3단계(경제) 라우터는 반드시 그 뒤에 등록한다(res.locals.account를 쓴다).
  r.use(createCharacterRouter());
  r.use(createKillRouter());
  r.use(createDropRouter());
  r.use(createGatheringRouter());
  r.use(createQuestRouter());
  r.use(createShopRouter());
  r.use(createEnhanceRouter());
  r.use(createInventoryRouter());
  r.use(createDungeonRouter());
  return r;
}
