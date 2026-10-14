// 정책 문서를 정적 웹사이트(GitHub Pages)로 내보낸다. 서버가 꺼져도 스토어·게임에 적은 주소가 열리게 하는 공개 사본이다.
//   npx tsx scripts/export-policy-site.ts <출력 폴더>
// 페이지는 서버의 GET /policies/:lang/:doc와 같은 함수로 만든다(확률 페이지는 내보낸 시점의 확률표).
// 확률표나 문서를 바꾸면 다시 내보내 올린다(Docs/OPS_ADMIN_GUIDE.md 7절).
import fs from 'node:fs';
import path from 'node:path';
import { initConfig } from '../src/config/env';
import { policyPageHtml } from '../src/domains/policies/policyPages';
import { DOCS, LANGUAGES } from '../src/domains/policies/policyVersions';

const out = process.argv[2];
if (!out) throw new Error('사용법: npx tsx scripts/export-policy-site.ts <출력 폴더>');
const envFile = path.resolve(__dirname, '..', '.env');
if (fs.existsSync(envFile)) process.loadEnvFile(envFile);
initConfig();

const TITLES: Record<string, Record<string, string>> = {
  ko: { terms: '이용약관', privacy: '개인정보처리방침', operation: '운영정책', paid: '유료 서비스 이용 정책', youth: '청소년 보호 정책', 'account-deletion': '계정 삭제 안내', probability: '확률형 아이템 정보 공개' },
  en: { terms: 'Terms of Service', privacy: 'Privacy Policy', operation: 'Operation Policy', paid: 'Paid Service Policy', youth: 'Youth Protection Policy', 'account-deletion': 'Deleting Your Account', probability: 'Probability Disclosure' },
};

for (const lang of LANGUAGES) {
  fs.mkdirSync(path.join(out, lang), { recursive: true });
  for (const doc of DOCS) {
    const html = policyPageHtml(lang, doc);
    if (!html) throw new Error(`${lang}/${doc} 문서가 없습니다`);
    // 서버 경로 링크(/policies/ko/probability)를 정적 사이트의 상대 경로로 바꾼다
    const local = html.replace(/href="\/policies\/(ko|en)\/([a-z-]+)"/g, (_m, l: string, d: string) => `href="../${l}/${d}.html"`);
    fs.writeFileSync(path.join(out, lang, `${doc}.html`), local);
  }
}

const list = (lang: string): string =>
  DOCS.map((d) => `<li><a href="${lang}/${d}.html">${TITLES[lang]?.[d] ?? d}</a></li>`).join('');
const index =
  '<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' +
  '<title>dotRPG 정책 / Policies</title><style>body{font-family:system-ui,sans-serif;max-width:720px;margin:40px auto;padding:0 16px;line-height:1.7;color:#1d1f24;background:#fff}' +
  'a{color:#2457c5}h2{margin-top:32px}</style></head><body><h1>dotRPG</h1>' +
  `<h2>한국어</h2><ul>${list('ko')}</ul><h2>English</h2><ul>${list('en')}</ul></body></html>`;
fs.writeFileSync(path.join(out, 'index.html'), index);
fs.writeFileSync(path.join(out, '.nojekyll'), '');
console.log(`${LANGUAGES.length * DOCS.length + 1} pages -> ${out}`);
