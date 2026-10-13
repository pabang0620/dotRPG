// 이름 판정 실측(DB 없이): 게임 속 짧은 이름·지명(모든 언어 번역)이 이름 필터에 걸리는지, 걸려야 할 이름이 걸리는지.
//   cd server && npm run build && node ../Tools/moderation/names_check.js
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..', '..');
process.chdir(path.join(ROOT, 'server'));
process.loadEnvFile('.env');
// WSL에서 /mnt/c 파일 확인(stat)이 느려 이름 하나에 20ms가 걸린다: 데이터를 리눅스 임시 폴더로 복사해 쓴다(운영 서버는 0.3ms).
const os = require('os');
const tmpData = fs.mkdtempSync(path.join(os.tmpdir(), 'names-data-'));
fs.cpSync(path.join(ROOT, 'server/data'), tmpData, { recursive: true });
process.env.GAME_DATA_DIR = tmpData;
require(path.join(ROOT, 'server/dist/config/env')).initConfig();
const { forbiddenReason } = require(path.join(ROOT, 'server/dist/domains/antiabuse/reservedNames'));
const LANG = path.join(ROOT, 'Assets/Resources/Data/Lang');
const names = new Set();
for (const lang of fs.readdirSync(LANG)) {
  const dir = path.join(LANG, lang);
  if (!fs.statSync(dir).isDirectory()) continue;
  for (const f of fs.readdirSync(dir).filter((x) => x.endsWith('.json'))) {
    for (const [ko, tr] of Object.entries(JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')))) {
      for (const s of [ko, tr]) for (const part of String(s).split(/\s+/)) if (/^[\p{L}\p{N}]{2,8}$/u.test(part)) names.add(part);
    }
  }
}
const hits = [...names].map((n) => [n, forbiddenReason(n)]).filter(([, r]) => r);
console.log(`${names.size} in-game words checked as names, ${hits.length} refused`);
for (const [n, r] of hits.slice(0, 80)) console.log(r, n);
const must = ['GM', 'gm01', '지엠', '운영자', '운영진', '어드민', 'Admin', 'adm1n', 'GameMaster', '運営', '管理员', '客服', '官方', 'администратор', 'Модератор', 'Админ', 'Moderador', 'QuảnTrị', '시발', 'tlqkf', 'Fuck', 'FuckYou', 'Asshole', 'Nigger', 'Сука', 'Хуйло', '傻逼', '死ね', 'Puta', 'Kontol'];
const missed = must.filter((n) => !forbiddenReason(n));
console.log(`must refuse: ${must.length - missed.length}/${must.length}` + (missed.length ? ' missed: ' + missed.join(' ') : ''));
