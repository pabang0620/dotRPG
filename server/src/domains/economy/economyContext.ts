// 한 경제 요청의 작업 상자: 캐릭터 행이 잠긴 트랜잭션 안에서 골드·아이템·경험치를 바꾸고
// 바뀔 때마다 원장 한 줄을 같이 남긴다(원장 없이 잔액만 바꾸지 않는다).
import type { PoolClient } from 'pg';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { parseItemKey } from '../../utils/itemKey';
import * as repo from './economyRepository';
import type { Bind, LockedChar, StackLocation } from './economyRepository';

export interface Delta {
  gold?: number;
  level?: number;
  xp?: number;
  stacks?: { item_key: string; location: string; count: number }[];
  worn?: { slot: number; item_key: string | null }[];
}

export class EconCtx {
  gold: number;
  level: number;
  xp: number;
  private readonly startLevel: number;
  private readonly startXp: number;
  private goldChanged = false;
  private readonly stacks = new Map<string, { item_key: string; location: string; count: number }>();
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

  bindOf(itemKey: string): Bind {
    const base = parseItemKey(itemKey)?.base ?? itemKey;
    return getGameData().economy.items.get(base)?.bind ?? 'none';
  }

  private noteStack(location: string, key: string, count: number): void {
    this.stacks.set(`${location}|${key}`, { item_key: key, location, count });
  }

  stackCount(location: StackLocation, key: string): Promise<number> {
    return repo.stackCount(this.client, this.char.id, location, key);
  }

  async addItem(location: StackLocation, key: string, n: number, reason: string, ref: string): Promise<void> {
    if (n <= 0) throw new Error('addItem 수량은 양수여야 합니다');
    const count = await repo.upsertStack(this.client, this.char.id, location, key, n, this.bindOf(key));
    await repo.insertItemLedger(this.client, this.char.id, key, n, count, location, reason, ref, this.requestId);
    this.noteStack(location, key, count);
  }

  /** 모자라면 false(아무것도 바꾸지 않는다) */
  async removeItem(location: StackLocation, key: string, n: number, reason: string, ref: string): Promise<boolean> {
    if (n <= 0) throw new Error('removeItem 수량은 양수여야 합니다');
    const count = await repo.decStack(this.client, this.char.id, location, key, n);
    if (count === null) return false;
    await repo.insertItemLedger(this.client, this.char.id, key, -n, count, location, reason, ref, this.requestId);
    this.noteStack(location, key, count);
    return true;
  }

  wornKey(slot: number): Promise<string | null> {
    return repo.getWornKey(this.client, this.char.id, slot);
  }

  /** 빈 슬롯에 착용 행을 만든다 */
  async wornPut(slot: number, key: string, reason: string, ref: string): Promise<void> {
    await repo.insertWorn(this.client, this.char.id, slot, key, this.bindOf(key));
    await repo.insertItemLedger(this.client, this.char.id, key, 1, 1, 'worn', reason, ref, this.requestId);
    this.worn.set(slot, key);
  }

  /** 착용 행을 지운다(키는 호출 쪽이 이미 읽었다) */
  async wornRemove(slot: number, key: string, reason: string, ref: string): Promise<void> {
    await repo.deleteWorn(this.client, this.char.id, slot);
    await repo.insertItemLedger(this.client, this.char.id, key, -1, 0, 'worn', reason, ref, this.requestId);
    this.worn.set(slot, null);
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
    const next = this.gold + delta;
    if (next < 0) {
      throw new AppError(422, '골드가 모자랍니다.', 'NOT_ENOUGH_GOLD', { need: -delta, have: this.gold });
    }
    await repo.updateGold(this.client, this.char.id, next);
    await repo.insertGoldLedger(this.client, this.char.id, delta, next, reason, ref, this.requestId);
    this.gold = next;
    this.goldChanged = true;
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
