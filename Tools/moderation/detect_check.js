// 금칙어 탐지 실측: 반드시 걸려야 하는 표현(우회형 포함)을 채팅·이름 판정에 넣고 놓친 것을 보인다. server에서 npm run build 먼저.
//   node Tools/moderation/detect_check.js
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..', '..');
const bw = require(path.join(ROOT, 'server/dist/utils/bannedWords'));
const list = JSON.parse(fs.readFileSync(path.join(ROOT, 'server/data/banned_words.json'), 'utf8'));
bw.setBannedWords(list.words, list.words_whole || [], list.names_only || [], list.allow || [], list.strong);
const MUST = JSON.parse(fs.readFileSync(path.join(__dirname, 'must_catch.json'), 'utf8'));
let miss = 0, total = 0;
for (const [lang, items] of Object.entries(MUST)) {
  if (lang.startsWith('_')) continue; // _judged_not_banned: 판단상 넣지 않은 말과 이유
  const missed = [];
  for (const s of items) {
    total++;
    const chat = bw.maskBannedWords(s).hit || bw.hasObfuscatedBannedWord(s);
    if (!chat) missed.push(s);
  }
  miss += missed.length;
  console.log(`${lang}: ${items.length - missed.length}/${items.length}` + (missed.length ? `  missed: ${missed.join(' | ')}` : ''));
}
console.log(`total missed ${miss}/${total}`);
