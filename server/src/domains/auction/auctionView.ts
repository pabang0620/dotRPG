// 경매 응답 모양(ListingView, MyListingView). 내부 bigint 키는 싣지 않고 uuid만 싣는다.
import { getGameData } from '../../gamedata/loader';
import type { ListingRow } from './auctionRepository';
import { hoursLeftBand, minBidOf } from './auctionRules';

export interface ListingView {
  id: string;
  item_key: string;
  count: number;
  seller_name: string;
  category: string;
  rarity: number | null;
  enhance: number;
  class_only: string | null;
  buyout: number;
  start_bid: number | null;
  current_bid: number | null;
  bid_count: number;
  min_bid: number | null;
  unit_price: number;
  hours_left_band: number;
  my_bid: boolean;
  will_bind: 'account' | null;
}

export interface MyListingView extends ListingView {
  deposit: number;
  fee_pct: number;
  ends_at: string;
  extend_count: number;
}

export function viewOf(l: ListingRow, sellerName: string, myCharacterId: number, now: Date): ListingView {
  const a = getGameData().auction;
  const isEquipment = getGameData().economy.shop.equipment.has(l.itemBase);
  return {
    id: l.uuid,
    item_key: l.itemKey,
    count: l.count,
    seller_name: sellerName,
    category: l.category,
    rarity: l.rarity,
    enhance: l.enhance,
    class_only: l.classOnly,
    buyout: l.buyoutPrice,
    start_bid: l.startBid,
    current_bid: l.currentBid,
    bid_count: l.bidCount,
    min_bid: minBidOf(l, a),
    unit_price: l.buyoutPrice / l.count,
    hours_left_band: hoursLeftBand(l.endsAt, now, a.timeBands),
    my_bid: l.currentBidderCharacterId === myCharacterId,
    will_bind: isEquipment ? 'account' : null,
  };
}

export function myViewOf(l: ListingRow, sellerName: string, myCharacterId: number, now: Date): MyListingView {
  return {
    ...viewOf(l, sellerName, myCharacterId, now),
    deposit: l.deposit,
    fee_pct: l.feePct,
    ends_at: l.endsAt.toISOString(),
    extend_count: l.extendCount,
  };
}
