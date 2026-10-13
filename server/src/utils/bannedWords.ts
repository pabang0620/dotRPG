// 채팅·모집 메시지·캐릭터 이름 금칙어. 게임 데이터가 아니라 운영 목록(server/data/banned_words.json). 파일이 없으면 빈 목록.
// words: 부분 일치(한글·한자·가나처럼 띄어 쓰지 않는 말과, 어디에 붙어도 욕인 영어). words_whole: 단어 단위
// (띄어 쓰는 언어: "ass"가 "class"에 걸리지 않게). names_only: 이름에만 쓰는 부분 일치(채팅에서는 일상어와 겹치는 말).
// allow: 금칙어를 품은 정상 단어("시발점")는 먼저 지우고 판정한다. 목록은 Tools/moderation/build_banned_words.py가 만든다.
import fs from 'node:fs';
import path from 'node:path';
import { getConfig } from '../config/env';
import { composeLooseJamo } from './hangulCompose';

interface Lists {
  sub: string[];
  /** 직접 정리한 강한 욕(띄어쓰기 우회 판정에만 쓴다. 공개 목록 전체에 쓰면 "새 키" 같은 일상 문장이 걸린다) */
  strong: string[];
  whole: string[];
  namesOnly: string[];
  allow: string[];
  wholeRe: RegExp | null;
  hangul: string[];
}

const norm = (s: string): string => s.normalize('NFKC').toLowerCase();
const clean = (list: unknown): string[] =>
  (Array.isArray(list) ? list : []).filter((w): w is string => typeof w === 'string' && w.trim().length > 0).map((w) => norm(w.trim()));
const escape = (w: string): string => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
/** 앞뒤가 글자·숫자가 아닐 때만 맞는 단어 정규식. 긴 말부터 맞춘다. */
const wholeRegex = (words: string[]): RegExp | null =>
  words.length === 0 ? null : new RegExp(`(?<![\\p{L}\\p{N}])(?:${[...words].sort((a, b) => b.length - a.length).map(escape).join('|')})(?![\\p{L}\\p{N}])`, 'gu');

function build(sub: string[], whole: string[], namesOnly: string[], allow: string[], strong: string[] = sub): Lists {
  // allow는 긴 말부터 지운다(짧은 예외가 긴 예외를 반쯤 지우지 않게)
  const allowSorted = [...allow].sort((a, b) => b.length - a.length);
  return { sub, strong, whole, namesOnly, allow: allowSorted, wholeRe: wholeRegex(whole), hangul: sub.filter((w) => /[가-힣]/u.test(w)) };
}

const EMPTY = build([], [], [], [], []);
let forced: Lists | null = null;

/** 테스트 전용. null이면 파일을 읽는다 */
export function setBannedWords(words: string[] | null, whole: string[] = [], namesOnly: string[] = [], allow: string[] = [], strong?: string[]): void {
  forced = words ? build(clean(words), clean(whole), clean(namesOnly), clean(allow), strong ? clean(strong) : clean(words)) : null;
}

/** 예외 단어 자리를 같은 길이의 빈칸(\u0000)으로 바꾼다. 위치가 그대로라 가림 위치 계산이 유지된다. */
function blankAllowed(l: Lists, t: string): string {
  let out = t;
  for (const a of l.allow) if (out.includes(a)) out = out.split(a).join('\u0000'.repeat(a.length));
  return out;
}

let cache: { file: string; mtime: number; lists: Lists } | null = null;

function load(): Lists {
  if (forced) return forced;
  const file = path.join(getConfig().gameDataDir, 'banned_words.json');
  let stat: fs.Stats;
  try {
    stat = fs.statSync(file);
  } catch {
    return EMPTY;
  }
  if (cache && cache.file === file && cache.mtime === stat.mtimeMs) return cache.lists;
  const raw: unknown = JSON.parse(fs.readFileSync(file, 'utf8'));
  const obj = (Array.isArray(raw) ? { words: raw } : raw) as { words?: unknown; words_whole?: unknown; names_only?: unknown; allow?: unknown; strong?: unknown };
  const sub = clean(obj.words);
  const lists = build(sub, clean(obj.words_whole), clean(obj.names_only), clean(obj.allow), obj.strong === undefined ? sub : clean(obj.strong));
  cache = { file, mtime: stat.mtimeMs, lists };
  return lists;
}

const hasWhole = (re: RegExp | null, text: string): boolean => {
  if (!re) return false;
  re.lastIndex = 0;
  return re.test(text);
};

const countWhole = (re: RegExp | null, text: string): number => (re ? (text.match(re) ?? []).length : 0);

export function containsBannedWord(text: string): boolean {
  const l = load();
  const t = blankAllowed(l, norm(text));
  return l.sub.some((w) => t.includes(w)) || hasWhole(l.wholeRe, t);
}

/**
 * 이름 판정(reservedNames): 부분 일치 + 이름 전용, 단어 단위 목록은 이름 전체가 그 말일 때만.
 * 앞뒤 붙임까지 막으면 다른 언어 단어가 걸린다(실측: rattrape -> rape, Kanal -> anal, Fische -> ische).
 */
export function nameHasBannedWord(name: string, includeWhole = true): boolean {
  const l = load();
  const t = blankAllowed(l, norm(name));
  if (l.sub.some((w) => t.includes(w)) || l.namesOnly.some((w) => t.includes(w))) return true;
  return includeWhole && l.whole.includes(t);
}

/**
 * 채팅용 가림. NFKC 소문자 사본에서 금칙어를 찾아 원문의 같은 구간(코드포인트 단위)을 '*'로 바꾼다. 길이는 유지된다.
 * 공백·기호로 숨긴 변형은 여기서 잡히지 않는다(hasObfuscatedBannedWord).
 */
export function maskBannedWords(text: string): { text: string; hit: boolean } {
  const l = load();
  if (l.sub.length === 0 && !l.wholeRe) return { text, hit: false };
  const original = Array.from(text);
  let folded = '';
  const owner: number[] = []; // 정규화 문자열의 각 UTF-16 위치 -> 원문 코드포인트 번호
  original.forEach((ch, i) => {
    const n = norm(ch);
    for (let k = 0; k < n.length; k++) owner.push(i);
    folded += n;
  });
  folded = blankAllowed(l, folded);
  const masked = new Set<number>();
  const mark = (at: number, len: number): void => {
    for (let k = at; k < at + len; k++) masked.add(owner[k] as number);
  };
  for (const w of l.sub) {
    for (let at = folded.indexOf(w); at >= 0; at = folded.indexOf(w, at + 1)) mark(at, w.length);
  }
  if (l.wholeRe) for (const m of folded.matchAll(l.wholeRe)) mark(m.index ?? 0, m[0].length);
  if (masked.size === 0) return { text, hit: false };
  return { text: original.map((ch, i) => (masked.has(i) ? '*' : ch)).join(''), hit: true };
}

const countOf = (hay: string, needle: string): number => {
  let n = 0;
  for (let at = hay.indexOf(needle); at >= 0; at = hay.indexOf(needle, at + 1)) n++;
  return n;
};

/** "f u c k", "시 발"처럼 한 글자씩 띄운 덩어리만 붙인다. "다시 발견"처럼 여러 글자 단어는 붙이지 않는다. */
const joinSpacedLetters = (s: string): string =>
  s.replace(/(?<![\p{L}\p{N}])\p{L}(?:\s+\p{L}(?![\p{L}\p{N}]))+/gu, (run) => run.replace(/\s+/gu, ''));
/** 단어 안에 끼운 기호와 한글 사이에 끼운 숫자를 지운다("씨.발", "f.u.c.k", "시1발"). 띄어쓰기는 남긴다. */
const stripInWord = (s: string): string =>
  s.replace(/['’]/gu, ' ').replace(/[^\p{L}\p{N}\s\u0000]/gu, '').replace(/(?<=[\uac00-\ud7a3])\p{N}+(?=[\uac00-\ud7a3])/gu, ''); // "시1발": 한글 사이 숫자도

/**
 * 숨기려는 변형이면 true(채팅은 거절, 이름은 금지). 원문 그대로의 금칙어를 섞어 빠져나가지 못하게 횟수로 비교한다.
 * - 단어 안에 기호를 끼웠다("씨.발", "f.u.c.k").
 * - 한 글자씩 띄웠다("시 발", "f u c k"). "다시 발견"처럼 여러 글자 단어 사이의 띄어쓰기는 붙이지 않는다.
 * - 풀어 쓴 자모를 음절로 묶으면 한글 금칙어가 나온다("ㅅㅣㅂㅏㄹ").
 * 두벌식으로 친 영문("tlqkf")은 목록 자체에 들어 있다(build_banned_words.py가 만든다).
 */
export function hasObfuscatedBannedWord(text: string): boolean {
  const l = load();
  const plain = blankAllowed(l, norm(text));
  // 아포스트로피는 띄어쓰기로 본다("t'ai"를 "tai"로 붙이지 않게)
  const base = plain.replace(/['’]/gu, ' ');
  const inWord = stripInWord(plain);
  if (l.sub.some((w) => countOf(inWord, w) > countOf(base, w))) return true;
  if (l.wholeRe && countWhole(l.wholeRe, inWord) > countWhole(l.wholeRe, base)) return true;
  // 한 글자씩 띄운 것은 강한 욕만 본다
  const spaced = joinSpacedLetters(inWord);
  if (l.strong.some((w) => countOf(spaced, w) > countOf(inWord, w))) return true;
  if (l.hangul.length === 0) return false;
  const jamo = blankAllowed(l, composeLooseJamo(text.normalize('NFC')).toLowerCase());
  return l.hangul.some((w) => countOf(jamo, w) > countOf(plain, w));
}

/** 진단용(운영 점검·오탐 테스트): 글에서 걸린 금칙어와 그 종류. 우회 판정은 'obfuscated' 하나로 표시한다. */
export function explainBanned(text: string): string[] {
  const l = load();
  const t = blankAllowed(l, norm(text));
  const hits = l.sub.filter((w) => t.includes(w)).map((w) => `sub:${w}`);
  if (l.wholeRe) for (const m of t.matchAll(l.wholeRe)) hits.push(`whole:${m[0]}`);
  if (hasObfuscatedBannedWord(text)) hits.push('obfuscated');
  return hits;
}
