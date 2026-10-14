// 정책 문서 페이지: server/policies/<lang>/<doc>.md + operator.json(운영 정보 자리표시)을 HTML 한 장으로 만든다.
// probability는 서버 확률표로 만든다(ratesDisclosure). 파일 수정 시각이 같으면 메모리 캐시를 쓴다.
import fs from 'node:fs';
import path from 'node:path';
import { getConfig } from '../../config/env';
import { fillPlaceholders, renderMarkdown, renderPage } from './policyRenderer';
import { LANGUAGES, type PolicyDoc, type PolicyLang } from './policyVersions';
import { buildRatesMarkdown, buildRatesPublication } from './ratesDisclosure';

let rootOverride: string | null = null;

/** 문서 폴더(server/policies). 테스트는 임시 폴더로 바꿀 수 있다(null이면 기본) */
export function setPoliciesDirForTest(dir: string | null): void {
  rootOverride = dir;
  cache.clear();
}

const policiesDir = (): string => rootOverride ?? path.resolve(__dirname, '..', '..', '..', 'policies');

const mtime = (p: string): number => {
  try {
    return fs.statSync(p).mtimeMs;
  } catch {
    return -1;
  }
};

const cache = new Map<string, { stamp: string; html: string }>();
const RATES_TTL_MS = 60_000;

function operatorValues(): Record<string, string> {
  try {
    const raw = JSON.parse(fs.readFileSync(path.join(policiesDir(), 'operator.json'), 'utf8')) as Record<string, unknown>;
    return Object.fromEntries(Object.entries(raw).filter(([, v]) => typeof v === 'string')) as Record<string, string>;
  } catch {
    return {};
  }
}

const titleOf = (md: string, doc: string): string => /^#\s+(.+)$/m.exec(md)?.[1]?.trim() ?? doc;

/** 렌더한 HTML. 그 언어 문서가 없으면 다른 언어 문서를 쓰고, 어디에도 없으면 null */
export function policyPageHtml(lang: PolicyLang, doc: PolicyDoc): string | null {
  const dir = policiesDir();
  const opPath = path.join(dir, 'operator.json');
  if (doc === 'probability') {
    const key = `${lang}/probability`;
    const hit = cache.get(key);
    const slot = String(Math.floor(Date.now() / RATES_TTL_MS));
    if (hit && hit.stamp === slot) return hit.html;
    const dataDir = getConfig().gameDataDir;
    // 확률표 지문(공개 기록과 같은 값, 앞 12자리)을 문서 맨 위 한 줄로 보여 준다
    const fp = buildRatesPublication(dataDir).fingerprint.slice(0, 12);
    const label = lang === 'ko' ? '확률표 지문' : 'Rate table fingerprint';
    const md = buildRatesMarkdown(dataDir, lang).replace(/^(# .+)$/m, (title) => `${title}\n\n${label}: ${fp}`);
    const html = renderPage(titleOf(md, doc), renderMarkdown(md), lang, 'dotRPG');
    cache.set(key, { stamp: slot, html });
    return html;
  }
  const order = [lang, ...LANGUAGES.filter((l) => l !== lang)];
  for (const l of order) {
    const file = path.join(dir, l, `${doc}.md`);
    const m = mtime(file);
    if (m < 0) continue;
    const key = `${lang}/${doc}`;
    const stamp = `${l}:${m}:${mtime(opPath)}`;
    const hit = cache.get(key);
    if (hit && hit.stamp === stamp) return hit.html;
    const values = operatorValues();
    const md = fillPlaceholders(fs.readFileSync(file, 'utf8'), values, l);
    const footer = l === 'ko' ? `시행일: ${values.effective_date || '(미정)'}` : `Effective date: ${values.effective_date || '(TBD)'}`;
    const html = renderPage(titleOf(md, doc), renderMarkdown(md), l, footer);
    cache.set(key, { stamp, html });
    return html;
  }
  return null;
}
