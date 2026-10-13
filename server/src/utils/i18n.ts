import fs from 'fs';
import path from 'path';
import type { Response } from 'express';
import { getConfig } from '../config/env';

/**
 * [I18N] 게임 클라이언트가 X-Lang: en을 보내면 사용자에게 보이는 메시지를 영어로 바꾼다(Docs/PLAN_I18N.md).
 * 표는 data/lang/en.json { "한국어 원문": "English" }. 호출부의 한국어 메시지는 그대로 두고 응답 직전에만 바꾼다.
 * 숫자가 들어간 문장("레벨 30부터 ...")은 숫자를 {0}, {1}로 바꾼 키로 한 번 더 찾는다. 표에 없으면 한국어 그대로.
 */
let table: Map<string, string> | null = null;

function load(): Map<string, string> {
  if (table) return table;
  table = new Map();
  try {
    const file = path.join(getConfig().gameDataDir, 'lang', 'en.json');
    const raw = JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>;
    for (const [ko, en] of Object.entries(raw)) if (typeof en === 'string' && en) table.set(ko, en);
  } catch {
    // 표가 없으면 한국어로 응답한다.
  }
  return table;
}

const NUMBER = /\d+(?:[.,]\d+)*/g;

export function translate(lang: string | undefined, message: string): string {
  if (lang !== 'en' || !message) return message;
  const t = load();
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
  return typeof h === 'string' ? h.toLowerCase() : undefined;
}

/** 테스트용: 표를 직접 넣거나(null이면 다음 호출에서 파일을 다시 읽는다). */
export function setTranslationsForTest(entries: Record<string, string> | null): void {
  table = entries ? new Map(Object.entries(entries)) : null;
}
