// 경매 순수 규칙: 수수료·보증금·최소 입찰가·분류·초성 검색. 모두 정수 계산이다(phase6_api.md 3절).
import { getGameData } from '../../gamedata/loader';
import type { AuctionData } from '../../gamedata/auctionData';
import { RARITY_ORDER } from '../../gamedata/economyData';
import { parseItemKey } from '../../utils/itemKey';

export type Category = 'weapon' | 'armor' | 'accessory' | 'material' | 'consumable';

/** 수수료 = ceil(price x pct / 100) 정수 */
export const feeOf = (price: number, pct: number): number => Math.floor((price * pct + 99) / 100);

/** 보증금 = clamp(round_half_up(buyout x bps / 10000), min, max) */
export function depositOf(buyout: number, a: AuctionData): number {
  const raw = Math.floor((buyout * a.depositBps + 5000) / 10000);
  return Math.min(a.depositMax, Math.max(a.depositMin, raw));
}

export interface BidState {
  buyoutPrice: number;
  startBid: number | null;
  currentBid: number | null;
}

/** 다음 최소 입찰가. 입찰 불가(start_bid 없음)면 null. 즉시 구매가를 넘지 않게 자른다 */
export function minBidOf(l: BidState, a: AuctionData): number | null {
  if (l.startBid === null) return null;
  if (l.currentBid === null) return Math.min(l.startBid, l.buyoutPrice);
  const stepped = Math.floor((l.currentBid * a.minBidStepBps + 9999) / 10000);
  return Math.min(l.buyoutPrice, Math.max(stepped, l.currentBid + 1));
}

/** 남은 시간 구간(미만 경계): 1 | 6 | 12 | 24 | 48. 정확한 시각은 노출하지 않는다 */
export function hoursLeftBand(endsAt: Date, now: Date, bands: number[]): number {
  const hours = (endsAt.getTime() - now.getTime()) / 3_600_000;
  for (const b of bands) if (hours < b) return b;
  return bands[bands.length - 1] as number;
}

export interface ItemFacts {
  base: string;
  enhance: number;
  isEquipment: boolean;
  category: Category;
  rarity: number | null;
  classOnly: 'warrior' | 'mage' | null;
}

/** 키에서 서버가 분류·등급·직업 제한을 정한다. 모르는 키(또는 비장비의 강화 표기)는 null */
export function factsOf(itemKey: string): ItemFacts | null {
  const p = parseItemKey(itemKey);
  if (!p) return null;
  const eco = getGameData().economy;
  const item = eco.items.get(p.base);
  if (!item) return null;
  const equip = eco.shop.equipment.get(p.base);
  if (equip) {
    if (p.level > eco.enhance.maxEnhance) return null;
    const category: Category =
      equip.category === 'Weapon' ? 'weapon' : equip.category === 'Ring' || equip.category === 'Necklace' ? 'accessory' : 'armor';
    const classOnly = equip.classOnly === 'warrior' || equip.classOnly === 'mage' ? equip.classOnly : null;
    return {
      base: p.base,
      enhance: p.level,
      isEquipment: true,
      category,
      rarity: RARITY_ORDER.indexOf(equip.rarity),
      classOnly,
    };
  }
  if (p.level !== 0) return null;
  return {
    base: p.base,
    enhance: 0,
    isEquipment: false,
    category: item.kind === 'material' ? 'material' : 'consumable',
    rarity: null,
    classOnly: null,
  };
}

export const feePctOf = (isEquipment: boolean, a: AuctionData): number => (isEquipment ? a.feePct.equipment : a.feePct.stack);

// ---------- 이름·초성 검색 ----------

const CHO = ['ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'];

function chosung(s: string): string {
  let out = '';
  for (const ch of s) {
    const c = ch.codePointAt(0) as number;
    out += c >= 0xac00 && c <= 0xd7a3 ? (CHO[Math.floor((c - 0xac00) / 588)] as string) : ch;
  }
  return out;
}

const JAMO_ONLY = /^[ㄱ-ㅎ]+$/;

/** 이름 부분 일치(자음만 입력하면 초성 검색)로 맞는 기본 id 목록 */
export function baseIdsMatching(q: string): string[] {
  const needle = q.trim().toLowerCase();
  if (!needle) return [];
  const initials = JAMO_ONLY.test(needle);
  const out: string[] = [];
  for (const [id, it] of getGameData().economy.items) {
    const name = it.name.toLowerCase();
    if (initials ? chosung(name).includes(needle) : name.includes(needle)) out.push(id);
  }
  return out;
}
