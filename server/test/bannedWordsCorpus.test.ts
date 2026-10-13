// 금칙어 오탐 실측: 게임의 모든 언어 번역문(대사·UI·아이템·퀘스트)은 채팅 필터에 걸리면 안 되고,
// 게임 속 이름(짧은 이름·지명)은 이름 필터의 욕설 판정(BANNED)에 걸리면 안 된다.
// 걸리면 어떤 금칙어가 어떤 문장을 막았는지 보고서를 남긴다(os.tmpdir()/banned_corpus_report.json).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { explainBanned, hasObfuscatedBannedWord, maskBannedWords, nameHasBannedWord, setBannedWords } from '../src/utils/bannedWords';
import { buildApp, shutdown } from './helpers';

const LANG_ROOT = path.resolve(__dirname, '..', '..', 'Assets', 'Resources', 'Data', 'Lang');
const RAW_LIST = path.resolve(__dirname, '..', 'data', 'banned_words.json');
const MUST_CATCH = path.resolve(__dirname, '..', '..', 'Tools', 'moderation', 'must_catch.json');

interface Entry { lang: string; text: string }

/** 번역표 전체(키 = 한국어 원문, 값 = 각 언어). 금칙어 목록 자체·SQL처럼 화면에 안 나오는 원문은 뺀다. */
function corpus(): Entry[] {
  const banned = JSON.parse(fs.readFileSync(RAW_LIST, 'utf8')) as Record<string, unknown>;
  const raw = new Set<string>(
    ['words', 'words_whole', 'names_only', 'strong'].flatMap((k) => (Array.isArray(banned[k]) ? (banned[k] as string[]) : [])),
  );
  const skip = (ko: string): boolean => raw.has(ko) || /\b(SELECT|UPDATE|INSERT)\b/.test(ko) || ko === '검은뿔,달빛마녀';
  const out: Entry[] = [];
  const seenKo = new Set<string>();
  if (!fs.existsSync(LANG_ROOT)) return out;
  for (const lang of fs.readdirSync(LANG_ROOT)) {
    const dir = path.join(LANG_ROOT, lang);
    if (!fs.statSync(dir).isDirectory()) continue;
    for (const f of fs.readdirSync(dir).filter((x) => x.endsWith('.json'))) {
      const table = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')) as Record<string, string>;
      for (const [ko, tr] of Object.entries(table)) {
        if (skip(ko)) continue;
        if (!seenKo.has(ko)) {
          seenKo.add(ko);
          out.push({ lang: 'ko', text: ko });
        }
        if (tr && tr !== ko) out.push({ lang, text: tr });
      }
    }
  }
  return out;
}

const isNameLike = (s: string): boolean => /^[\p{L}\p{N}]{2,8}$/u.test(s);

buildApp();
beforeAll(() => {
  // 파일을 한 번만 읽어 고정한다(문장마다 파일 확인을 하면 WSL의 /mnt/c에서 10분 넘게 걸렸다). 판정은 운영과 같은 함수다.
  const l = JSON.parse(fs.readFileSync(RAW_LIST, 'utf8')) as Record<string, string[]>;
  setBannedWords(l.words ?? [], l.words_whole ?? [], l.names_only ?? [], l.allow ?? [], l.strong);
});
afterAll(async () => {
  setBannedWords(null);
  await shutdown();
});

describe('[금칙어] 게임 번역문 오탐 실측', () => {
  const entries = corpus();

  it('번역문이 충분히 있다(측정 대상 확인)', () => {
    expect(entries.length).toBeGreaterThan(1000);
  });

  it('어떤 언어의 게임 문장도 채팅 금칙어·우회 판정에 걸리지 않는다', () => {
    const byWord = new Map<string, { count: number; samples: string[] }>();
    for (const e of entries) {
      for (const hit of explainBanned(e.text)) {
        const key = `${e.lang} ${hit}`;
        const row = byWord.get(key) ?? { count: 0, samples: [] };
        row.count++;
        if (row.samples.length < 3) row.samples.push(e.text.slice(0, 80));
        byWord.set(key, row);
      }
    }
    const report = [...byWord.entries()].sort((a, b) => b[1].count - a[1].count).map(([k, v]) => ({ hit: k, ...v }));
    fs.writeFileSync(path.join(os.tmpdir(), 'banned_corpus_report.json'), JSON.stringify(report, null, 1));
    expect(report.slice(0, 40)).toEqual([]);
  });

  it('반드시 걸러야 하는 표현(우회형 포함, Tools/moderation/must_catch.json)은 모두 걸린다', () => {
    const must = JSON.parse(fs.readFileSync(MUST_CATCH, 'utf8')) as Record<string, unknown>;
    const missed: string[] = [];
    for (const [lang, items] of Object.entries(must)) {
      if (lang.startsWith('_') || !Array.isArray(items)) continue;
      for (const s of items as string[]) if (!maskBannedWords(s).hit && !hasObfuscatedBannedWord(s)) missed.push(`${lang}:${s}`);
    }
    expect(missed).toEqual([]);
  });

  it('게임 속 짧은 이름·지명은 이름 욕설 판정에 걸리지 않는다(금칙어 원문 자체는 제외)', () => {
    const hits = entries.filter((e) => isNameLike(e.text) && nameHasBannedWord(e.text)).map((e) => `${e.lang}:${e.text}`);
    fs.writeFileSync(path.join(os.tmpdir(), 'banned_names_report.json'), JSON.stringify(hits, null, 1));
    expect(hits.slice(0, 40)).toEqual([]);
  });
});
