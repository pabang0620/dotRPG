// 시험 서버 전용: 로그인 아이디 계정의 모든 캐릭터에게 봉인 열쇠 조각을 우편(운영자 지급)으로 보낸다.
// 사용: node scripts/test-mail-raid-keys.mjs <개수> <로그인 아이디> [...]
// 시험 서버: cd /opt/dotrpg/app && sudo bash -c 'set -a; . /opt/dotrpg/.env; set +a; node scripts/test-mail-raid-keys.mjs 300 admin darac'
import { randomUUID } from 'node:crypto';
import { assertTestStage } from './_stageGuard.mjs';
import { initConfig } from '../dist/config/env.js';
import { closePool, getPool, withTransaction } from '../dist/db/pool.js';
import { initGameData } from '../dist/gamedata/loader.js';
import { createSystemMail } from '../dist/domains/mail/mailRepository.js';

assertTestStage();
const [countText, ...logins] = process.argv.slice(2);
const count = Number(countText);
if (!Number.isInteger(count) || count <= 0 || logins.length === 0) throw new Error('사용: node scripts/test-mail-raid-keys.mjs <개수> <로그인 아이디> ...');
const cfg = initConfig();
initGameData(cfg.gameDataDir);
const pool = getPool();
for (const login of logins) {
  const acc = await pool.query("SELECT account_id FROM auth_identities WHERE provider = 'dev' AND subject = $1", [login]);
  if (acc.rows.length !== 1) throw new Error(`로그인 아이디 '${login}'을(를) 찾지 못했습니다.`);
  const chars = await pool.query('SELECT id, name FROM characters WHERE account_id = $1 AND deleted_at IS NULL ORDER BY id', [acc.rows[0].account_id]);
  for (const c of chars.rows) {
    const now = new Date();
    await withTransaction(async (client) => {
      await createSystemMail(client, {
        characterId: Number(c.id), systemCode: 'event', gold: 0,
        item: { itemKey: 'key_seal', count, bind: 'none' },
        now, expiresAt: new Date(now.getTime() + 30 * 24 * 3600 * 1000), requestId: randomUUID(),
      });
    });
    console.log(`${login}/${c.name}: 봉인 열쇠 조각 ${count}개 우편`);
  }
}
await closePool();
