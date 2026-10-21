// 길드 규칙 상수(Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 1절)
export const GUILD_MAX_MEMBERS = 30;
export const GUILD_MAX_OFFICERS = 3;
export const GUILD_CREATE_LEVEL = 20;
/** 만들기 비용: 무자본이 Lv20 무렵 모은 골드의 일부(경제가 조금 모자라게 설계돼 있어 가볍지 않은 지출) */
// 2026-10-21 100,000 -> 30,000: Lv20 누적 수입 약 208,000에서 100,000을 쓰면 Lv20~30 강화를 못 했다(Lv20 수입의 15% 수준)
export const GUILD_CREATE_GOLD = 30_000;
export const GUILD_MAX_PENDING_APPLICATIONS = 3;
/** 탈퇴·추방·해체 뒤 다른 길드 가입 대기 */
export const GUILD_COOLDOWN_MS = 24 * 60 * 60 * 1000;
/** 길드보스는 가입 24시간 뒤부터 */
export const GUILD_BOSS_JOIN_WAIT_MS = 24 * 60 * 60 * 1000;
export const GUILD_NAME_MIN = 2;
export const GUILD_NAME_MAX = 12;
export const GUILD_NOTICE_MAX = 60;
export const GUILD_PAGE_SIZE = 20;
export const GUILD_NAME_RE = /^[가-힣A-Za-z0-9]+$/;

export type GuildRole = 'leader' | 'officer' | 'member';
export type JoinMode = 'open' | 'approval';

/** 내보낼 수 있나: 길드장은 길드장 말고 누구나, 부길드장은 길드원만 */
export const canKick = (actor: GuildRole, target: GuildRole): boolean =>
  actor === 'leader' ? target !== 'leader' : actor === 'officer' ? target === 'member' : false;
export const canManage = (role: GuildRole): boolean => role === 'leader' || role === 'officer';
