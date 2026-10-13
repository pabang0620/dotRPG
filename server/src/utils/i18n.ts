import fs from 'fs';
import path from 'path';
import type { Response } from 'express';
import { getConfig } from '../config/env';

/**
 * [I18N] 게임 클라이언트가 X-Lang(en, ja, zh-Hans ...)을 보내면 사용자에게 보이는 메시지를 그 언어로 바꾼다
 * (Docs/PLAN_I18N.md). 표는 data/lang.<언어>.json { "한국어 원문": "번역" }. 호출부의 한국어 메시지는 그대로 두고
 * 응답 직전에만 바꾼다.
 * 숫자가 들어간 문장("레벨 30부터 ...")은 숫자를 {0}, {1}로 바꾼 키로 한 번 더 찾는다. 표에 없으면 한국어 그대로.
 */
/** 받는 언어 코드. 헤더 값이 파일 이름이 되므로 이 목록 밖은 한국어로 처리한다. */
export const LANGUAGES = ['en', 'ja', 'zh-Hans', 'zh-Hant', 'es', 'pt-BR', 'de', 'fr', 'ru', 'vi', 'id'] as const;

const tables = new Map<string, Map<string, string>>();

function load(lang: string): Map<string, string> {
  const cached = tables.get(lang);
  if (cached) return cached;
  const table = new Map<string, string>();
  try {
    const file = path.join(getConfig().gameDataDir, `lang.${lang}.json`);
    const raw = JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>;
    for (const [ko, tr] of Object.entries(raw)) if (typeof tr === 'string' && tr) table.set(ko, tr);
  } catch {
    // 표가 없으면 한국어로 응답한다.
  }
  tables.set(lang, table);
  return table;
}

const NUMBER = /\d+(?:[.,]\d+)*/g;

export function translate(lang: string | undefined, message: string): string {
  if (!lang || !message || !(LANGUAGES as readonly string[]).includes(lang)) return message;
  const t = load(lang);
  const exact = t.get(message);
  if (exact) return exact;
  const nums = message.match(NUMBER);
  if (!nums) return message;
  let i = 0;
  const pattern = t.get(message.replace(NUMBER, () => `{${i++}}`));
  return pattern ? pattern.replace(/\{(\d+)\}/g, (_m, n: string) => nums[Number(n)] ?? '') : message;
}

/** 응답을 보내는 요청의 언어(X-Lang 헤더). 관리자 화면 등 헤더가 없으면 한국어. */
export function langOf(res: Response): string | undefined {
  const h = res.req?.headers?.['x-lang'];
  return typeof h === 'string' ? h : undefined;
}

/** 테스트용: 영어 표를 직접 넣거나(null이면 다음 호출에서 파일을 다시 읽는다). */
export function setTranslationsForTest(entries: Record<string, string> | null): void {
  tables.clear();
  if (entries) tables.set('en', new Map(Object.entries(entries)));
}
