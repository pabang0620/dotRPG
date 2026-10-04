import { Router } from 'express';
import { createAuthRouter } from '../domains/auth/authRoutes';
import { createAuctionRouter } from '../domains/auction/auctionRoutes';
import { createBlocksRouter } from '../domains/blocks/blocksRoutes';
import { createAchievementRouter } from '../domains/achievements/achievementRoutes';
import { createCharacterRouter } from '../domains/characters/characterRoutes';
import { createDropRouter } from '../domains/drops/dropRoutes';
import { createDungeonRouter } from '../domains/dungeons/dungeonRoutes';
import { createEnhanceRouter } from '../domains/enhance/enhanceRoutes';
import { createFriendsRouter } from '../domains/friends/friendsRoutes';
import { createFieldRouter } from '../domains/fieldsessions/fieldRoutes';
import { createRelayRouter } from '../domains/relay/relayRoutes';
import { createGatheringRouter } from '../domains/gathering/gatheringRoutes';
import { createInventoryRouter } from '../domains/inventory/inventoryRoutes';
import { createKillRouter } from '../domains/kills/killRoutes';
import { createMailRouter } from '../domains/mail/mailRoutes';
import { createMatchRouter } from '../domains/match/matchRoutes';
import { createPartyInvitesRouter } from '../domains/partyinvites/partyInvitesRoutes';
import { createPartyRouter } from '../domains/party/partyRoutes';
import { createPartyRunRouter } from '../domains/partyruns/partyRunRoutes';
import { createReportsRouter } from '../domains/reports/reportsRoutes';
import { createRaidRouter } from '../domains/raids/raidRoutes';
import { createQuestRouter } from '../domains/quests/questRoutes';
import { createShopRouter } from '../domains/shop/shopRoutes';
import { createStarshopRouter } from '../domains/starshop/starshopRoutes';
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
  r.use(createStarshopRouter()); // 캐시샵(별조각 뽑기·교환)
  r.use(createEnhanceRouter());
  r.use(createInventoryRouter());
  r.use(createDungeonRouter());
  // 4단계: 파티 협동, 자동 매칭, 파티 판, 레이드 (모두 /characters/{uuid}/... 아래)
  r.use(createPartyRouter());
  r.use(createMatchRouter());
  r.use(createPartyRunRouter());
  r.use(createRaidRouter());
  // 5단계: 파티 초대(캐릭터 경로), 친구·차단·신고(계정 단위, 클라이언트 버전만 검사)
  r.use(createPartyInvitesRouter());
  r.use(createFriendsRouter());
  r.use(createBlocksRouter());
  r.use(createAchievementRouter()); // 업적·칭호
  r.use(createReportsRouter());
  // 6단계: 경매·우편(캐릭터 경로 아래, 데이터 버전까지 검사)
  r.use(createAuctionRouter());
  r.use(createMailRouter());
  // 8단계: 전투 중계 입장 티켓·전송 전환, 필드 파티 세션(캐릭터 경로 아래, 데이터 버전까지 검사)
  r.use(createRelayRouter());
  r.use(createFieldRouter());
  return r;
}
