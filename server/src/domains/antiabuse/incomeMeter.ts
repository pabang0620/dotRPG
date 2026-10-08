// 경제 속도 감시의 시간별 집계(12.3). EconCtx가 원장을 쓸 때 요청 안에서 누계를 만들고, runEconomy가 같은 트랜잭션에서 한 문장으로 올린다.
// income_hourly는 원장에서 파생한 값이다(정본은 원장). 어긋나면 income_reconcile 작업이 고친다.
import type { PoolClient } from 'pg';
import { afterCommit, type Queryable } from '../../db/pool';
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { parseItemKey } from '../../utils/itemKey';
import { sellPriceOf } from '../shop/sellPrice';

/** 집계에 들어가는 원장 사유(12.3 표). 그 밖의 사유(quest_reward, shop_sell, mail_claim, gacha, admin_* 등)는 제외 */
export const XP_REASONS: ReadonlySet<string> = new Set(['kill', 'dungeon_clear', 'dungeon_sweep']);
export const GOLD_REASONS: ReadonlySet<string> = new Set(['drop_claim', 'dungeon_card', 'raid_gold']);
export const ITEM_REASONS: ReadonlySet<string> = new Set(['drop_claim', 'gather', 'chest', 'dungeon_card', 'raid_core', 'raid_key']);

export interface IncomeCounts {
  xp: number;
  goldAcq: number;
  itemValue: number;
  ore: number;
  essence: number;
  core: number;
  epicPlus: number;
  uniquePlus: number;
}

export const emptyCounts = (): IncomeCounts => ({ xp: 0, goldAcq: 0, itemValue: 0, ore: 0, essence: 0, core: 0, epicPlus: 0, uniquePlus: 0 });

/** 아이템 키 하나(n개)가 집계에 더하는 값. 장비 등급은 shop.json의 rarity */
export function countItem(c: IncomeCounts, itemKey: string, n: number): void {
  const eco = getGameData().economy;
  const base = parseItemKey(itemKey)?.base ?? itemKey;
  c.itemValue += sellPriceOf(itemKey) * n;
  if (base === 'mat_ore') c.ore += n;
  else if (base === 'mat_essence') c.essence += n;
  else if (base === eco.enhance.promote?.coreItem) c.core += n;
  const rarity = eco.shop.equipment.get(base)?.rarity;
  if (rarity === 'Epic' || rarity === 'Unique' || rarity === 'Legendary') c.epicPlus += n;
  if (rarity === 'Unique' || rarity === 'Legendary') c.uniquePlus += n;
}

/** 한 요청의 누계. EconCtx가 changeGold/grantXp/addItem에서 부른다 */
export class IncomeNote {
  readonly counts: IncomeCounts = emptyCounts();

  gold(delta: number, reason: string): void {
    if (delta > 0 && GOLD_REASONS.has(reason)) this.counts.goldAcq += delta;
  }

  xp(granted: number, reason: string): void {
    if (granted > 0 && XP_REASONS.has(reason)) this.counts.xp += granted;
  }

  item(itemKey: string, n: number, reason: string): void {
    if (n > 0 && ITEM_REASONS.has(reason)) countItem(this.counts, itemKey, n);
  }

  isEmpty(): boolean {
    return Object.values(this.counts).every((v) => v === 0);
  }
}

export const HOUR_MS = 3_600_000;
/** UTC 시간 버킷의 시작(게임 일 경계와 무관한 롤링 창용) */
export const hourStart = (d: Date): Date => new Date(Math.floor(d.getTime() / HOUR_MS) * HOUR_MS);

// ---------- 더티 집합(프로세스 메모리, 12.8) ----------

const dirty = new Set<number>();
export const markDirty = (characterId: number): void => {
  if (getConfig().aa.hold.mode !== 'off') dirty.add(characterId);
};
export function takeDirty(): number[] {
  const ids = [...dirty];
  dirty.clear();
  return ids;
}

let flushMsTotal = 0;
let flushCount = 0;
export const flushStats = (): { count: number; avg_ms: number } => ({ count: flushCount, avg_ms: flushCount === 0 ? 0 : Math.round((flushMsTotal / flushCount) * 100) / 100 });

/** runEconomy가 핸들러 성공 직후 부른다: 한 문장 upsert. 비어 있으면 아무것도 쓰지 않는다. 커밋 뒤 더티 표시 */
export async function flush(client: PoolClient, characterId: number, level: number, now: Date, note: IncomeNote): Promise<void> {
  if (note.isEmpty()) return;
  const t0 = performance.now();
  const c = note.counts;
  await client.query(
    `INSERT INTO income_hourly (character_id, hour_start, level_max, xp, gold_acq, item_value, ore, essence, core, epic_plus, unique_plus, updated_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
     ON CONFLICT (character_id, hour_start) DO UPDATE SET
       level_max = GREATEST(income_hourly.level_max, EXCLUDED.level_max),
       xp = income_hourly.xp + EXCLUDED.xp,
       gold_acq = income_hourly.gold_acq + EXCLUDED.gold_acq,
       item_value = income_hourly.item_value + EXCLUDED.item_value,
       ore = income_hourly.ore + EXCLUDED.ore,
       essence = income_hourly.essence + EXCLUDED.essence,
       core = income_hourly.core + EXCLUDED.core,
       epic_plus = income_hourly.epic_plus + EXCLUDED.epic_plus,
       unique_plus = income_hourly.unique_plus + EXCLUDED.unique_plus,
       updated_at = EXCLUDED.updated_at`,
    [characterId, hourStart(now), Math.max(1, level), c.xp, c.goldAcq, c.itemValue, c.ore, c.essence, c.core, c.epicPlus, c.uniquePlus, now],
  );
  flushMsTotal += performance.now() - t0;
  flushCount++;
  afterCommit(client, () => markDirty(characterId), `income-dirty:${characterId}`);
}

/** 경매 체결의 판매자(수입, 가중 수입)·구매자(지출) 한 줄. 캐릭터 행을 잠그지 않는 정산 틱에서도 쓴다(한 문장 upsert) */
export async function noteAuction(
  db: Queryable,
  characterId: number,
  now: Date,
  amounts: { inAmount?: number; inWeighted?: number; out?: number },
): Promise<void> {
  const inA = amounts.inAmount ?? 0;
  const inW = amounts.inWeighted ?? 0;
  const out = amounts.out ?? 0;
  if (inA === 0 && inW === 0 && out === 0) return;
  await db.query(
    `INSERT INTO income_hourly (character_id, hour_start, level_max, auction_in, auction_in_w, auction_out, updated_at)
     SELECT c.id, $2, c.level, $3, $4, $5, $6 FROM characters c WHERE c.id = $1
     ON CONFLICT (character_id, hour_start) DO UPDATE SET
       auction_in = income_hourly.auction_in + EXCLUDED.auction_in,
       auction_in_w = income_hourly.auction_in_w + EXCLUDED.auction_in_w,
       auction_out = income_hourly.auction_out + EXCLUDED.auction_out,
       level_max = GREATEST(income_hourly.level_max, EXCLUDED.level_max),
       updated_at = EXCLUDED.updated_at`,
    [characterId, hourStart(now), inA, inW, out, now],
  );
}
