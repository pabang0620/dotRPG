// 시즌 랭킹(월드보스 개인 2주 시즌, 길드보스 길드 주간, 전투력 개인). 전체 순서를 1분 동안 메모리에 캐시하고 100위까지 내려 준다.
import { getPool, type Queryable } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import * as bossRepo from '../bossrush/bossRushRepository';
import { WORLD_SEASON_WEEKS, worldSeasonBounds, type BossKind } from '../bossrush/bossRushRules';
import { computePower } from '../characters/powerEstimate';
import * as repo from './rankingRepository';

export type Board = 'world_boss' | 'guild_boss' | 'power';
export type Week = 'current' | 'previous';

const CACHE_MS = 60_000;
const TOP = 100;
const POWER_MIN_LEVEL = 20;
const WEEK_MS = 7 * 24 * 60 * 60 * 1000;

/** 정렬된 전체 순서. key는 캐릭터 id(world_boss·power) 또는 길드 id(guild_boss) */
interface Ranked { key: number; value: number }
const cache = new Map<string, { at: number; rows: Ranked[]; updatedAt: Date }>();

/** 테스트·정산 직후: 캐시를 비운다 */
export function clearRankingCache(): void {
  cache.clear();
}

export function weekStartOf(week: Week, now: Date): Date {
  const start = new Date(resetBoundaries(now).weeklyStartAt);
  return week === 'current' ? start : new Date(start.getTime() - WEEK_MS);
}

/**
 * 보드의 시즌 시작. 길드보스·전투력은 주간 경계 그대로. 월드보스(2주 시즌, 2026-10-30)는 current = now를 포함하는 시즌,
 * previous = now 전에 끝난 가장 최근 시즌(행이 아직 없으면 계산한 시즌 경계로 빈 보드)
 */
export async function seasonStartOf(db: Queryable, board: Board, week: Week, now: Date): Promise<Date> {
  if (board !== 'world_boss') return weekStartOf(week, now);
  const own = worldSeasonBounds(Date.parse(resetBoundaries(now).weeklyStartAt));
  if (week === 'current') return (await bossRepo.seasonCovering(db, 'world', now))?.week_start ?? own.start;
  const prev = await bossRepo.lastEndedSeason(db, 'world', now);
  return prev?.week_start ?? new Date(own.start.getTime() - WORLD_SEASON_WEEKS * WEEK_MS);
}

async function compute(db: Queryable, board: Board, weekStart: Date): Promise<Ranked[]> {
  if (board === 'power') {
    const rows = await repo.powerCandidates(db, POWER_MIN_LEVEL);
    return rows
      .map((c) => ({ key: c.id, value: computePower(c.class, c.level, c.worn, c.bonus_hp) }))
      .sort((a, b) => b.value - a.value || a.key - b.key);
  }
  const kind: BossKind = board === 'world_boss' ? 'world' : 'guild';
  const season = await bossRepo.seasonAt(db, kind, weekStart);
  if (!season) return [];
  if (kind === 'world') return (await bossRepo.worldBoard(db, season.id)).map((e) => ({ key: e.character_id, value: e.best }));
  const alive = await db.query<{ id: string }>('SELECT id FROM guilds WHERE disbanded_at IS NULL');
  const aliveIds = new Set(alive.rows.map((x) => Number(x.id)));
  // 길드보스: 이번 주 점수 합, 같으면 그 점수에 먼저 도달한 길드
  return (await bossRepo.scoresOf(db, season.id))
    .filter((g) => aliveIds.has(g.guild_id) && g.score > 0)
    .sort((a, b) => b.score - a.score || a.scored_at.getTime() - b.scored_at.getTime() || a.guild_id - b.guild_id)
    .map((g) => ({ key: g.guild_id, value: g.score }));
}

/** 정렬된 전체 순서(캐시). fresh면 캐시를 쓰지 않는다(정산) */
export async function ranked(board: Board, weekStart: Date, opts: { fresh?: boolean; db?: Queryable } = {}): Promise<{ rows: Ranked[]; updatedAt: Date }> {
  const key = `${board}:${weekStart.toISOString()}`;
  const hit = cache.get(key);
  const nowMs = Date.now();
  if (!opts.fresh && hit && nowMs - hit.at < CACHE_MS) return hit;
  const rows = await compute(opts.db ?? getPool(), board, weekStart);
  const entry = { at: nowMs, rows, updatedAt: getNow() };
  if (!opts.fresh) cache.set(key, entry);
  return entry;
}

/** 길드보스 순위 색: 1위 gold, 2~10위 silver, 그 밖은 null */
export const guildColorOf = (rank: number): 'gold' | 'silver' | null => (rank === 1 ? 'gold' : rank <= 10 ? 'silver' : null);

/** 순위(1부터). 없으면 null */
export const rankOf = (rows: Ranked[], key: number): number | null => {
  const i = rows.findIndex((r) => r.key === key);
  return i < 0 ? null : i + 1;
};

export async function board(board: Board, week: Week, me: { characterId: number; guildId: number | null } | null) {
  const now = getNow();
  const db = getPool();
  const seasonStart = await seasonStartOf(db, board, week, now);
  const { rows, updatedAt } = await ranked(board, seasonStart);
  const top = rows.slice(0, TOP);
  let entries: unknown[];
  let mine: { rank: number; value: number } | null = null;
  if (board === 'guild_boss') {
    const names = await repo.guildNames(db, top.map((r) => r.key));
    // guild_color: 1위 금색, 2~10위 은색(설계 3절). 순위 그대로라 지난주 보드도 그 주의 순위 색이다
    entries = top.map((r, i) => ({ rank: i + 1, guild_name: names.get(r.key) ?? '', value: r.value, guild_color: guildColorOf(i + 1) }));
    if (me?.guildId) {
      const rank = rankOf(rows, me.guildId);
      if (rank) mine = { rank, value: (rows[rank - 1] as Ranked).value };
    }
  } else {
    const shown = await repo.displays(db, top.map((r) => r.key), now);
    entries = top.map((r, i) => {
      const d = shown.get(r.key);
      return { rank: i + 1, name: d?.name ?? '', guild_name: d?.guild_name ?? null, level: d?.level ?? 0, career: d?.career ?? 0, value: r.value, name_color: d?.name_color ?? null };
    });
    if (me) {
      const rank = rankOf(rows, me.characterId);
      if (rank) mine = { rank, value: (rows[rank - 1] as Ranked).value };
    }
  }
  // season_start·season_end: 그 보드 시즌의 경계(월드보스는 2주). 클라이언트 "이번 시즌 / 지난 시즌" 표시용
  const season = board === 'power' ? null : await bossRepo.seasonAt(db, board === 'world_boss' ? 'world' : 'guild', seasonStart);
  return { board, week, entries, me: mine, updated_at: updatedAt.toISOString(), season_start: seasonStart.toISOString(), season_end: season?.week_end.toISOString() ?? null };
}
