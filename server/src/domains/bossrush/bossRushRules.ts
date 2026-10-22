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
 * 월드보스 주간 체력(2026-10-21 하향): 실측 평균 1회 피해 110,000(레이드 장비) x 하루 3회 x 7일 x 참여율 0.5 = 한 사람당 주 1,155,000.
 * 40,000,000이면 참가자 35명 안팎이 한 주에 격파하고, 20명이면 1.7주다. 출시 초 인원에서도 격파 보너스가 나오게 작게 시작하고,
 * 참가자가 100명 수준이면 env WORLD_BOSS_HP를 160,000,000으로 올린다(새 주의 시즌을 만들 때만 읽는다).
 */
export const WORLD_BOSS_HP_DEFAULT = 40_000_000;

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

export const WORLD_JOIN_GOLD = 30_000;
export const WORLD_JOIN_ORE = 5;
export const WORLD_DEFEAT_GOLD = 30_000;
/** 길드보스 참가 보상(그 주 1회 이상 끝까지 도전한 길드원) */
export const GUILD_JOIN_GOLD = 20_000;
/** 강화석(items.json mat_ore) */
export const ORE_ITEM = 'mat_ore';

export type RankTitleId = 'rank_world_1' | 'rank_world_10' | 'rank_world_100' | 'rank_guild_1';
export const RANK_TITLE_NAMES: Record<RankTitleId, string> = {
  rank_world_1: '세계를 꺾은 자',
  rank_world_10: '거신 사냥꾼',
  rank_world_100: '거신에 맞선 자',
  rank_guild_1: '최강 길드',
};
export const isRankTitle = (id: string): id is RankTitleId => Object.prototype.hasOwnProperty.call(RANK_TITLE_NAMES, id);

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
