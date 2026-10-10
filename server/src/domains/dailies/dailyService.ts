// 일일·주간 의뢰(Docs/server/phase14_daily_quests.md). 칸은 (캐릭터, 기간 시작 날짜) 시드로 매번 계산하고, 수락하면 행에 고정한다.
// 청구는 경제 요청 틀(runEconomy: 캐릭터 잠금, request_id 재생)에서 진행을 다시 세고 경험치·골드를 원장과 함께 준다.
import { createHash } from 'node:crypto';
import { getPool, type Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import {
  bandFor,
  fieldLevel,
  findTemplate,
  getDailyQuests,
  type Board,
  type DailyBand,
  type DailyTemplate,
  type Period,
} from '../../gamedata/dailyQuests';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import { findOwnedAlive } from '../characters/characterRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './dailyRepository';
import type { SlotBody } from './dailyValidation';

const KST_OFFSET_MS = 9 * 60 * 60 * 1000;
const DAY_MS = 24 * 60 * 60 * 1000;

export type SlotState = 'offered' | 'active' | 'ready' | 'claimed';
export interface SlotView {
  period: Period;
  day: string;
  slot: number;
  template_id: string;
  type: DailyTemplate['type'];
  title: string;
  text: string;
  label: string;
  map: string | null;
  monster: string | null;
  count: number;
  progress: number;
  state: SlotState;
  reward: { xp: number; gold: number };
}
export interface DailiesView {
  character_id: string;
  unlocked: boolean;
  level: number;
  game_day: string;
  next_reset_at: string;
  giver: string;
  slots: SlotView[];
  weekly: { week_start: string; next_reset_at: string; slots: SlotView[] };
}

const kstDate = (iso: string): string => new Date(Date.parse(iso) + KST_OFFSET_MS).toISOString().slice(0, 10);

/** 06:00 KST 경계의 게임 날짜(YYYY-MM-DD) */
export function gameDayOf(now: Date): string {
  return kstDate(resetBoundaries(now).dailyStartAt);
}

/** 목요일 06:00 KST 주 시작의 KST 날짜 */
export function weekStartOf(now: Date): string {
  return kstDate(resetBoundaries(now).weeklyStartAt);
}

const shiftDay = (day: string, n: number): string => new Date(Date.parse(`${day}T00:00:00Z`) + n * DAY_MS).toISOString().slice(0, 10);

/** 진행 가능한 기간 시작 날짜들(오래된 것 먼저, 지금 기간이 마지막) */
function openDays(board: Board, now: Date): string[] {
  if (board.period === 'week') return [weekStartOf(now)];
  const today = gameDayOf(now);
  const days: string[] = [];
  for (let i = board.carry; i >= 0; i--) days.push(shiftDay(today, -i));
  return days;
}

/** kill은 맵의 필드 몬스터 레벨 <= 캐릭터 레벨 + 2 일 때만, 나머지는 항상 후보 */
function eligible(t: DailyTemplate, level: number): boolean {
  if (t.type !== 'kill') return true;
  const lv = fieldLevel(t.map, t.monster);
  return lv !== null && lv <= level + 2;
}

/** 그 기간 칸들의 의뢰: 구간 후보를 (기간, 캐릭터, 날짜, 의뢰 id) 해시로 섞어 앞에서부터. 후보가 모자라면 반복 */
export function pickSlots(board: Board, characterId: number, day: string, level: number): DailyTemplate[] {
  const band = bandFor(board, level);
  let pool = band.templates.filter((t) => eligible(t, level));
  if (pool.length === 0) pool = band.templates;
  const key = (t: DailyTemplate) => createHash('sha256').update(`${board.period}:${characterId}:${day}:${t.id}`).digest('hex');
  const sorted = [...pool].sort((a, b) => (key(a) < key(b) ? -1 : 1));
  return Array.from({ length: board.per }, (_, i) => sorted[i % sorted.length] as DailyTemplate);
}

export function rewardFor(board: Board, band: DailyBand, level: number): { xp: number; gold: number } {
  const p = getGameData().economy.progression;
  if (level >= p.maxLevel) return { xp: 0, gold: board.maxLevelGold };
  return { xp: Math.round(board.xpShare * (p.xpToNext[level - 1] as number)), gold: band.gold };
}

function nonRaidDungeons(): string[] {
  return [...getGameData().economy.dungeons.byId.values()].filter((d) => !d.isRaid).map((d) => d.id);
}

async function progressOf(db: Queryable, characterId: number, t: DailyTemplate, since: Date): Promise<number> {
  let n: number;
  if (t.type === 'kill') n = await repo.killsSince(db, characterId, t.map, t.monster, since);
  else if (t.type === 'dungeon') n = await repo.clearsSince(db, characterId, nonRaidDungeons(), since);
  else if (t.type === 'raid') n = await repo.clearsSince(db, characterId, [t.target], since);
  else n = await repo.dailyClaimsSince(db, characterId, since);
  return Math.min(n, t.count);
}

const LOCKED = () => new AppError(403, '아직 일일 의뢰를 받을 수 없습니다.', 'DAILY_LOCKED');
const EXPIRED = () => new AppError(422, '기간이 지난 의뢰입니다.', 'DAILY_EXPIRED');
const LABEL: Record<string, string> = { dungeon: '요일 던전 클리어', raid: '레이드 클리어', daily: '일일 의뢰 완료' };

async function boardSlots(db: Queryable, board: Board, characterId: number, level: number, now: Date): Promise<SlotView[]> {
  const days = openDays(board, now);
  const current = days[days.length - 1] as string;
  const rows = new Map((await repo.rowsSince(db, characterId, board.period, days[0] as string)).map((r) => [`${r.day}:${r.slot}`, r]));
  const slots: SlotView[] = [];
  for (const day of days) {
    const picks = pickSlots(board, characterId, day, level);
    for (let slot = 0; slot < board.per; slot++) {
      const row = rows.get(`${day}:${slot}`);
      // 지난 기간 칸은 끝낸 것을 보여 주지 않는다(지금 기간 칸만 받은 표시)
      if (row?.claimed_at && day !== current) continue;
      const found = row ? findTemplate(board, row.template_id) : { band: bandFor(board, level), template: picks[slot] as DailyTemplate };
      if (!found) continue; // 데이터에서 빠진 의뢰
      const t = found.template;
      const progress = row?.claimed_at ? t.count : row ? await progressOf(db, characterId, t, row.accepted_at) : 0;
      const state: SlotState = row?.claimed_at ? 'claimed' : !row ? 'offered' : progress >= t.count ? 'ready' : 'active';
      slots.push({
        period: board.period,
        day,
        slot,
        template_id: t.id,
        type: t.type,
        title: t.title,
        text: t.text,
        label: t.type === 'kill' ? t.label : (LABEL[t.type] as string),
        map: t.type === 'kill' ? t.map : null,
        monster: t.type === 'kill' ? t.monster : null,
        count: t.count,
        progress,
        state,
        reward: rewardFor(board, found.band, level),
      });
    }
  }
  return slots;
}

export async function view(db: Queryable, characterId: number, characterUuid: string, level: number, now: Date): Promise<DailiesView> {
  const data = getDailyQuests();
  const b = resetBoundaries(now);
  const unlocked = await repo.hasClaimedQuest(db, characterId, data.unlockQuest);
  return {
    character_id: characterUuid,
    unlocked,
    level,
    game_day: gameDayOf(now),
    next_reset_at: b.nextDailyAt,
    giver: bandFor(data.day, level).giver ?? '',
    slots: unlocked ? await boardSlots(db, data.day, characterId, level, now) : [],
    weekly: {
      week_start: weekStartOf(now),
      next_reset_at: b.nextWeeklyAt,
      slots: unlocked ? await boardSlots(db, data.week, characterId, level, now) : [],
    },
  };
}

export async function list(accountId: number, characterUuid: string): Promise<DailiesView> {
  const db = getPool();
  const c = await findOwnedAlive(db, accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return view(db, c.id, c.uuid, c.level, getNow());
}

/** 칸 번호와 기간이 지금 진행할 수 있는 범위인지(지금 기간 또는 이월 안의 지난 날) */
function assertOpen(board: Board, day: string, slot: number, now: Date): void {
  if (slot >= board.per) throw new AppError(422, '없는 칸입니다.', 'DAILY_SLOT_INVALID');
  if (!openDays(board, now).includes(day)) throw EXPIRED();
}

export function accept(accountId: number, characterUuid: string, body: SlotBody): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/dailies/accept',
    requestId: body.request_id,
    payload: { period: body.period, day: body.day, slot: body.slot },
    handler: async (ctx) => {
      const data = getDailyQuests();
      const board = data[body.period];
      if (!(await repo.hasClaimedQuest(ctx.client, ctx.char.id, data.unlockQuest))) throw LOCKED();
      assertOpen(board, body.day, body.slot, ctx.now);
      const t = pickSlots(board, ctx.char.id, body.day, ctx.level)[body.slot] as DailyTemplate;
      await repo.insertRow(ctx.client, ctx.char.id, board.period, body.day, body.slot, t.id, ctx.now);
      return { status: 200, data: await view(ctx.client, ctx.char.id, ctx.char.uuid, ctx.level, ctx.now) };
    },
  });
}

export function claim(accountId: number, characterUuid: string, body: SlotBody): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/dailies/claim',
    requestId: body.request_id,
    payload: { period: body.period, day: body.day, slot: body.slot },
    handler: async (ctx) => {
      const data = getDailyQuests();
      const board = data[body.period];
      if (!(await repo.hasClaimedQuest(ctx.client, ctx.char.id, data.unlockQuest))) throw LOCKED();
      if (body.slot >= board.per) throw new AppError(422, '없는 칸입니다.', 'DAILY_SLOT_INVALID');
      const row = await repo.lockRow(ctx.client, ctx.char.id, board.period, body.day, body.slot);
      if (row?.claimed_at) throw new AppError(409, '이미 보상을 받은 의뢰입니다.', 'DAILY_ALREADY_CLAIMED');
      assertOpen(board, body.day, body.slot, ctx.now);
      if (!row) throw new AppError(422, '먼저 의뢰를 수락해야 합니다.', 'DAILY_NOT_ACCEPTED');
      const found = findTemplate(board, row.template_id);
      if (!found) throw new AppError(422, '더 이상 없는 의뢰입니다.', 'DAILY_UNKNOWN');
      const have = await progressOf(ctx.client, ctx.char.id, found.template, row.accepted_at);
      if (have < found.template.count) {
        throw new AppError(422, '아직 의뢰를 끝내지 못했습니다.', 'DAILY_NOT_DONE', { need: found.template.count, have });
      }

      const levelAtClaim = ctx.level;
      const reward = rewardFor(board, found.band, levelAtClaim);
      const { granted } = await ctx.grantXp(reward.xp, 'daily_quest', found.template.id);
      if (reward.gold > 0) await ctx.changeGold(reward.gold, 'daily_quest', found.template.id);
      await repo.markClaimed(ctx.client, ctx.char.id, board.period, body.day, body.slot, { xp: granted, gold: reward.gold, level: levelAtClaim }, ctx.requestId, ctx.now);

      return {
        status: 200,
        data: {
          ...(await view(ctx.client, ctx.char.id, ctx.char.uuid, ctx.level, ctx.now)),
          granted: { xp: granted, gold: reward.gold },
          xp: ctx.xp,
          gold: ctx.gold,
          delta: ctx.delta(),
        },
      };
    },
  });
}
