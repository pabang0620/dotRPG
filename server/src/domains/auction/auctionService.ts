// 경매 쓰기 요청: 등록, 취소, 즉시 구매, 입찰. 락 순서는 요청자 캐릭터 행 -> auction_listings 한 행(phase6_api.md 8.1).
import { randomUUID } from 'node:crypto';
import { getConfig } from '../../config/env';
import { withTransaction } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import type { EconCtx } from '../economy/economyContext';
import { insertItemLedger, lockCharacter } from '../economy/economyRepository';
import type { EconResult, StoredResult } from '../economy/economyService';
import * as mailRepo from '../mail/mailRepository';
import { FlagError, recordFlag, runAuction } from './auctionFlags';
import * as repo from './auctionRepository';
import type { ListingRow } from './auctionRepository';
import { limitsOf, referenceOf } from './auctionPricing';
import { closeAsSold, refundBid, refundMail, returnToSeller } from './auctionSettle';
import { depositOf, factsOf, feePctOf, hoursLeftBand, minBidOf } from './auctionRules';
import { myViewOf } from './auctionView';
import type { BidBody, BuyoutBody, ListBody } from './auctionValidation';

const DAY_MS = 86_400_000;
const NOT_FOUND_CHAR = () => new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
const LISTING_NOT_FOUND = () => new AppError(404, '등록을 찾을 수 없습니다.', 'LISTING_NOT_FOUND');

// ---------- A4 등록 ----------

export function listItem(accountId: number, characterUuid: string, body: ListBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runAuction({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/auction/listings',
    requestId,
    payload,
    handler: (ctx) => processList(ctx, body),
  });
}

async function processList(ctx: EconCtx, body: ListBody): Promise<EconResult> {
  const cfg = getConfig().auction;
  const data = getGameData().auction;
  const eco = getGameData().economy;
  const now = getNow();
  const flagBase = { accountId: ctx.char.accountId, characterId: ctx.char.id };

  // 1. 자격(둘 중 하나라도 미달이면 불가). 구매·입찰은 가능
  const created = await repo.accountCreatedAt(ctx.client, ctx.char.accountId);
  const ageOk = created.getTime() <= now.getTime() - cfg.minAccountAgeDays * DAY_MS;
  if (ctx.char.level < cfg.minLevel || !ageOk) {
    throw new FlagError(
      422,
      '아직 경매에 등록할 수 없습니다.',
      'LISTING_GATE',
      { ...flagBase, kind: 'gate', severity: 1, detail: { level: ctx.char.level } },
      { need_level: cfg.minLevel, need_days: cfg.minAccountAgeDays },
    );
  }

  // 2. 아이템(5.2절)
  const facts = factsOf(body.item_key);
  if (!facts) throw new AppError(404, '알 수 없는 아이템입니다.', 'ITEM_UNKNOWN');
  const item = eco.items.get(facts.base);
  if (!item || item.kind === 'currency') throw new AppError(404, '알 수 없는 아이템입니다.', 'ITEM_UNKNOWN');
  if (item.bind !== 'none') {
    throw new AppError(422, '거래할 수 없는 아이템입니다.', 'NOT_TRADABLE', { reason: 'BASE_BOUND' });
  }
  const maxCount = facts.isEquipment ? 1 : data.maxStack;
  if (body.count > maxCount) throw new AppError(422, '등록 수량이 올바르지 않습니다.', 'BAD_COUNT', { max: maxCount });
  const tradable = await ctx.client.query<{ bind: string; count: number }>(
    "SELECT bind, count FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key = $2",
    [ctx.char.id, body.item_key],
  );
  const haveNone = tradable.rows.find((r) => r.bind === 'none')?.count ?? 0;
  const haveAny = tradable.rows.reduce((a, r) => a + r.count, 0);
  if (haveNone === 0 && haveAny > 0) throw new AppError(422, '귀속된 아이템은 등록할 수 없습니다.', 'ITEM_BOUND');
  if (haveNone < body.count) {
    throw new AppError(422, '가방에 아이템이 부족합니다.', 'NOT_ENOUGH_ITEMS', { need: body.count, have: haveNone });
  }
  const ref = await referenceOf(ctx.client, body.item_key, now);
  const limits = limitsOf(ref, body.count);
  if (!limits) throw new AppError(422, '거래할 수 없는 아이템입니다.', 'NOT_TRADABLE', { reason: 'NO_PRICE' });

  // 3. 기간
  if (!data.durations.includes(body.hours)) {
    throw new AppError(422, '등록 기간이 올바르지 않습니다.', 'BAD_DURATION', { allowed: data.durations });
  }
  // 4. 동시 등록(캐릭터 행이 잠겨 있어 정확히 센다)
  const active = await repo.countActiveOfSeller(ctx.client, ctx.char.id);
  if (active >= data.maxListings) {
    throw new AppError(409, '동시에 등록할 수 있는 수를 넘었습니다.', 'LISTING_LIMIT', { limit: data.maxListings });
  }
  // 5. 가격
  if (body.buyout < limits.min || body.buyout > limits.max) {
    throw new FlagError(
      422,
      '가격이 허용 범위를 벗어났습니다.',
      'PRICE_OUT_OF_RANGE',
      { ...flagBase, kind: 'price_band', severity: 1, detail: { item_key: body.item_key, buyout: body.buyout, min: limits.min, max: limits.max } },
      { min: limits.min, max: limits.max, source: ref.source },
    );
  }
  if (body.start_bid !== undefined && (body.start_bid >= body.buyout || body.start_bid < limits.min)) {
    throw new AppError(422, '입찰 시작가가 올바르지 않습니다.', 'START_BID_INVALID', { min: limits.min, max: body.buyout - 1 });
  }
  // 6. 보증금
  const deposit = depositOf(body.buyout, data);
  if (ctx.gold < deposit) {
    throw new AppError(422, '보증금이 모자랍니다.', 'NOT_ENOUGH_GOLD', { need: deposit, have: ctx.gold, for: 'deposit' });
  }

  // 7. 효과: 등록 행 -> 가방 -n / 경매 +n -> 보증금
  const listing = await repo.insertListing(ctx.client, {
    uuid: randomUUID(),
    sellerCharacterId: ctx.char.id,
    sellerAccountId: ctx.char.accountId,
    itemKey: body.item_key,
    itemBase: facts.base,
    enhance: facts.enhance,
    count: body.count,
    category: facts.category,
    rarity: facts.rarity,
    classOnly: facts.classOnly,
    buyoutPrice: body.buyout,
    startBid: body.start_bid ?? null,
    deposit,
    feePct: feePctOf(facts.isEquipment, data),
    durationHours: body.hours,
    createdAt: now,
    endsAt: new Date(now.getTime() + body.hours * 3_600_000),
  });
  const taken = await ctx.removeItem('bag', body.item_key, body.count, 'auction_list', listing.uuid, 'none');
  if (taken === null) throw new AppError(422, '가방에 아이템이 부족합니다.', 'NOT_ENOUGH_ITEMS');
  await insertItemLedger(ctx.client, ctx.char.id, body.item_key, body.count, body.count, 'auction', 'auction_list', listing.uuid, ctx.requestId);
  await ctx.changeGold(-deposit, 'auction_deposit', listing.uuid);

  const name = await repo.characterName(ctx.client, ctx.char.id);
  return {
    status: 201,
    data: {
      listing: myViewOf(listing, name, ctx.char.id, now),
      deposit,
      fee_pct: listing.feePct,
      delta: ctx.delta(),
    },
  };
}

// ---------- A6 즉시 구매, A7 입찰 ----------

/** 락 직후 공통 판정(없음, 상태, 마감, 내 계정, 쌍 한도) */
async function lockAndCheck(ctx: EconCtx, listingUuid: string, price: (l: ListingRow) => number): Promise<{ l: ListingRow; now: Date }> {
  const l = await repo.lockListingByUuid(ctx.client, listingUuid);
  if (!l) throw LISTING_NOT_FOUND();
  if (l.status !== 'active') throw new AppError(409, '이미 팔렸거나 끝난 등록입니다.', 'LISTING_NOT_ACTIVE');
  const now = getNow(); // 락을 잡은 뒤의 시각
  if (l.endsAt.getTime() <= now.getTime()) throw new AppError(409, '마감된 등록입니다.', 'LISTING_ENDED');
  const flagBase = { accountId: ctx.char.accountId, characterId: ctx.char.id };
  if (l.sellerAccountId === ctx.char.accountId) {
    throw new FlagError(403, '내 등록은 사거나 입찰할 수 없습니다.', 'OWN_LISTING', {
      ...flagBase,
      kind: 'self_account',
      severity: 2,
      detail: { listing: l.uuid },
    });
  }
  const cfg = getConfig().auction;
  await repo.lockPair(ctx.client, l.sellerAccountId, ctx.char.accountId);
  const today = await repo.pairToday(ctx.client, l.sellerAccountId, ctx.char.accountId, resetBoundaries(now).dailyStartAt, l.id);
  const p = price(l);
  if (today.trades >= cfg.pairDailyTrades || today.gold + p > cfg.pairDailyGold) {
    throw new FlagError(
      422,
      '같은 상대와 하루에 거래할 수 있는 한도를 넘었습니다.',
      'PAIR_LIMIT',
      { ...flagBase, kind: 'pair_limit', severity: 1, detail: { listing: l.uuid, trades: today.trades, gold: today.gold } },
      { limit: { trades: cfg.pairDailyTrades, gold: cfg.pairDailyGold } },
    );
  }
  return { l, now };
}

function needGold(ctx: EconCtx, need: number, extra: Record<string, unknown> = {}): void {
  if (ctx.gold < need) throw new AppError(422, '골드가 모자랍니다.', 'NOT_ENOUGH_GOLD', { need, have: ctx.gold, ...extra });
}

/** A6 5와 같은 효과: 대금을 내고, 직전 최고 입찰을 돌려주고, 체결한다 */
async function doBuyout(ctx: EconCtx, l: ListingRow, now: Date): Promise<EconResult> {
  await ctx.changeGold(-l.buyoutPrice, 'auction_buyout', l.uuid);
  const env = { client: ctx.client, now, requestId: ctx.requestId };
  const top = await repo.getTopBid(ctx.client, l.id);
  if (top) await refundBid(env, l, top, 'lost_to_buyout');
  await closeAsSold(env, l, l.buyoutPrice, { characterId: ctx.char.id, accountId: ctx.char.accountId }, 'buyout');
  const mailId = await mailRepo.findMailOfListing(ctx.client, l.id, ctx.char.id, 'bought');
  const isEquipment = getGameData().economy.shop.equipment.has(l.itemBase);
  return {
    status: 200,
    data: {
      result: 'bought',
      price: l.buyoutPrice,
      mail_id: mailId,
      bind: isEquipment ? 'account' : 'none',
      delta: ctx.delta(),
    },
  };
}

export function buyout(accountId: number, characterUuid: string, listingUuid: string, body: BuyoutBody): Promise<StoredResult> {
  const { request_id: requestId } = body;
  return runAuction({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/auction/listings/:listing_id/buyout',
    requestId,
    payload: { listing_id: listingUuid },
    handler: async (ctx) => {
      const { l, now } = await lockAndCheck(ctx, listingUuid, (x) => x.buyoutPrice);
      needGold(ctx, l.buyoutPrice);
      return doBuyout(ctx, l, now);
    },
  });
}

export function bid(accountId: number, characterUuid: string, listingUuid: string, body: BidBody): Promise<StoredResult> {
  const { request_id: requestId, ...rest } = body;
  return runAuction({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/auction/listings/:listing_id/bids',
    requestId,
    payload: { listing_id: listingUuid, ...rest },
    handler: async (ctx) => {
      const a = getGameData().auction;
      const { l, now } = await lockAndCheck(ctx, listingUuid, (x) => Math.min(body.amount, x.buyoutPrice));
      if (l.startBid === null) throw new AppError(422, '즉시 구매만 가능한 등록입니다.', 'NOT_BIDDABLE');
      if (l.currentBidderAccountId === ctx.char.accountId) {
        throw new AppError(409, '이미 최고 입찰자입니다.', 'ALREADY_TOP_BIDDER');
      }
      if (body.amount >= l.buyoutPrice) {
        needGold(ctx, l.buyoutPrice);
        return doBuyout(ctx, l, now);
      }
      const minBid = minBidOf(l, a) as number;
      if (body.amount < minBid) throw new AppError(409, '최소 입찰가보다 낮습니다.', 'BID_TOO_LOW', { min_bid: minBid });
      needGold(ctx, body.amount);

      const env = { client: ctx.client, now, requestId: ctx.requestId };
      const prev = await repo.getTopBid(ctx.client, l.id);
      if (prev) await repo.closeBid(ctx.client, prev.id, 'outbid', now);
      const bidUuid = randomUUID();
      await repo.insertBid(ctx.client, bidUuid, l.id, ctx.char.id, ctx.char.accountId, body.amount, ctx.requestId, now);
      await ctx.changeGold(-body.amount, 'auction_bid', bidUuid);

      // 마감 연장(저격 방지): 구간 안 입찰이면 늘리고 횟수 한도까지
      let endsAt = l.endsAt;
      let extendCount = l.extendCount;
      let extended = false;
      if (endsAt.getTime() - now.getTime() <= a.extendWindowMinutes * 60_000 && extendCount < a.extendMax) {
        endsAt = new Date(endsAt.getTime() + a.extendMinutes * 60_000);
        extendCount++;
        extended = true;
      }
      await repo.updateBid(ctx.client, l.id, body.amount, ctx.char.id, ctx.char.accountId, endsAt, extendCount);
      if (prev) {
        // 이미 outbid로 닫았다: 예치금 반환 우편만 만든다
        await refundMail(env, l, prev);
      }
      const after = { ...l, currentBid: body.amount };
      return {
        status: 200,
        data: {
          result: 'bid',
          current_bid: body.amount,
          min_bid: minBidOf(after, a),
          hours_left_band: hoursLeftBand(endsAt, now, a.timeBands),
          extended,
          delta: ctx.delta(),
        },
      };
    },
  });
}

// ---------- A5 취소 ----------

export async function cancelListing(accountId: number, characterUuid: string, listingUuid: string, requestId: string | null = null): Promise<Record<string, unknown>> {
  try {
    return await withTransaction(async (client) => {
      const c = await lockCharacter(client, accountId, characterUuid);
      if (!c) throw NOT_FOUND_CHAR();
      const l = await repo.lockListingByUuid(client, listingUuid);
      if (!l) throw LISTING_NOT_FOUND();
      if (l.sellerCharacterId !== c.id) {
        throw new FlagError(404, '등록을 찾을 수 없습니다.', 'LISTING_NOT_FOUND', {
          accountId,
          characterId: c.id,
          kind: 'foreign_id',
          severity: 1,
          detail: { listing: listingUuid },
        });
      }
      if (l.status === 'cancelled') {
        // 응답을 못 받고 다시 보낸 경우: 상태 기반 멱등
        const mail = await mailRepo.findMailOfListing(client, l.id, c.id, 'cancelled');
        return { already: true, mail_id: mail, forfeited_deposit: l.deposit };
      }
      if (l.status !== 'active') throw new AppError(409, '이미 팔렸거나 끝난 등록입니다.', 'LISTING_NOT_ACTIVE');
      const now = getNow();
      if (l.endsAt.getTime() <= now.getTime()) throw new AppError(409, '마감된 등록입니다.', 'LISTING_ENDED');
      if (l.currentBid !== null) throw new AppError(409, '입찰자가 있어 취소할 수 없습니다.', 'HAS_BIDS');
      await returnToSeller({ client, now, requestId }, l, 'cancelled');
      const mail = await mailRepo.findMailOfListing(client, l.id, c.id, 'cancelled');
      return { already: false, mail_id: mail, forfeited_deposit: l.deposit };
    });
  } catch (err) {
    if (err instanceof FlagError) await recordFlag(err.flag);
    throw err;
  }
}
