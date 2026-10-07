// income_hourly는 원장에서 파생한 값이다. 일 1회 최근 2일치를 원장과 대조하고 어긋나면 로그 + 재계산한다(정본은 원장, 12.8).
import { getPool } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { countItem, emptyCounts, GOLD_REASONS, HOUR_MS, hourStart, ITEM_REASONS, XP_REASONS, type IncomeCounts } from './incomeMeter';

interface Row extends IncomeCounts {
  auctionIn: number;
  auctionInW: number;
  auctionOut: number;
}

const keyOf = (characterId: number, hour: Date): string => `${characterId}|${hour.getTime()}`;
const blank = (): Row => ({ ...emptyCounts(), auctionIn: 0, auctionInW: 0, auctionOut: 0 });

const COLS: (keyof Row)[] = ['xp', 'goldAcq', 'itemValue', 'ore', 'essence', 'core', 'epicPlus', 'uniquePlus', 'auctionIn', 'auctionInW', 'auctionOut'];

export interface ReconcileResult {
  checked: number;
  mismatched: number;
  fixed: number;
  samples: Record<string, unknown>[];
}

/** 원장에서 (캐릭터, 시간)별 집계를 다시 계산한다 */
async function fromLedgers(since: Date): Promise<Map<string, { characterId: number; hour: Date; row: Row }>> {
  const db = getPool();
  const out = new Map<string, { characterId: number; hour: Date; row: Row }>();
  const get = (characterId: number, hour: Date): Row => {
    const k = keyOf(characterId, hour);
    let e = out.get(k);
    if (!e) {
      e = { characterId, hour, row: blank() };
      out.set(k, e);
    }
    return e.row;
  };
  const hourSql = "to_timestamp(floor(extract(epoch FROM created_at) / 3600) * 3600)";
  const xp = await db.query<{ character_id: string; h: Date; s: string }>(
    `SELECT character_id, ${hourSql} AS h, sum(delta) AS s FROM xp_ledger WHERE created_at >= $1 AND reason = ANY($2::text[]) GROUP BY 1, 2`,
    [since, [...XP_REASONS]],
  );
  for (const r of xp.rows) get(Number(r.character_id), r.h).xp += Number(r.s);
  const gold = await db.query<{ character_id: string; h: Date; s: string }>(
    `SELECT character_id, ${hourSql} AS h, sum(delta) AS s FROM gold_ledger WHERE created_at >= $1 AND delta > 0 AND reason = ANY($2::text[]) GROUP BY 1, 2`,
    [since, [...GOLD_REASONS]],
  );
  for (const r of gold.rows) get(Number(r.character_id), r.h).goldAcq += Number(r.s);
  const items = await db.query<{ character_id: string; h: Date; item_key: string; s: string }>(
    `SELECT character_id, ${hourSql} AS h, item_key, sum(delta) AS s FROM item_ledger
      WHERE created_at >= $1 AND delta > 0 AND reason = ANY($2::text[]) GROUP BY 1, 2, 3`,
    [since, [...ITEM_REASONS]],
  );
  for (const r of items.rows) countItem(get(Number(r.character_id), r.h), r.item_key, Number(r.s));
  const trades = await db.query<{ seller: string; buyer: string; h: Date; payout: string; price: string; w: number }>(
    `SELECT t.seller_character_id AS seller, t.buyer_character_id AS buyer,
            to_timestamp(floor(extract(epoch FROM t.traded_at) / 3600) * 3600) AS h, t.seller_payout AS payout, t.price,
            COALESCE((SELECT max(f.weight_pct) FROM auction_trade_flags f WHERE f.trade_id = t.id), 100) AS w
       FROM auction_trades t WHERE t.traded_at >= $1`,
    [since],
  );
  for (const t of trades.rows) {
    const payout = Number(t.payout);
    const seller = get(Number(t.seller), t.h);
    seller.auctionIn += payout;
    seller.auctionInW += Math.floor((payout * t.w) / 100);
    get(Number(t.buyer), t.h).auctionOut += Number(t.price);
  }
  return out;
}

/** 최근 hours시간(기본 48)을 원장과 대조한다. fix=true면 어긋난 줄을 원장 값으로 고친다 */
export async function reconcileIncome(opts: { hours?: number; fix?: boolean; now?: Date } = {}): Promise<ReconcileResult> {
  const db = getPool();
  const now = opts.now ?? getNow();
  const since = new Date(hourStart(new Date(now.getTime() - (opts.hours ?? 48) * HOUR_MS)).getTime());
  const expected = await fromLedgers(since);
  const stored = await db.query<Record<string, string | number | Date>>(
    `SELECT character_id, hour_start, xp, gold_acq, item_value, ore, essence, core, epic_plus, unique_plus, auction_in, auction_in_w, auction_out
       FROM income_hourly WHERE hour_start >= $1`,
    [since],
  );
  const have = new Map<string, Row>();
  for (const r of stored.rows) {
    have.set(keyOf(Number(r.character_id), r.hour_start as Date), {
      xp: Number(r.xp), goldAcq: Number(r.gold_acq), itemValue: Number(r.item_value), ore: Number(r.ore), essence: Number(r.essence), core: Number(r.core),
      epicPlus: Number(r.epic_plus), uniquePlus: Number(r.unique_plus), auctionIn: Number(r.auction_in), auctionInW: Number(r.auction_in_w), auctionOut: Number(r.auction_out),
    });
  }
  const result: ReconcileResult = { checked: 0, mismatched: 0, fixed: 0, samples: [] };
  const keys = new Set([...expected.keys(), ...have.keys()]);
  for (const k of keys) {
    result.checked++;
    const want = expected.get(k)?.row ?? blank();
    const got = have.get(k) ?? blank();
    const diff = COLS.filter((c) => want[c] !== got[c]);
    if (diff.length === 0) continue;
    result.mismatched++;
    const [cid, ms] = k.split('|') as [string, string];
    if (result.samples.length < 20) result.samples.push({ character_id: Number(cid), hour: new Date(Number(ms)).toISOString(), columns: diff, ledger: Object.fromEntries(diff.map((c) => [c, want[c]])), stored: Object.fromEntries(diff.map((c) => [c, got[c]])) });
    logger.warn({ character_id: Number(cid), hour: new Date(Number(ms)).toISOString(), columns: diff }, 'anti_abuse.income_mismatch');
    if (opts.fix === false) continue;
    await db.query(
      `INSERT INTO income_hourly (character_id, hour_start, level_max, xp, gold_acq, item_value, ore, essence, core, epic_plus, unique_plus, auction_in, auction_in_w, auction_out, updated_at)
       SELECT c.id, $2, c.level, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14 FROM characters c WHERE c.id = $1
       ON CONFLICT (character_id, hour_start) DO UPDATE SET
         xp = EXCLUDED.xp, gold_acq = EXCLUDED.gold_acq, item_value = EXCLUDED.item_value, ore = EXCLUDED.ore, essence = EXCLUDED.essence, core = EXCLUDED.core,
         epic_plus = EXCLUDED.epic_plus, unique_plus = EXCLUDED.unique_plus, auction_in = EXCLUDED.auction_in, auction_in_w = EXCLUDED.auction_in_w,
         auction_out = EXCLUDED.auction_out, updated_at = EXCLUDED.updated_at`,
      [Number(cid), new Date(Number(ms)), want.xp, want.goldAcq, want.itemValue, want.ore, want.essence, want.core, want.epicPlus, want.uniquePlus, want.auctionIn, want.auctionInW, want.auctionOut, now],
    );
    result.fixed++;
  }
  return result;
}
