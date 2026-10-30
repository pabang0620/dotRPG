// 월드보스·길드보스 순수 규칙(DB 없음). 설계: Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 2~3절.
import { levelCap } from '../../gamedata/chapterGate';
export type BossKind = 'world' | 'guild';

/** 한 번 도전 시간(초). 클라이언트 타이머와 같다 */
export const ATTEMPT_SECONDS = 180;
/** 끝 보고를 받는 여유(초): 3분이 끝난 뒤 결과 화면·네트워크 지연 */
export const FINISH_GRACE_SECONDS = 60;
export const ATTEMPTS_PER_DAY: Record<BossKind, number> = { world: 3, guild: 2 };

/** 월드보스: 첫 보스. 그림은 기존 보스를 키우고 색을 바꾼다(클라이언트 MonsterDatabase에 같은 id) */
export const WORLD_BOSS_ID = 'boss_abyss_titan';
/** 길드보스도 같은 보스를 같은 세기로 쓴다(점수 경쟁이라 죽이지 않는다). 클라이언트에 정의가 하나뿐이다 */
export const GUILD_BOSS_ID = 'boss_abyss_titan';
/** 도전 최소 레벨. 챕터 3이 열려도 40 고정(열리는 순간 Lv40 전원이 밀려나지 않게, Docs/ch3/LIVE_CONTENT.md) */
export const BOSS_MIN_LEVEL = 40;
/** 방 안 보스 레벨 = 지금 레벨 상한(levelCap). 3장이 닫혀 있으면 40, 열리면 50 */
export const bossLevel = (): number => levelCap();
/** 방 안 보스 체력 배율: 클라이언트 기본 배율(BossRushCatalog, 그라흐 레이드 약 10판 분량)에 곱한다. 실제 승부는 서버 공유 체력 */
export const ROOM_HP_MUL = 1;

/**
 * Lv40 숙련자 3분 한 번 피해: 2026-10-20 보스 방 실측(-measureOnly bossrush, 측정 봇, 직업 4종 x 혼자/AI 3명 x 장비 2벌).
 * 3분을 버틴 판 기준 레이드 장비 6만~15만, 클리어 장비 7만~34만 -> 대표값 150,000.
 * (처음 추정 15,000은 사냥터 몬스터 기준이라 보스 한 마리에 집중하는 화력을 10배 낮게 봤다)
 */
export const EST_ATTEMPT_DAMAGE = 150_000;

/**
 * 월드보스 시즌 체력(2026-10-30: 시즌이 1주에서 2주로 늘어 40,000,000 x 2).
 * 근거(2026-10-21): 실측 평균 1회 피해 110,000(레이드 장비) x 하루 3회 x 7일 x 참여율 0.5 = 한 사람당 주 1,155,000.
 * 2주 80,000,000이면 참가자 35명 안팎이 시즌 첫 주 끝 무렵에 격파하고(예전 1주 격파와 같은 시점), 20명이면 1.7주다.
 * env WORLD_BOSS_HP는 "한 시즌(2주) 체력"이다. 참가자가 100명 수준이면 320,000,000으로 올린다(새 시즌을 만들 때만 읽는다).
 */
export const WORLD_BOSS_HP_DEFAULT = 80_000_000;

// ---------- 월드보스 시즌(2주) ----------

const WEEK_MS = 7 * 24 * 60 * 60 * 1000;
/** 월드보스 시즌 길이(2026-10-30 사용자 결정: 1주 -> 2주. 길드보스는 1주 그대로) */
export const WORLD_SEASON_WEEKS = 2;
/**
 * 2주 시즌의 기준점: 2026-10-29(목) 06:00 KST. 시즌은 이 시각부터 2주마다 바뀐다(모든 서버가 같은 상수로 같은 경계를 쓴다).
 * 주간 경계(목요일 06:00 KST, resetBoundaries) 중 기준점에서 짝수 번째 주만 시즌 시작이다
 */
export const WORLD_SEASON_ANCHOR_MS = Date.UTC(2026, 9, 28, 21, 0, 0);

/** now가 속한 월드보스 시즌의 시작·끝. weeklyStartMs는 resetBoundaries(now).weeklyStartAt */
export function worldSeasonBounds(weeklyStartMs: number): { start: Date; end: Date } {
  const weeks = Math.round((weeklyStartMs - WORLD_SEASON_ANCHOR_MS) / WEEK_MS);
  const offset = ((weeks % WORLD_SEASON_WEEKS) + WORLD_SEASON_WEEKS) % WORLD_SEASON_WEEKS;
  const start = weeklyStartMs - offset * WEEK_MS;
  return { start: new Date(start), end: new Date(start + WORLD_SEASON_WEEKS * WEEK_MS) };
}

/** 챕터 3 보스 레벨(50) 시즌의 월드보스 체력 배율(Docs/ch3/LIVE_CONTENT.md 2절). 조정은 env WORLD_BOSS_HP로 한다 */
export const LV50_WORLD_HP_MUL = 2.0;
/** 시즌 생성 때 정하는 월드보스 체력: 보스 레벨 50 이상이면 x2.0 */
export const worldSeasonHp = (baseHp: number, level: number): number => (level >= 50 ? Math.round(baseHp * LV50_WORLD_HP_MUL) : baseHp);

// 길드보스(2026-10-20 사용자 결정): 죽이는 보스가 아니라 점수를 쌓는다. 길드원 도전마다 인정 피해가 그 주 길드 점수에 더해지고,
// 길드 랭킹은 점수 합(같으면 먼저 도달한 길드). 30명 전원이 주 내내 하면 420회 x 1회 150,000 = 약 6,300만.

/**
 * 피해 상한 배율: 화력 상한(attackCap 기반 초당 상한)이 실제 보스 피해보다 훨씬 넉넉하다(2026-10-20 실측: 1.5배일 때 실제 피해가
 * 상한의 3~19%). 0.6이면 실측 최고(클리어 장비 전사 34만)가 상한의 약 47%라 정상 플레이는 잘리지 않고, 조작 여지는 약 2배로 줄어든다.
 */
export const DAMAGE_SLACK = 0.6;

/** 한 번 도전 피해 상한: 초당 화력 상한 x 실제 걸린 시간(최대 3분) x 여유 */
export function attemptDamageCap(dpsCap: number, elapsedSeconds: number): number {
  const t = Math.max(1, Math.min(ATTEMPT_SECONDS, elapsedSeconds));
  return Math.floor(dpsCap * t * DAMAGE_SLACK);
}

// ---------- 주간 보상(명예 위주) ----------

// 월드보스 보상은 시즌(2주)마다 한 번이라, 주당 수입이 예전 1주 시즌과 같도록 두 배로 둔다(2026-10-30).
// 보스 우편 수입은 theory_gold.py·income_caps.json에 들어 있지 않다(무자본 비율 계산 밖). 주당 액수가 그대로라 비율에 영향 없음
export const WORLD_JOIN_GOLD = 60_000;
export const WORLD_JOIN_ORE = 10;
export const WORLD_DEFEAT_GOLD = 60_000;
/** 길드보스 참가 보상(그 주 1회 이상 끝까지 도전한 길드원) */
export const GUILD_JOIN_GOLD = 20_000;
/** 강화석(items.json mat_ore) */
export const ORE_ITEM = 'mat_ore';
export const ESSENCE_ITEM = 'mat_essence';
export const PROTECT_ITEM = 'ticket_protect';

/**
 * 길드보스 1~10위 길드원 보상(2026-10-30 사용자 결정, 주간 정산 우편). 정산 시각에 그 길드에 있던 길드원(그 주가 끝나기 전에 가입) 전원.
 * Lv40 사냥 1시간 수입이 골드 환산 약 77,000·마력 정수 148개(income_caps.json 41~44)라 1위도 사냥 약 12분어치 골드다.
 * 보스 우편은 theory_gold.py 무자본 비율 계산 밖이고, 한 사람이 받는 최대치는 주 15,000골드(참가 20,000과 합쳐 35,000)
 */
export interface GuildTierReward { gold: number; items: { itemKey: string; count: number }[] }
export function guildTierReward(rank: number): GuildTierReward | null {
  if (rank === 1) return { gold: 15_000, items: [{ itemKey: PROTECT_ITEM, count: 1 }, { itemKey: ESSENCE_ITEM, count: 30 }] };
  if (rank <= 3) return { gold: 10_000, items: [{ itemKey: ESSENCE_ITEM, count: 20 }] };
  if (rank <= 10) return { gold: 5_000, items: [{ itemKey: ESSENCE_ITEM, count: 10 }] };
  return null;
}

export type RankTitleId = 'rank_world_1' | 'rank_world_10' | 'rank_world_100' | 'rank_guild_1';
export const RANK_TITLE_NAMES: Record<RankTitleId, string> = {
  rank_world_1: '세계를 꺾은 자',
  rank_world_10: '거신 사냥꾼',
  rank_world_100: '거신에 맞선 자',
  rank_guild_1: '최강 길드',
};
export const isRankTitle = (id: string): id is RankTitleId => Object.prototype.hasOwnProperty.call(RANK_TITLE_NAMES, id);

/** 영구 칭호(2026-10-30): 월드보스 순위 칭호는 한 번 받으면 만료가 없다. 길드보스 "최강 길드"는 다음 주 정산까지 */
export const isPermanentRankTitle = (id: string): boolean => id.startsWith('rank_world_');

/** 월드보스 순위 -> 칭호·이름 색 */
export function worldRankReward(rank: number): { title: RankTitleId; color: 'gold' | 'orange' | null } | null {
  if (rank === 1) return { title: 'rank_world_1', color: 'gold' };
  if (rank <= 10) return { title: 'rank_world_10', color: 'orange' };
  if (rank <= 100) return { title: 'rank_world_100', color: null };
  return null;
}

/** 길드보스 순위 -> 길드 이름 색 */
export const guildRankColor = (rank: number): 'gold' | 'silver' | null => (rank === 1 ? 'gold' : rank <= 10 ? 'silver' : null);

/** 이름 색이 여럿이면 금색이 이긴다 */
export function strongestColor(colors: (string | null)[]): 'gold' | 'orange' | null {
  if (colors.includes('gold')) return 'gold';
  if (colors.includes('orange')) return 'orange';
  return null;
}
