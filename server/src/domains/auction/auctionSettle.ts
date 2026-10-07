// 정산: 체결(즉시 구매·낙찰), 만료, 취소, 입찰 반환. 호출하는 쪽이 listing 한 행을 이미 잠갔다.
// 결과는 전부 우편 INSERT와 원장이고, 판매자·입찰자 캐릭터 행은 잠그지 않는다(phase6_api.md 8.1).
import type { PoolClient } from 'pg';
import { getGameData } from '../../gamedata/loader';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { afterCommit } from '../../db/pool';
import { checkAuction } from '../antiabuse/holds';
import { noteAuction } from '../antiabuse/incomeMeter';
import { evaluateTrade } from '../antiabuse/tradeFlags';
import { insertItemLedger } from '../economy/economyRepository';
import { announceMail, type MailKind } from '../mail/mailNotify';
import { insertMail } from '../mail/mailRepository';
import * as repo from './auctionRepository';
import type { ListingRow } from './auctionRepository';
import { feeOf } from './auctionRules';

export interface SettleEnv {
  client: PoolClient;
  now: Date;
  /** 요청 처리 중이면 그 request_id, 틱이면 null(원장에 남기지 않는다) */
  requestId: string | null;
}

const DAY_MS = 86_400_000;

async function sendMail(
  env: SettleEnv,
  m: {
    characterId: number;
    kind: MailKind;
    listing: ListingRow;
    bidId?: number | null;
    itemKey?: string | null;
    count?: number | null;
    bind?: 'none' | 'account' | 'character' | null;
    gold?: number;
  },
): Promise<void> {
  const gold = m.gold ?? 0;
  const created = await insertMail(env.client, {
    characterId: m.characterId,
    kind: m.kind,
    listingId: m.listing.id,
    bidId: m.bidId ?? null,
    refItemKey: m.listing.itemKey,
    refCount: m.listing.count,
    itemKey: m.itemKey ?? null,
    count: m.count ?? null,
    bind: m.bind ?? null,
    gold,
    createdAt: env.now,
    expiresAt: new Date(env.now.getTime() + getGameData().auction.mailDays * DAY_MS),
  });
  announceMail(env.client, {
    characterUuid: created.characterUuid,
    mailUuid: created.uuid,
    kind: m.kind,
    refItemKey: m.listing.itemKey,
    gold,
    at: env.now,
  });
}

/** 닫힌 입찰의 예치금을 우편으로 돌려준다(입찰 상태는 호출 쪽이 이미 닫았다) */
export async function refundMail(env: SettleEnv, l: ListingRow, bid: repo.TopBid): Promise<void> {
  await sendMail(env, { characterId: bid.bidderCharacterId, kind: 'outbid', listing: l, bidId: bid.id, gold: bid.amount });
}

/** 입찰 한 건을 닫고(즉시 구매에 짐) 예치금을 우편으로 돌려준다 */
export async function refundBid(env: SettleEnv, l: ListingRow, bid: repo.TopBid, state: 'lost_to_buyout'): Promise<void> {
  await repo.closeBid(env.client, bid.id, state, env.now);
  await refundMail(env, l, bid);
}

/** 9.1 closeAsSold: 즉시 구매와 낙찰이 공유한다. buyer의 골드는 호출 쪽이 이미 냈다(낙찰은 입찰 때 예치) */
export async function closeAsSold(
  env: SettleEnv,
  l: ListingRow,
  price: number,
  buyer: { characterId: number; accountId: number },
  kind: 'buyout' | 'bid',
): Promise<void> {
  const { client, now } = env;
  const fee = feeOf(price, l.feePct);
  const payout = price - fee + l.deposit;
  await repo.closeListing(client, l.id, 'sold', kind, kind === 'buyout', now);

  const isEquipment = getGameData().economy.shop.equipment.has(l.itemBase);
  // 경매로 산 장비는 계정 귀속(재판매 불가). 재료·소모품은 귀속 없음
  const bind = isEquipment ? 'account' : 'none';
  await sendMail(env, { characterId: l.sellerCharacterId, kind: 'sold', listing: l, gold: payout });
  await sendMail(env, {
    characterId: buyer.characterId,
    kind: 'bought',
    listing: l,
    itemKey: l.itemKey,
    count: l.count,
    bind,
  });

  await insertItemLedger(client, l.sellerCharacterId, l.itemKey, -l.count, 0, 'auction', 'auction_sold', l.uuid, env.requestId);
  await insertItemLedger(client, buyer.characterId, l.itemKey, l.count, l.count, 'mail', 'auction_buy', l.uuid, env.requestId);

  const tradeId = await repo.insertTrade(client, {
    listingId: l.id,
    itemKey: l.itemKey,
    itemBase: l.itemBase,
    count: l.count,
    price,
    feePct: l.feePct,
    fee,
    depositReturned: l.deposit,
    sellerPayout: payout,
    kind,
    buyerCharacterId: buyer.characterId,
    buyerAccountId: buyer.accountId,
    sellerCharacterId: l.sellerCharacterId,
    sellerAccountId: l.sellerAccountId,
    tradedAt: now,
  });
  await repo.upsertPriceDaily(client, l.itemKey, resetBoundaries(now).dailyStartAt, l.count, price, now);
  await repo.insertSink(client, 'fee', fee, l.id, null, l.sellerCharacterId, now);

  // 9단계 7.4: 의심 거래 표시(거절하지 않고 기록만). 틱 정산에서도 같은 함수가 돈다
  const verdict = await evaluateTrade(client, { tradeId, itemKey: l.itemKey, count: l.count, price, sellerAccountId: l.sellerAccountId, buyerAccountId: buyer.accountId, now });
  // 12.3: 경제 속도 감시의 경매 집계. 판매자는 수입과 상대 위험 가중 수입, 구매자는 지출
  await noteAuction(client, l.sellerCharacterId, now, { inAmount: payout, inWeighted: Math.floor((payout * verdict.weightPct) / 100) });
  await noteAuction(client, buyer.characterId, now, { out: price });
  // 큰 이전은 30초 더티 워커를 기다리지 않고 판매자 계정의 경매 창을 커밋 직후 평가한다(결과는 체결에 영향이 없다)
  afterCommit(client, async () => {
    await checkAuction(l.sellerAccountId, now);
  });
}

/** 9.2: 입찰 없이 마감(expired) 또는 판매자 취소(cancelled). 아이템은 판매자 우편으로, 보증금은 소각 */
export async function returnToSeller(env: SettleEnv, l: ListingRow, status: 'expired' | 'cancelled'): Promise<void> {
  const { client, now } = env;
  await repo.closeListing(client, l.id, status, null, false, now);
  await sendMail(env, {
    characterId: l.sellerCharacterId,
    kind: status,
    listing: l,
    itemKey: l.itemKey,
    count: l.count,
    bind: 'none',
  });
  await insertItemLedger(client, l.sellerCharacterId, l.itemKey, -l.count, 0, 'auction', 'auction_return', l.uuid, env.requestId);
  await insertItemLedger(client, l.sellerCharacterId, l.itemKey, l.count, l.count, 'mail', 'auction_return', l.uuid, env.requestId);
  await repo.insertSink(client, 'deposit_forfeit', l.deposit, l.id, null, l.sellerCharacterId, now);
}

/** 마감된 등록 한 건의 정산(잠긴 상태): 입찰이 없으면 만료, 있으면 낙찰 */
export async function settleEnded(env: SettleEnv, l: ListingRow): Promise<'expired' | 'sold'> {
  if (l.currentBid === null || l.currentBidderCharacterId === null || l.currentBidderAccountId === null) {
    await returnToSeller(env, l, 'expired');
    return 'expired';
  }
  const top = await repo.getTopBid(env.client, l.id);
  if (top) await repo.closeBid(env.client, top.id, 'won', env.now);
  await closeAsSold(
    env,
    l,
    l.currentBid,
    { characterId: l.currentBidderCharacterId, accountId: l.currentBidderAccountId },
    'bid',
  );
  return 'sold';
}
