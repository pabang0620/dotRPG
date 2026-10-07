// 한 경제 요청의 작업 상자: 캐릭터 행이 잠긴 트랜잭션 안에서 골드·아이템·경험치를 바꾸고
// 바뀔 때마다 원장 한 줄을 같이 남긴다(원장 없이 잔액만 바꾸지 않는다).
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { logger } from '../../utils/logger';
import { getGameData } from '../../gamedata/loader';
import { isSweepTicketKey } from '../../gamedata/sweepData';
import { AppError } from '../../utils/AppError';
import { parseItemKey } from '../../utils/itemKey';
import { IncomeNote } from '../antiabuse/incomeMeter';
import * as repo from './economyRepository';
import { strongerBind, type Bind, type Consumed, type LockedChar, type StackLocation } from './economyRepository';

export interface Delta {
  gold?: number;
  level?: number;
  xp?: number;
  stacks?: { item_key: string; location: string; bind: Bind; count: number }[];
  worn?: { slot: number; item_key: string | null }[];
}

export class EconCtx {
  gold: number;
  level: number;
  xp: number;
  private readonly startLevel: number;
  private readonly startXp: number;
  private goldChanged = false;
  /** 9단계: 경제 속도 감시용 시간별 집계의 요청 안 누계(12.3). runEconomy가 성공 직후 같은 트랜잭션에서 올린다 */
  readonly income = new IncomeNote();
  private readonly stacks = new Map<string, { item_key: string; location: string; bind: Bind; count: number }>();
  private readonly worn = new Map<number, string | null>();

  constructor(
    readonly client: PoolClient,
    readonly char: LockedChar,
    readonly requestId: string,
    readonly now: Date,
  ) {
    this.gold = char.gold;
    this.level = char.level;
    this.xp = char.xp;
    this.startLevel = char.level;
    this.startXp = char.xp;
  }

  /** 아이템 종류가 정하는 귀속 하한(items.json). 어떤 경로로도 이보다 약해지지 않는다 */
  bindOf(itemKey: string): Bind {
    const base = parseItemKey(itemKey)?.base ?? itemKey;
    return getGameData().economy.items.get(base)?.bind ?? 'none';
  }

  /** 획득 경로가 정하는 귀속(phase6 5.1): 하한 위에 경로 규칙을 더 강하게만 덮는다 */
  bindFor(reason: string, itemKey: string): Bind {
    const floor = this.bindOf(itemKey);
    const base = parseItemKey(itemKey)?.base;
    const isEquipment = base !== undefined && getGameData().economy.shop.equipment.has(base);
    if (!isEquipment) return floor;
    if (reason === 'quest_reward') return strongerBind(floor, 'character');
    if (reason === 'shop_buy') return strongerBind(floor, 'account');
    // 9단계 A7: 별조각 장비 뽑기는 계정 귀속(상점 구매와 같다). 이미 가진 옛 스택은 소급하지 않는다
    if (reason === 'gacha') return strongerBind(floor, 'account');
    return floor;
  }

  private noteStack(location: string, key: string, bind: Bind, count: number): void {
    this.stacks.set(`${location}|${key}|${bind}`, { item_key: key, location, bind, count });
  }

  stackCount(location: StackLocation, key: string): Promise<number> {
    return repo.stackCount(this.client, this.char.id, location, key);
  }

  /** bind를 주지 않으면 획득 경로(reason)가 정한다. 준 값도 키의 하한보다 약해지지 않는다 */
  async addItem(
    location: StackLocation,
    key: string,
    n: number,
    reason: string,
    ref: string,
    bind?: Bind,
  ): Promise<void> {
    if (n <= 0) throw new Error('addItem 수량은 양수여야 합니다');
    // 10단계 E7: 던전 클리어권은 계정 지갑에만 있다. 가방·창고에 넣는 길을 프로그래밍 오류로 막는다
    if (isSweepTicketKey(getGameData().sweep, key)) throw new Error(`클리어권 ${key} 은 가방에 넣을 수 없습니다`);
    const b = bind ? strongerBind(bind, this.bindOf(key)) : this.bindFor(reason, key);
    const rowCount = await repo.upsertStack(this.client, this.char.id, location, key, n, b);
    const total = await repo.stackCount(this.client, this.char.id, location, key);
    await repo.insertItemLedger(this.client, this.char.id, key, n, total, location, reason, ref, this.requestId);
    this.noteStack(location, key, b, rowCount);
    this.income.item(key, n, reason);
  }

  /** 모자라면 null(아무것도 바꾸지 않는다). 강한 귀속부터 소모하고 소모한 귀속별 수량을 돌려준다 */
  async removeItem(
    location: StackLocation,
    key: string,
    n: number,
    reason: string,
    ref: string,
    onlyBind?: Bind,
  ): Promise<Consumed[] | null> {
    if (n <= 0) throw new Error('removeItem 수량은 양수여야 합니다');
    const consumed = await repo.decStack(this.client, this.char.id, location, key, n, onlyBind);
    if (consumed === null) return null;
    const total = await repo.stackCount(this.client, this.char.id, location, key);
    await repo.insertItemLedger(this.client, this.char.id, key, -n, total, location, reason, ref, this.requestId);
    for (const c of consumed) this.noteStack(location, key, c.bind, c.left);
    return consumed;
  }

  /** 소모한 귀속을 그대로 다른 위치·키에 넣는다(창고 이동, 강화 결과) */
  async addConsumed(
    location: StackLocation,
    key: string,
    consumed: Consumed[],
    reason: string,
    ref: string,
  ): Promise<void> {
    for (const c of consumed) await this.addItem(location, key, c.n, reason, ref, c.bind);
  }

  wornKey(slot: number): Promise<string | null> {
    return repo.getWornKey(this.client, this.char.id, slot);
  }

  /** 빈 슬롯에 착용 행을 만든다 */
  async wornPut(slot: number, key: string, reason: string, ref: string, bind?: Bind): Promise<void> {
    await repo.insertWorn(this.client, this.char.id, slot, key, bind ? strongerBind(bind, this.bindOf(key)) : this.bindFor(reason, key));
    await repo.insertItemLedger(this.client, this.char.id, key, 1, 1, 'worn', reason, ref, this.requestId);
    this.worn.set(slot, key);
  }

  /** 착용 행을 지운다(키는 호출 쪽이 이미 읽었다). 그 행의 귀속을 돌려준다 */
  async wornRemove(slot: number, key: string, reason: string, ref: string): Promise<Bind> {
    const bind = await repo.deleteWorn(this.client, this.char.id, slot);
    await repo.insertItemLedger(this.client, this.char.id, key, -1, 0, 'worn', reason, ref, this.requestId);
    this.worn.set(slot, null);
    return bind;
  }

  /** 착용 슬롯의 키를 바꾼다(강화 결과): 옛 키 -1, 새 키 +1 */
  async wornReplace(slot: number, oldKey: string, newKey: string, reason: string, ref: string): Promise<void> {
    await repo.updateWornKey(this.client, this.char.id, slot, newKey);
    await repo.insertItemLedger(this.client, this.char.id, oldKey, -1, 0, 'worn', reason, ref, this.requestId);
    await repo.insertItemLedger(this.client, this.char.id, newKey, 1, 1, 'worn', reason, ref, this.requestId);
    this.worn.set(slot, newKey);
  }

  /** 골드를 바꾼다. 잔액이 모자라면 NOT_ENOUGH_GOLD (호출 쪽이 먼저 확인해 자세한 정보를 준다) */
  async changeGold(delta: number, reason: string, ref: string): Promise<void> {
    if (delta === 0) return;
    let next = this.gold + delta;
    if (next < 0) {
      throw new AppError(422, '골드가 모자랍니다.', 'NOT_ENOUGH_GOLD', { need: -delta, have: this.gold });
    }
    // 클라이언트 Inventory가 int라 모든 골드 획득 경로에서 상한을 건다. 보상 경로는 거절하지 않고 상한까지만 주고 로그를 남긴다
    // (우편은 수령 전에 상한을 확인해 거절하므로 여기에 닿지 않는다)
    const cap = getConfig().auction.goldClientMax;
    if (next > cap) {
      logger.warn({ character_id: this.char.id, reason, delta, balance: this.gold, cap }, 'gold.cap_reached');
      next = Math.max(this.gold, cap);
      delta = next - this.gold;
      if (delta === 0) return;
    }
    await repo.updateGold(this.client, this.char.id, next);
    await repo.insertGoldLedger(this.client, this.char.id, delta, next, reason, ref, this.requestId);
    this.gold = next;
    this.goldChanged = true;
    this.income.gold(delta, reason);
  }

  /** Progression.AddXp: 만렙이거나 0 이하면 아무 일도 없다. 레벨업을 반복하고 만렙에 닿으면 xp=0 */
  async grantXp(amount: number, reason: string, ref: string): Promise<{ granted: number; leveledUp: boolean }> {
    const p = getGameData().economy.progression;
    if (amount <= 0 || this.level >= p.maxLevel) return { granted: 0, leveledUp: false };
    let level = this.level;
    let xp = this.xp + amount;
    while (level < p.maxLevel && xp >= (p.xpToNext[level - 1] as number)) {
      xp -= p.xpToNext[level - 1] as number;
      level++;
    }
    if (level >= p.maxLevel) xp = 0;
    await repo.updateLevelXp(this.client, this.char.id, level, xp);
    await repo.insertXpLedger(this.client, this.char.id, amount, level, xp, reason, ref, this.requestId);
    const leveledUp = level > this.level;
    this.level = level;
    this.xp = xp;
    this.income.xp(amount, reason);
    return { granted: amount, leveledUp };
  }

  delta(): Delta {
    const d: Delta = {};
    if (this.goldChanged) d.gold = this.gold;
    if (this.level !== this.startLevel || this.xp !== this.startXp) {
      d.level = this.level;
      d.xp = this.xp;
    }
    if (this.stacks.size > 0) d.stacks = [...this.stacks.values()];
    if (this.worn.size > 0) d.worn = [...this.worn.entries()].map(([slot, item_key]) => ({ slot, item_key }));
    return d;
  }
}
