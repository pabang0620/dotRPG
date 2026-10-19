// 월드보스·길드보스 순수 규칙(DB 없음). 설계: Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 2~3절.
export type BossKind = 'world' | 'guild';

/** 한 번 도전 시간(초). 클라이언트 타이머와 같다 */
export const ATTEMPT_SECONDS = 180;
/** 끝 보고를 받는 여유(초): 3분이 끝난 뒤 결과 화면·네트워크 지연 */
export const FINISH_GRACE_SECONDS = 60;
export const ATTEMPTS_PER_DAY: Record<BossKind, number> = { world: 3, guild: 2 };

/** 월드보스: 첫 보스. 그림은 기존 보스를 키우고 색을 바꾼다(클라이언트 MonsterDatabase에 같은 id) */
export const WORLD_BOSS_ID = 'boss_abyss_titan';
/** 길드보스도 같은 보스를 단계 배율(stageDamageMul)로 쓴다. 클라이언트에 정의가 하나뿐이다 */
export const GUILD_BOSS_ID = 'boss_abyss_titan';
/** 전투 방의 보스 레벨. 만렙과 같다 */
export const BOSS_LEVEL = 40;
/** 방 안 보스 체력 배율: 클라이언트 기본 배율(BossRushCatalog, 그라흐 레이드 약 10판 분량)에 곱한다. 실제 승부는 서버 공유 체력 */
export const ROOM_HP_MUL = 1;

/**
 * Lv40 숙련자 3분 한 번 피해 추정: 2026-10-16 실측 Lv40 성소 분당 17~19마리 x Lv40 일반 몬스터 실효 체력 약 220
 * = 초당 약 65, 버프·스킬 집중을 더해 초당 약 80 x 180초 = 약 15,000.
 */
export const EST_ATTEMPT_DAMAGE = 15_000;

/**
 * 월드보스 주간 체력: 1회 추정 15,000 x 하루 3회 x 7일 x 예상 Lv40 참가자 100명 x 실제 참여율 0.5 = 15,750,000 -> 16,000,000.
 * 참가자가 예상보다 적으면 못 잡는 주가 생기고, 그 주는 랭킹만 남는다. 출시 후 env WORLD_BOSS_HP로 조정한다.
 */
export const WORLD_BOSS_HP_DEFAULT = 16_000_000;

/**
 * 길드보스 1단계 체력: 활동 길드원 5명 x 하루 2회 x 2일 x 1회 추정 15,000 = 300,000 (작은 길드도 주 초반에 1단계를 넘긴다).
 * 단계마다 체력 x1.5, 보스 피해 x1.15. 10단계까지 합 약 3,400만이라 30명 전원이 주 내내 해도(420회 x 15,000 = 630만) 7단계 안팎.
 */
export const GUILD_STAGE1_HP = 300_000;
export const GUILD_STAGE_HP_MUL = 1.5;
export const GUILD_STAGE_DAMAGE_MUL = 1.15;
export const GUILD_MAX_STAGE = 10;

/** 피해 상한 여유 배율: 화력 상한(attackCap 기반 초당 상한)도 이미 넉넉해서 1.5배만 더 둔다 */
export const DAMAGE_SLACK = 1.5;

export const stageHp = (stage: number): number => Math.round(GUILD_STAGE1_HP * GUILD_STAGE_HP_MUL ** (stage - 1));
export const stageDamageMul = (stage: number): number => Math.round(GUILD_STAGE_DAMAGE_MUL ** (stage - 1) * 1000) / 1000;

/** 길드보스에 피해를 넣는다: 단계 체력을 넘기면 남은 피해로 다음 단계를 깎는다. 10단계를 다 깎으면 hp_left 0에서 멈춘다 */
export function applyGuildDamage(stage: number, hpLeft: number, damage: number): { stage: number; hpLeft: number; stageHp: number; advanced: boolean } {
  let s = stage;
  let left = hpLeft;
  let d = damage;
  let advanced = false;
  while (d > 0) {
    if (d < left) { left -= d; d = 0; break; }
    d -= left;
    left = 0;
    if (s >= GUILD_MAX_STAGE) break;
    s += 1;
    left = stageHp(s);
    advanced = true;
  }
  return { stage: s, hpLeft: left, stageHp: stageHp(s), advanced };
}

/** 한 번 도전 피해 상한: 초당 화력 상한 x 실제 걸린 시간(최대 3분) x 여유 */
export function attemptDamageCap(dpsCap: number, elapsedSeconds: number): number {
  const t = Math.max(1, Math.min(ATTEMPT_SECONDS, elapsedSeconds));
  return Math.floor(dpsCap * t * DAMAGE_SLACK);
}

// ---------- 주간 보상(명예 위주) ----------

export const WORLD_JOIN_GOLD = 30_000;
export const WORLD_JOIN_ORE = 5;
export const WORLD_DEFEAT_GOLD = 30_000;
export const GUILD_GOLD_PER_STAGE = 10_000;
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
