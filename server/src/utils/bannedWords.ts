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

const norm = (s: string): string => s.normalize('NFKC').toLowerCase();

/**
 * 채팅용 가림. NFKC 소문자 사본에서 금칙어를 찾아 원문의 같은 구간(코드포인트 단위)을 '*'로 바꾼다. 길이는 유지된다.
 * 공백·기호로 숨긴 변형은 여기서 잡히지 않는다(hasObfuscatedBannedWord).
 */
export function maskBannedWords(text: string): { text: string; hit: boolean } {
  const words = load()
    .map(norm)
    .filter((w) => w.length > 0);
  if (words.length === 0) return { text, hit: false };
  const original = Array.from(text);
  let folded = '';
  const owner: number[] = []; // 정규화 문자열의 각 UTF-16 위치 -> 원문 코드포인트 번호
  original.forEach((ch, i) => {
    const n = norm(ch);
    for (let k = 0; k < n.length; k++) owner.push(i);
    folded += n;
  });
  const masked = new Set<number>();
  for (const w of words) {
    let from = 0;
    for (;;) {
      const at = folded.indexOf(w, from);
      if (at < 0) break;
      for (let k = at; k < at + w.length; k++) masked.add(owner[k] as number);
      from = at + 1;
    }
  }
  if (masked.size === 0) return { text, hit: false };
  return { text: original.map((ch, i) => (masked.has(i) ? '*' : ch)).join(''), hit: true };
}

const countOf = (hay: string, needle: string): number => {
  let n = 0;
  for (let at = hay.indexOf(needle); at >= 0; at = hay.indexOf(needle, at + 1)) n++;
  return n;
};

/**
 * 공백·기호·점을 지운 사본에서 금칙어가 원문보다 더 많이 나오면(예: "시 발", "씨.발") 숨기려는 변형으로 본다.
 * 원문 그대로의 금칙어를 함께 섞어 가림 쪽으로 빠져나가지 못하게 횟수로 비교한다.
 */
export function hasObfuscatedBannedWord(text: string): boolean {
  const plain = norm(text);
  const stripped = plain.replace(/[^\p{L}\p{N}]/gu, '');
  return load()
    .map(norm)
    .some((w) => w.length > 0 && countOf(stripped, w) > countOf(plain, w));
}
