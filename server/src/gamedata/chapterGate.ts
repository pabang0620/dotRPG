// 챕터 3 잠금(Docs/PLAN_CHAPTER3_2026-10-21.md, Docs/ch3/LIVE_CONTENT.md 잠금 절).
// 출시 빌드에는 3장 데이터가 실리고, 서버 env CHAPTER3_OPEN 하나로 연다. 레벨 상한과 3장 콘텐츠 진입은 전부 이 파일을 거친다.
import { getConfig } from '../config/env';
import { AppError } from '../utils/AppError';
import { getGameData } from './loader';

/** 챕터 2까지의 만렙. 3장이 닫혀 있으면 데이터 maxLevel이 더 커도 여기서 멈춘다 */
export const CHAPTER2_LEVEL_CAP = 40;
/** 챕터 3 만렙 */
export const CHAPTER3_LEVEL_CAP = 50;

export const chapter3Open = (): boolean => getConfig().policy.chapter3Open;

/**
 * 지금 오를 수 있는 최고 레벨: 게임 데이터 maxLevel과 챕터 상한 중 작은 쪽.
 * 데이터가 아직 40이면 플래그를 켜도 40이다(1단계에서 데이터가 50이 된다).
 */
export function levelCap(dataMaxLevel: number = getGameData().economy.progression.maxLevel): number {
  return Math.min(dataMaxLevel, chapter3Open() ? CHAPTER3_LEVEL_CAP : CHAPTER2_LEVEL_CAP);
}

/** 3장 태그가 달릴 수 있는 콘텐츠 종류(태그는 1단계부터 데이터에 chapter: 3 으로 채운다) */
export type ChapterKind = 'quest' | 'map' | 'dungeon' | 'monster' | 'item' | 'shop' | 'spec';

type Tagged = { chapter?: number } | undefined;

/** 그 콘텐츠의 챕터 번호. 데이터에 chapter 필드가 없으면 0(기존 콘텐츠, 항상 열림) */
function chapterOf(kind: ChapterKind, id: string): number {
  const d = getGameData() as unknown as Record<string, unknown>;
  const eco = (d.economy ?? {}) as Record<string, unknown>;
  const pick = (m: unknown): Tagged => (m instanceof Map ? (m.get(id) as Tagged) : undefined);
  const source: Record<ChapterKind, unknown> = {
    quest: eco.quests,
    map: d.maps,
    dungeon: eco.dungeons,
    monster: eco.monsters,
    item: eco.items,
    shop: eco.shop,
    spec: eco.specs,
  };
  return pick(source[kind])?.chapter ?? 0;
}

export function isChapter3(kind: ChapterKind, id: string): boolean {
  return chapterOf(kind, id) >= 3;
}

/** 3장 콘텐츠이고 아직 닫혀 있으면 422 CHAPTER_LOCKED */
export function assertOpen(kind: ChapterKind, id: string): void {
  if (isChapter3(kind, id) && !chapter3Open()) {
    throw new AppError(422, '아직 열리지 않은 콘텐츠입니다.', 'CHAPTER_LOCKED', { kind, id });
  }
}
