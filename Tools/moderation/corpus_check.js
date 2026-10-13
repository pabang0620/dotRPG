// 금칙어 오탐 빠른 실측(DB 없이): server/dist의 판정 코드로 게임 번역문 전체를 돌린다. 먼저 server에서 npm run build.
//   node Tools/moderation/corpus_check.js [최대 줄 수]
// 정식 테스트는 server/test/bannedWordsCorpus.test.ts(같은 기준)다.
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..', '..');
const bw = require(path.join(ROOT, 'server/dist/utils/bannedWords'));
const list = JSON.parse(fs.readFileSync(path.join(ROOT, 'server/data/banned_words.json'), 'utf8'));
bw.setBannedWords(list.words, list.words_whole || [], list.names_only || [], list.allow || [], list.strong);
const raw = new Set([...(list.words || []), ...(list.words_whole || []), ...(list.names_only || [])]);
const LANG = path.join(ROOT, 'Assets/Resources/Data/Lang');
const rows = [];
const seen = new Set();
for (const lang of fs.readdirSync(LANG)) {
  const dir = path.join(LANG, lang);
  if (!fs.statSync(dir).isDirectory()) continue;
  for (const f of fs.readdirSync(dir).filter((x) => x.endsWith('.json'))) {
    const t = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
    for (const [ko, tr] of Object.entries(t)) {
      if (raw.has(ko.toLowerCase()) || /\b(SELECT|UPDATE|INSERT)\b/.test(ko) || ko === '검은뿔,달빛마녀') continue;
      if (!seen.has(ko)) { seen.add(ko); rows.push(['ko', ko]); }
      if (tr && tr !== ko) rows.push([lang, tr]);
    }
  }
}
const t0 = Date.now();
const by = new Map();
for (const [lang, text] of rows) for (const h of bw.explainBanned(text)) {
  const k = `${lang} ${h}`;
  const r = by.get(k) || { n: 0, s: [] };
  r.n++; if (r.s.length < 2) r.s.push(text.replace(/\n/g, ' ').slice(0, 70));
  by.set(k, r);
}
const out = [...by].sort((a, b) => b[1].n - a[1].n);
console.log(`${rows.length} lines, ${out.length} hit kinds, ${Date.now() - t0} ms`);
for (const [k, r] of out.slice(0, Number(process.argv[2] || 60))) console.log(r.n, k, '|', r.s.join(' / '));
