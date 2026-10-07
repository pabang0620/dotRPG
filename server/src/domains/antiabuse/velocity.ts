// 경제 속도 평가(12.2~12.4). 상한 식과 임계 판정은 순수 함수이고(단위 테스트로 고정), 버킷 읽기는 한 함수다.
// 상한 표는 server/data/income_caps.json(Tools/balance/theory_income.py가 만든다)에서 읽는다. 문서의 표를 코드에 복사하지 않는다.
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import type { IncomeBand, IncomeCaps } from '../../gamedata/antiAbuseData';
import { HOUR_MS, hourStart } from './incomeMeter';

export type WindowKind = '1h' | '24h' | '7d';
export const WINDOWS: readonly WindowKind[] = ['1h', '24h', '7d'];
export const WINDOW_HOURS: Record<WindowKind, number> = { '1h': 1, '24h': 24, '7d': 168 };
/** 롤링 창이 06:00 경계 하루 반에 걸칠 수 있어 일 단위 덩어리에 곱하는 계수(12.2의 3) */
export const DAYS_TOUCHED: Record<WindowKind, number> = { '1h': 2, '24h': 2, '7d': 8 };
const MID_RAID_DAYS: Record<WindowKind, number> = { '1h': 2, '24h': 2, '7d': 3 };
const FINAL_RAID_WEEKS: Record<WindowKind, number> = { '1h': 2, '24h': 2, '7d': 2 };

export const METRICS = ['xp', 'goldEq', 'ore', 'essence', 'epicPlus', 'uniquePlus'] as const;
export type Metric = (typeof METRICS)[number];
export type MetricValues = Record<Metric, number>;

export interface Bucket {
  hourStart: Date;
  /** 그 시간 버킷에서 본 최고 레벨(income_hourly). 없으면 null */
  levelMax: number | null;
  activeSeconds: number;
  xp: number;
  goldAcq: number;
  itemValue: number;
  ore: number;
  essence: number;
  epicPlus: number;
  uniquePlus: number;
  auctionIn: number;
  auctionInW: number;
  auctionOut: number;
}

/** 레벨이 속한 대역(표 밖이면 가장 가까운 대역) */
export function bandOf(caps: IncomeCaps, level: number): IncomeBand {
  const sorted = [...caps.bands].sort((a, b) => a.minLevel - b.minLevel);
  const hit = sorted.find((b) => level >= b.minLevel && level <= b.maxLevel);
  if (hit) return hit;
  return level < (sorted[0] as IncomeBand).minLevel ? (sorted[0] as IncomeBand) : (sorted[sorted.length - 1] as IncomeBand);
}

/**
 * cap(metric, window) = Σ_버킷 rate(대역(버킷 레벨)) x 활동시간/3600 + 일 단위 덩어리(최고 레벨 대역) x days_touched + 레이드 덩어리
 * 프레즌스가 없는 시간은 활동 시간 0이라 덩어리 상한만 허용된다(HTTP 봇을 그대로 잡는다).
 */
export function capsFor(caps: IncomeCaps, window: WindowKind, buckets: Bucket[], maxLevel: number): MetricValues {
  const out: MetricValues = { xp: 0, goldEq: 0, ore: 0, essence: 0, epicPlus: 0, uniquePlus: 0 };
  let activeHours = 0;
  for (const b of buckets) {
    const band = bandOf(caps, b.levelMax ?? maxLevel);
    const h = b.activeSeconds / 3600;
    activeHours += h;
    out.xp += band.perHour.xp * h;
    out.goldEq += band.perHour.goldEq * h;
    out.ore += band.perHour.ore * h;
    out.essence += band.perHour.essence * h;
    out.epicPlus += band.perHour.epicPlus * h;
  }
  const top = bandOf(caps, maxLevel);
  const days = DAYS_TOUCHED[window];
  out.xp += top.perDay.dungeonXp * days + top.perDay.raidMidXp * MID_RAID_DAYS[window] + top.perWeek.raidFinalXp * FINAL_RAID_WEEKS[window];
  out.goldEq += top.perDay.dungeonGoldEq * days + top.perDay.raidMidGoldEq * MID_RAID_DAYS[window] + top.perWeek.raidFinalGoldEq * FINAL_RAID_WEEKS[window];
  out.uniquePlus = caps.uniquePlus.perDay * days + caps.uniquePlus.perHour * activeHours;
  return out;
}

export function valuesOf(buckets: Bucket[]): MetricValues {
  const v: MetricValues = { xp: 0, goldEq: 0, ore: 0, essence: 0, epicPlus: 0, uniquePlus: 0 };
  for (const b of buckets) {
    v.xp += b.xp;
    v.goldEq += b.goldAcq + b.itemValue;
    v.ore += b.ore;
    v.essence += b.essence;
    v.epicPlus += b.epicPlus;
    v.uniquePlus += b.uniquePlus;
  }
  return v;
}

/** 절대 하한: 이보다 작은 값은 임계를 넘어도 무시한다(새 계정의 작은 값, 시간 0 구간의 오탐 방지). 에픽 이상은 유니크 하한을 쓴다 */
export function floorOf(metric: Metric): number {
  const h = getConfig().aa.hold;
  switch (metric) {
    case 'xp':
      return h.minXp;
    case 'goldEq':
      return h.minGoldEq;
    case 'ore':
      return h.minOre;
    case 'essence':
      return h.minEssence;
    default:
      return h.minUnique;
  }
}

export interface WindowEval {
  window: WindowKind;
  values: MetricValues;
  caps: MetricValues;
  activeSeconds: number;
  /** 임계(value > 배수 x cap)를 넘고 절대 하한도 넘는 지표 */
  over: Metric[];
}

export function evaluateBuckets(caps: IncomeCaps, window: WindowKind, buckets: Bucket[], maxLevel: number, mult: number): WindowEval {
  const values = valuesOf(buckets);
  const cap = capsFor(caps, window, buckets, maxLevel);
  const over = METRICS.filter((m) => values[m] > mult * cap[m] && values[m] > floorOf(m));
  return { window, values, caps: cap, activeSeconds: buckets.reduce((a, b) => a + b.activeSeconds, 0), over };
}

/** 경매 위반(12.4): 24시간 가중 순유입이 자기 골드환산 상한의 비율 + 하한을 넘는가 */
export function auctionViolation(buckets: Bucket[], capGoldEq24h: number, ratio: number, floor: number): { net: number; limit: number; over: boolean } {
  const net = buckets.reduce((a, b) => a + b.auctionInW - b.auctionOut, 0);
  const limit = ratio * capGoldEq24h + floor;
  return { net, limit, over: net > limit };
}

interface IncomeRow {
  hour_start: Date;
  level_max: number;
  xp: string;
  gold_acq: string;
  item_value: string;
  ore: number;
  essence: number;
  epic_plus: number;
  unique_plus: number;
  auction_in: string;
  auction_in_w: string;
  auction_out: string;
}
interface PlayRow {
  hour_start: Date;
  active_seconds: number;
}

/** 창 시작(시간 버킷 경계)부터의 버킷. 해제 기준선이 있으면 그 시각이 속한 시간까지는 제외한다(운영자가 승인한 수입) */
export async function loadBuckets(db: Queryable, characterId: number, now: Date, window: WindowKind, baseline: Date | null): Promise<Bucket[]> {
  let since = hourStart(new Date(now.getTime() - WINDOW_HOURS[window] * HOUR_MS));
  if (baseline) {
    const after = new Date(hourStart(baseline).getTime() + HOUR_MS);
    if (after.getTime() > since.getTime()) since = after;
  }
  const inc = await db.query<IncomeRow>(
    `SELECT hour_start, level_max, xp, gold_acq, item_value, ore, essence, epic_plus, unique_plus, auction_in, auction_in_w, auction_out
       FROM income_hourly WHERE character_id = $1 AND hour_start >= $2`,
    [characterId, since],
  );
  const play = await db.query<PlayRow>('SELECT hour_start, active_seconds FROM play_time_hourly WHERE character_id = $1 AND hour_start >= $2', [characterId, since]);
  const byHour = new Map<number, Bucket>();
  const get = (d: Date): Bucket => {
    let b = byHour.get(d.getTime());
    if (!b) {
      b = { hourStart: d, levelMax: null, activeSeconds: 0, xp: 0, goldAcq: 0, itemValue: 0, ore: 0, essence: 0, epicPlus: 0, uniquePlus: 0, auctionIn: 0, auctionInW: 0, auctionOut: 0 };
      byHour.set(d.getTime(), b);
    }
    return b;
  };
  for (const r of inc.rows) {
    const b = get(r.hour_start);
    b.levelMax = r.level_max;
    b.xp = Number(r.xp);
    b.goldAcq = Number(r.gold_acq);
    b.itemValue = Number(r.item_value);
    b.ore = r.ore;
    b.essence = r.essence;
    b.epicPlus = r.epic_plus;
    b.uniquePlus = r.unique_plus;
    b.auctionIn = Number(r.auction_in);
    b.auctionInW = Number(r.auction_in_w);
    b.auctionOut = Number(r.auction_out);
  }
  for (const r of play.rows) get(r.hour_start).activeSeconds = r.active_seconds;
  return [...byHour.values()].sort((a, b) => a.hourStart.getTime() - b.hourStart.getTime());
}

/** 서버 전체의 가장 오래된 시간 버킷(배포 직후 창을 건너뛰는 기준). 없으면 null */
export async function firstBucketAt(db: Queryable): Promise<Date | null> {
  const r = await db.query<{ t: Date | null }>('SELECT min(hour_start) AS t FROM income_hourly');
  return r.rows[0]?.t ?? null;
}
