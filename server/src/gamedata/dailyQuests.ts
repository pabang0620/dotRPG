// 일일·주간 의뢰 표(server/data/daily_quests.json, Docs/server/phase14_daily_quests.md 1절, 6절).
// level_rewards.json 과 같은 방식: 서버 정본, data_version 해시에 넣지 않는다(클라이언트는 GET /characters/:uuid/dailies 로 받는다).
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { getConfig } from '../config/env';
import { getGameData } from './loader';

const text = z.string().min(1).max(200);
const id = z.string().regex(/^[a-z0-9_]{1,64}$/);
const killTemplate = z.strictObject({
  id,
  type: z.literal('kill'),
  title: text,
  text,
  map: z.string().min(1),
  monster: z.string().min(1),
  label: text,
  count: z.number().int().min(1).max(1000),
});
const dungeonTemplate = z.strictObject({ id, type: z.literal('dungeon'), title: text, text, count: z.number().int().min(1).max(20) });
/** 주간 전용: 수락 이후 그 레이드 클리어 수 */
const raidTemplate = z.strictObject({ id, type: z.literal('raid'), title: text, text, target: z.string().min(1), count: z.number().int().min(1).max(10) });
/** 주간 전용: 수락 이후 청구한 일일 의뢰 수 */
const dailyTemplate = z.strictObject({ id, type: z.literal('daily'), title: text, text, count: z.number().int().min(1).max(50) });
const templateSchema = z.discriminatedUnion('type', [killTemplate, dungeonTemplate, raidTemplate, dailyTemplate]);
const bandSchema = z.strictObject({
  id: z.string().min(1),
  minLevel: z.number().int().min(1),
  maxLevel: z.number().int().min(1),
  giver: text.optional(),
  gold: z.number().int().min(0),
  templates: z.array(templateSchema).min(1),
});
const weeklySchema = z.strictObject({
  perWeek: z.number().int().min(1).max(10),
  xpShare: z.number().min(0).max(1),
  maxLevelGold: z.number().int().min(0),
  bands: z.array(bandSchema).min(1),
});
const fileSchema = z.strictObject({
  unlockQuest: z.string().min(1),
  perDay: z.number().int().min(1).max(10),
  carryDays: z.number().int().min(0).max(7),
  xpShare: z.number().min(0).max(1),
  maxLevelGold: z.number().int().min(0),
  bands: z.array(bandSchema).min(1),
  weekly: weeklySchema,
});

export type DailyTemplate = z.infer<typeof templateSchema>;
export type DailyBand = z.infer<typeof bandSchema>;
export type Period = 'day' | 'week';

/** 한 기간(일일 또는 주간)의 의뢰판 */
export interface Board {
  period: Period;
  /** 기간당 칸 수 */
  per: number;
  /** 지난 기간 칸을 이어서 할 수 있는 기간 수(주간은 0) */
  carry: number;
  xpShare: number;
  maxLevelGold: number;
  bands: DailyBand[];
}
export interface DailyData {
  unlockQuest: string;
  day: Board;
  week: Board;
}

/** 맵의 필드 스폰에서 그 몬스터의 레벨(여러 줄이면 가장 낮은 값). 없으면 null */
export function fieldLevel(mapId: string, monsterId: string): number | null {
  const spawns = getGameData().economy.mapExtra.get(mapId)?.fieldSpawns ?? [];
  const levels = spawns.filter((s) => s.monsterId === monsterId).map((s) => s.level);
  return levels.length > 0 ? Math.min(...levels) : null;
}

function checkBoard(b: Board, errors: string[]): void {
  const raids = new Set([...getGameData().economy.dungeons.byId.values()].filter((d) => d.isRaid).map((d) => d.id));
  const seen = new Set<string>();
  let prevMax = 0;
  for (const band of b.bands) {
    const where = `${b.period}.${band.id}`;
    if (band.minLevel > band.maxLevel) errors.push(`${where}: minLevel > maxLevel`);
    if (band.minLevel <= prevMax) errors.push(`${where}: 레벨 구간이 앞 구간과 겹칩니다`);
    prevMax = band.maxLevel;
    for (const t of band.templates) {
      if (seen.has(t.id)) errors.push(`${b.period} 의뢰 id 중복: ${t.id}`);
      seen.add(t.id);
      if (t.type === 'kill' && fieldLevel(t.map, t.monster) === null) errors.push(`${t.id}: ${t.map} 의 필드 몬스터에 ${t.monster} 가 없습니다`);
      if (t.type === 'raid' && !raids.has(t.target)) errors.push(`${t.id}: 레이드가 아닙니다 ${t.target}`);
      if (b.period === 'day' && (t.type === 'raid' || t.type === 'daily')) errors.push(`${t.id}: 일일 의뢰에 ${t.type} 형식은 쓸 수 없습니다`);
    }
  }
}

/** 검증 오류 목록(빈 배열 = 정상). 맵·몬스터·레이드·퀘스트 참조는 로드된 게임 데이터와 대조한다 */
export function validateDailyQuests(raw: unknown): { data: DailyData | null; errors: string[] } {
  const p = fileSchema.safeParse(raw);
  if (!p.success) return { data: null, errors: p.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`) };
  const f = p.data;
  const data: DailyData = {
    unlockQuest: f.unlockQuest,
    day: { period: 'day', per: f.perDay, carry: f.carryDays, xpShare: f.xpShare, maxLevelGold: f.maxLevelGold, bands: f.bands },
    week: { period: 'week', per: f.weekly.perWeek, carry: 0, xpShare: f.weekly.xpShare, maxLevelGold: f.weekly.maxLevelGold, bands: f.weekly.bands },
  };
  const errors: string[] = [];
  if (!getGameData().economy.quests.has(f.unlockQuest)) errors.push(`unlockQuest 가 quest_index.json 에 없습니다: ${f.unlockQuest}`);
  checkBoard(data.day, errors);
  checkBoard(data.week, errors);
  return { data, errors };
}

let current: DailyData | null = null;

/** 파일이 없거나 형식이 틀리면 던진다(의뢰가 조용히 사라지면 안 된다) */
export function loadDailyQuests(dir: string = getConfig().gameDataDir): DailyData {
  const file = path.join(dir, 'daily_quests.json');
  if (!fs.existsSync(file)) throw new Error('daily_quests.json 이 없습니다');
  const { data, errors } = validateDailyQuests(JSON.parse(fs.readFileSync(file, 'utf8')) as unknown);
  if (!data || errors.length > 0) throw new Error(`daily_quests.json 검증 실패: ${errors.join('; ')}`);
  current = data;
  return data;
}

export function getDailyQuests(): DailyData {
  return current ?? loadDailyQuests();
}

/** 레벨이 속한 구간. 첫 구간보다 낮으면 첫 구간, 마지막보다 높으면 마지막 구간 */
export function bandFor(board: Board, level: number): DailyBand {
  const bands = board.bands;
  for (const b of bands) if (level >= b.minLevel && level <= b.maxLevel) return b;
  return level < (bands[0] as DailyBand).minLevel ? (bands[0] as DailyBand) : (bands[bands.length - 1] as DailyBand);
}

/** 의뢰 id로 의뢰와 그 구간 */
export function findTemplate(board: Board, templateId: string): { band: DailyBand; template: DailyTemplate } | null {
  for (const band of board.bands) {
    const template = band.templates.find((t) => t.id === templateId);
    if (template) return { band, template };
  }
  return null;
}
