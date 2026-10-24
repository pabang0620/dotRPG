// 일일·주간 완료 상자와 강화 재료 상자(Docs/PLAN_DAILY_REWARDS_2026-10-24.md).
// 그 기간 칸을 모두 청구하면 상자 하나. 일일 상자는 연속 일수(전날 상자를 열었으면 +1)로 단계가 오르고 주기(7일)마다 금 상자.
// 내용·보너스 굴림·스킨 게이지는 서버가 정하고, 지급은 runEconomy 안(캐릭터 잠금, request_id 재생)에서 원장과 함께 한다.
import type { Queryable } from '../../db/pool';
import { levelCap } from '../../gamedata/chapterGate';
import { getDailyQuests, type Board, type ChestStep, type Chests, type ItemCount, type Period } from '../../gamedata/dailyQuests';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import { assertNoHold } from '../antiabuse/holds';
import type { EconCtx } from '../economy/economyContext';
import * as sealedRepo from '../sealedbox/sealedBoxRepository';
import { gaugeCap } from '../starshop/starshopDefs';
import * as wallets from '../starshop/starWallet';
import * as tickets from '../sweep/ticketWallet';
import { gameDayOf, openDays, shiftDay } from './dailyDays';
import * as repo from './dailyChestRepository';

export type ChestState = 'locked' | 'ready' | 'claimed';
export interface ChestView {
  period: Period;
  day: string;
  state: ChestState;
  /** 청구한 칸 / 필요한 칸 */
  done: number;
  need: number;
  /** 일일: 이 상자를 열면 되는(연) 연속 일수. 주간: 0 */
  streak: number;
  /** 일일: 단계 1..cycle. 주간: 0 */
  step: number;
  name: string;
  grade: string;
  /** 고정 내용(만렙이면 만렙 추가분 포함). 보너스 굴림 결과는 열 때 정한다 */
  rewards: ItemCount[];
  bonus_rolls: number;
  skin_gauge: number;
  max_level: boolean;
}
export interface ChestsView {
  /** 열 수 있거나 진행 중인 일일 상자(어제 것이 남았으면 먼저), 오늘 상자는 항상 마지막 */
  day: ChestView[];
  week: ChestView;
  streak: {
    /** 지금 이어지고 있는 연속 일수(0 = 끊김) */
    current: number;
    cycle: number;
    /** 어제 상자를 아직 안 열었지만 어제 의뢰를 오늘 끝내면 이어지는 상태 */
    grace: boolean;
  };
}
export interface OpenedReward extends ItemCount {
  source: 'fixed' | 'max_level' | 'bonus';
}

const atMaxLevel = (level: number): boolean => level >= levelCap(getGameData().economy.progression.maxLevel);

function stepOf(c: Chests, period: Period, streak: number): ChestStep {
  if (period === 'week') return c.week;
  return c.day[(streak - 1) % c.day.length] as ChestStep;
}

/** 고정 내용 + 만렙 추가분(같은 키는 합친다) */
export function fixedContents(step: ChestStep, maxLevel: boolean): ItemCount[] {
  const sum = new Map<string, number>();
  for (const r of [...step.rewards, ...(maxLevel ? step.maxLevel : [])]) sum.set(r.item_key, (sum.get(r.item_key) ?? 0) + r.count);
  return [...sum].map(([item_key, count]) => ({ item_key, count }));
}

function chestView(c: Chests, board: Board, day: string, done: number, claimed: boolean, streak: number, level: number): ChestView {
  const step = stepOf(c, board.period, streak);
  const max = atMaxLevel(level);
  return {
    period: board.period,
    day,
    state: claimed ? 'claimed' : done >= board.per ? 'ready' : 'locked',
    done: Math.min(done, board.per),
    need: board.per,
    streak: board.period === 'day' ? streak : 0,
    step: board.period === 'day' ? step.streak : 0,
    name: step.name,
    grade: step.grade,
    rewards: fixedContents(step, max),
    bonus_rolls: step.bonusRolls,
    skin_gauge: step.skinGauge ?? 0,
    max_level: max,
  };
}

/** GET /dailies 의 chests: 일일(어제 남은 것 + 오늘), 주간, 연속 표시 */
export async function chestsView(db: Queryable, characterId: number, level: number, now: Date): Promise<ChestsView> {
  const data = getDailyQuests();
  const c = data.chests;
  const today = gameDayOf(now);
  const days = openDays(data.day, now);
  const from = shiftDay(days[0] as string, -1);
  const rows = new Map((await repo.chestsSince(db, characterId, 'day', from)).map((r) => [r.day, r]));
  const done = await repo.claimedSlotsSince(db, characterId, 'day', days[0] as string);
  const dayViews: ChestView[] = [];
  for (const d of days) {
    const row = rows.get(d);
    const streak = row ? row.streak : (rows.get(shiftDay(d, -1))?.streak ?? 0) + 1;
    const v = chestView(c, data.day, d, done.get(d) ?? 0, !!row, streak, level);
    // 지난 날 상자는 열 수 있을 때나 연속이 걸려 있을 때만 보여 준다
    if (d !== today && (row || (v.state !== 'ready' && !rows.has(shiftDay(d, -1))))) continue;
    dayViews.push(v);
  }
  const week = openDays(data.week, now)[0] as string;
  const weekRow = (await repo.chestsSince(db, characterId, 'week', week)).find((r) => r.day === week);
  const weekView = chestView(c, data.week, week, await repo.claimedSlots(db, characterId, 'week', week), !!weekRow, 0, level);

  const yesterday = shiftDay(today, -1);
  let current = 0;
  let grace = false;
  const latest = rows.get(today) ?? rows.get(yesterday);
  if (latest) current = latest.streak;
  else if (data.day.carry >= 1 && rows.get(shiftDay(today, -2))) {
    current = (rows.get(shiftDay(today, -2)) as repo.ChestRow).streak;
    grace = true;
  }
  return { day: dayViews, week: weekView, streak: { current, cycle: c.day.length, grace } };
}

/** 가중치 표에서 한 줄(0 이상 합계 미만 정수) */
function pickWeighted<T extends { weight: number }>(rows: T[]): { row: T; rate: number } {
  const total = rows.reduce((a, r) => a + r.weight, 0);
  let roll = getRng().int(0, total);
  for (const r of rows) {
    if (roll < r.weight) return { row: r, rate: (r.weight * 100) / total };
    roll -= r.weight;
  }
  const last = rows[rows.length - 1] as T;
  return { row: last, rate: (last.weight * 100) / total };
}

const tierOf = (rate: number): 'common' | 'rare' => (rate < 5 ? 'rare' : 'common');

/** 보호권 조각이 per개 이상이면 그만큼 장비 보호권으로 바꾼다 */
async function mergeFragments(ctx: EconCtx, c: Chests, ref: string): Promise<{ fragments: number; tickets: number }> {
  const have = await ctx.stackCount('bag', c.fragment.item);
  const n = Math.floor(have / c.fragment.per);
  if (n <= 0) return { fragments: 0, tickets: 0 };
  const used = await ctx.removeItem('bag', c.fragment.item, n * c.fragment.per, 'fragment_merge', ref);
  if (used === null) return { fragments: 0, tickets: 0 };
  await ctx.addItem('bag', c.fragment.into, n, 'fragment_merge', ref);
  return { fragments: n * c.fragment.per, tickets: n };
}

export interface ChestOpened {
  period: Period;
  day: string;
  streak: number;
  step: number;
  name: string;
  grade: string;
  rewards: OpenedReward[];
  merged: { fragments: number; tickets: number };
  skin_gauge: number;
}

/** 상자를 연다(runEconomy 핸들러 안). 열 수 있는 기간·칸 완료·한 번만을 여기서 확인한다 */
export async function openChestIn(ctx: EconCtx, period: Period, day: string): Promise<ChestOpened> {
  const data = getDailyQuests();
  const c = data.chests;
  const board = data[period];
  if (!openDays(board, ctx.now).includes(day)) throw new AppError(422, '기간이 지난 상자입니다.', 'DAILY_EXPIRED');
  const existing = await repo.chestsSince(ctx.client, ctx.char.id, period, shiftDay(day, -1));
  if (existing.some((r) => r.day === day)) throw new AppError(409, '이미 연 상자입니다.', 'DAILY_CHEST_CLAIMED');
  const have = await repo.claimedSlots(ctx.client, ctx.char.id, period, day);
  if (have < board.per) throw new AppError(422, '의뢰를 모두 끝내야 상자를 열 수 있습니다.', 'DAILY_CHEST_LOCKED', { need: board.per, have });

  const streak = period === 'day' ? (existing.find((r) => r.day === shiftDay(day, -1))?.streak ?? 0) + 1 : 0;
  const step = stepOf(c, period, streak);
  const max = atMaxLevel(ctx.level);
  const ref = `${period}:${day}`;
  const rewards: OpenedReward[] = [
    ...step.rewards.map((r) => ({ ...r, source: 'fixed' as const })),
    ...(max ? step.maxLevel.map((r) => ({ ...r, source: 'max_level' as const })) : []),
  ];
  const pulls: sealedRepo.PullLogRow[] = [];
  for (let i = 0; i < step.bonusRolls; i++) {
    const { row, rate } = pickWeighted(c.bonus);
    for (const r of row.rewards) rewards.push({ ...r, source: 'bonus' });
    const first = row.rewards[0] as ItemCount;
    pulls.push({ seq: i, source: 'daily_chest', rowId: row.id, itemKey: first.item_key, count: first.count, tier: tierOf(rate), boosted: false, rate, gaugeBefore: null, gaugeAfter: null });
  }
  const sum = new Map<string, number>();
  for (const r of rewards) sum.set(r.item_key, (sum.get(r.item_key) ?? 0) + r.count);
  for (const [key, n] of sum) await ctx.addItem('bag', key, n, 'daily_chest', ref);
  const merged = await mergeFragments(ctx, c, ref);

  // 스킨 선택 게이지: 계정당 그 주 한 번(부캐릭터로 여러 번 채우지 못한다). 락 순서: 캐릭터 -> 계정 -> 별조각 지갑
  let gauge = 0;
  if ((step.skinGauge ?? 0) > 0) {
    await tickets.lockAccount(ctx.client, ctx.char.accountId);
    if (!(await repo.gaugeGrantedThisWeek(ctx.client, ctx.char.accountId, day))) {
      const w = await wallets.lockWallet(ctx.client, ctx.char.accountId);
      const next = Math.min(gaugeCap('skin'), w.skinPity + (step.skinGauge ?? 0));
      gauge = next - w.skinPity;
      if (gauge > 0) await wallets.setPity(ctx.client, ctx.char.accountId, next, 'skin');
    }
  }

  await repo.insertChest(ctx.client, {
    characterId: ctx.char.id,
    accountId: ctx.char.accountId,
    period,
    day,
    streak,
    step: period === 'day' ? step.streak : 0,
    level: ctx.level,
    rewards,
    skinGauge: gauge,
    ratesVersion: c.ratesVersion,
    requestId: ctx.requestId,
    now: ctx.now,
  });
  // 어제 상자를 오늘 상자보다 늦게 연 경우: 오늘(그 뒤) 상자의 연속 일수를 이어 붙인다
  if (period === 'day') {
    let s = streak;
    for (let d = shiftDay(day, 1); d <= gameDayOf(ctx.now); d = shiftDay(d, 1)) {
      s += 1;
      if (!(await repo.setStreak(ctx.client, ctx.char.id, d, s))) break;
    }
  }
  if (pulls.length > 0) await sealedRepo.insertPulls(ctx.client, { accountId: ctx.char.accountId, characterId: ctx.char.id, requestId: ctx.requestId, version: c.ratesVersion, rows: pulls });
  return { period, day, streak, step: period === 'day' ? step.streak : 0, name: step.name, grade: step.grade, rewards, merged, skin_gauge: gauge };
}

/** 강화 재료 상자인지 */
export const isMatBox = (itemKey: string): boolean => getDailyQuests().chests.matBox.item === itemKey;

/** POST /items/open 의 강화 재료 상자 갈래(runEconomy 핸들러 안): 구간을 고르고 구간 안에서 같은 확률로 수량을 정한다 */
export async function openMatBoxIn(ctx: EconCtx, itemKey: string) {
  const c = getDailyQuests().chests;
  await assertNoHold(ctx.client, ctx.char.accountId, ctx.char.id);
  const have = await ctx.stackCount('bag', itemKey);
  if (have < 1) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have });
  const consumed = await ctx.removeItem('bag', itemKey, 1, 'box_open', itemKey);
  if (consumed === null) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have });
  const { row: band, rate } = pickWeighted(c.matBox.bands);
  const count = getRng().int(band.min, band.max + 1);
  const reward: ItemCount = { item_key: c.matBox.reward, count };
  await ctx.addItem('bag', reward.item_key, count, 'box_open', itemKey);
  const rowId = `${c.matBox.reward}_${band.min}_${band.max}`;
  const tier = tierOf(rate);
  await sealedRepo.insertPulls(ctx.client, {
    accountId: ctx.char.accountId,
    characterId: ctx.char.id,
    requestId: ctx.requestId,
    version: c.ratesVersion,
    rows: [{ seq: 0, source: 'mat_box', rowId, itemKey: reward.item_key, count, tier, boosted: false, rate, gaugeBefore: null, gaugeAfter: null }],
  });
  const result = { item_key: reward.item_key, count, boosted: false, tier, rewards: [reward], row_id: rowId, grade: tier === 'rare' ? 'rare' : 'normal' };
  return { status: 200, data: { item_key: itemKey, results: [result], delta: ctx.delta() } };
}

/** 확률표 스냅샷(star_rates_snapshots, 버전 = chests.ratesVersion). 기동 때 ensureRatesSnapshot 이 같은 버전 다른 내용을 막는다 */
export function dailyRatesContent(c: Chests = getDailyQuests().chests): Record<string, unknown> {
  return { version: c.ratesVersion, kind: 'daily_chest', fragment: c.fragment, day: c.day, week: c.week, bonus: c.bonus, mat_box: c.matBox };
}
