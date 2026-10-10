import { Router } from 'express';
import { createAuthRouter } from '../domains/auth/authRoutes';
import { createAuctionRouter } from '../domains/auction/auctionRoutes';
import { createBlocksRouter } from '../domains/blocks/blocksRoutes';
import { createAchievementRouter } from '../domains/achievements/achievementRoutes';
import { createCharacterRouter } from '../domains/characters/characterRoutes';
import { createDropRouter } from '../domains/drops/dropRoutes';
import { createDungeonRouter } from '../domains/dungeons/dungeonRoutes';
import { createEnhanceRouter } from '../domains/enhance/enhanceRoutes';
import { createPromoteRouter } from '../domains/promote/promoteRoutes';
import { createRaidShopRouter } from '../domains/raidshop/raidShopRoutes';
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
import { createPresenceRouter } from '../domains/antiabuse/presenceRoutes';
import { createCareerGrantRouter } from '../domains/characters/careerGrantRoutes';
import { createSweepRouter } from '../domains/sweep/sweepRoutes';
import { createReviveRouter } from '../domains/revive/reviveRoutes';
import { createPaymentsRouter } from '../domains/payments/paymentsRoutes';
import { createLevelRewardsRouter } from '../domains/levelrewards/levelRewardsRoutes';
import { createDailyRouter } from '../domains/dailies/dailyRoutes';
import { createClientErrorRouter } from '../domains/clienterrors/clientErrorRoutes';
import { createSealedBoxRouter } from '../domains/sealedbox/sealedBoxRoutes';
import { createWithdrawalRouter } from '../domains/withdrawal/withdrawalRoutes';

export function createRouter(): Router {
  const r = Router();
  r.use(createSystemRouter());
  r.use(createAuthRouter());
  // 회원 탈퇴 W1~W3(계정 단위라 /characters 앞에 둔다)
  r.use(createWithdrawalRouter());
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
  r.use(createPromoteRouter());
  r.use(createRaidShopRouter()); // 13단계: 레이드 상점
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
  // 9단계: 프레즌스(P1~P3), 전직·각성 서버 기록(C1~C4). 캐릭터 경로 아래라 데이터 버전까지 검사한다
  r.use(createPresenceRouter());
  r.use(createCareerGrantRouter());
  // 10단계: 던전 소탕·클리어권 구매·주간 활동(캐릭터 경로 아래, 데이터 버전까지 검사). SWEEP_ENABLED가 꺼져 있으면 503
  r.use(createSweepRouter());
  // 12단계: 부활 코인(캐릭터 경로 아래, 데이터 버전까지 검사)
  r.use(createReviveRouter());
  // 11단계: 별조각 Steam 결제(계정 단위 /payments/*, 클라이언트 버전만 검사). PAYMENTS_ENABLED가 꺼져 있으면 B1·B2는 503
  r.use(createPaymentsRouter());
  // 13단계: 레벨 달성 보상(캐릭터 경로에서 현재 레벨 검사, 계정당 단계별 1회)
  r.use(createLevelRewardsRouter());
  // 14단계: 봉인된 상자(캐시샵 뽑기·상자 아이템 열기), 강화권(enhance), 성장 패스(level-rewards)
  r.use(createSealedBoxRouter());
  // 14단계: 일일 의뢰(캐릭터 경로 아래, 데이터 버전까지 검사)
  r.use(createDailyRouter());
  // 15단계: 클라이언트 예외 보고(계정 단위, 버전 검사 없음)
  r.use(createClientErrorRouter());
  return r;
}
