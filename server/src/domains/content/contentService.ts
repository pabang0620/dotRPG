// 챕터 3 이야기 묶음(Docs/ch3/LIVE_CONTENT.md 7절 스포일러 보호).
// 3장 퀘스트·대사·컷신 텍스트는 클라이언트 빌드에 없고, 3장이 열린 서버만 이 묶음을 내준다.
// 파일은 GameDataExport가 쓰는 data/chapter3.json({ schema, bundle, lang })이고 데이터 버전 해시에 들어간다.
import fs from 'node:fs';
import path from 'node:path';
import { getConfig } from '../../config/env';
import { chapter3Open } from '../../gamedata/chapterGate';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';

export const CHAPTER3_FILE = 'chapter3.json';

export interface Chapter3Content {
  data_version: string;
  /** 묶음 원문(JSON 문자열). 클라이언트가 그대로 Chapter3Content.Merge에 넘긴다 */
  bundle: string;
  /** 언어 코드 -> { 한국어: 번역 } */
  lang: Record<string, Record<string, string>>;
}

// 같은 데이터 폴더·버전이면 파일을 다시 읽지 않는다(응답 캐시)
let cache: { key: string; content: Chapter3Content } | null = null;

function load(): Chapter3Content {
  const dir = getConfig().gameDataDir;
  const version = getGameData().dataVersion;
  const key = `${dir}|${version}`;
  if (cache?.key === key) return cache.content;
  let raw: { bundle?: unknown; lang?: unknown };
  try {
    raw = JSON.parse(fs.readFileSync(path.join(dir, CHAPTER3_FILE), 'utf8')) as typeof raw;
  } catch {
    throw new AppError(503, '이야기 데이터를 읽을 수 없습니다.', 'CONTENT_UNAVAILABLE');
  }
  if (!raw || typeof raw.bundle !== 'object' || raw.bundle === null) {
    throw new AppError(503, '이야기 데이터를 읽을 수 없습니다.', 'CONTENT_UNAVAILABLE');
  }
  const lang = (typeof raw.lang === 'object' && raw.lang !== null ? raw.lang : {}) as Chapter3Content['lang'];
  const content: Chapter3Content = { data_version: version, bundle: JSON.stringify(raw.bundle), lang };
  cache = { key, content };
  return content;
}

/** 3장이 닫혀 있으면 422 CHAPTER_LOCKED(묶음 내용은 한 글자도 보내지 않는다) */
export function getChapter3Content(): Chapter3Content {
  if (!chapter3Open()) {
    throw new AppError(422, '아직 열리지 않은 콘텐츠입니다.', 'CHAPTER_LOCKED', { kind: 'content', id: 'chapter3' });
  }
  return load();
}
