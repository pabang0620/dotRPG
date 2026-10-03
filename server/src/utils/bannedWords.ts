// 모집 메시지 금칙어. 게임 데이터가 아니라 운영 목록(server/data/banned_words.json, 5단계 채팅과 공유). 파일이 없으면 빈 목록.
import fs from 'node:fs';
import path from 'node:path';
import { getConfig } from '../config/env';

let forced: string[] | null = null;

/** 테스트 전용. null이면 파일을 읽는다 */
export function setBannedWords(words: string[] | null): void {
  forced = words ? words.map((w) => w.toLowerCase()) : null;
}

let cache: { file: string; mtime: number; words: string[] } | null = null;

function load(): string[] {
  if (forced) return forced;
  const file = path.join(getConfig().gameDataDir, 'banned_words.json');
  let stat: fs.Stats;
  try {
    stat = fs.statSync(file);
  } catch {
    return [];
  }
  if (cache && cache.file === file && cache.mtime === stat.mtimeMs) return cache.words;
  const raw: unknown = JSON.parse(fs.readFileSync(file, 'utf8'));
  const list = Array.isArray(raw) ? raw : (raw as { words?: unknown }).words;
  const words = (Array.isArray(list) ? list : []).filter((w): w is string => typeof w === 'string' && w.length > 0).map((w) => w.toLowerCase());
  cache = { file, mtime: stat.mtimeMs, words };
  return words;
}

export function containsBannedWord(text: string): boolean {
  const t = text.toLowerCase();
  return load().some((w) => t.includes(w));
}
