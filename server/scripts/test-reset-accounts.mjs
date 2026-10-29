// 시험 서버 전용: 개발 로그인 계정의 캐릭터를 모두 지우고 1레벨 전사·마법사·도적을 새로 만든다. 물약과 별조각도 넣는다.
// 사용: node scripts/test-reset-accounts.mjs <로그인 아이디>:<접두어> ... (예: admin:어드 darac:다락)
// 시험 서버: cd /opt/dotrpg/app && sudo bash -c 'set -a; . /opt/dotrpg/.env; node scripts/test-reset-accounts.mjs admin:어드 darac:다락'
import { randomUUID } from 'node:crypto';
import { assertTestStage } from './_stageGuard.mjs';
import { initConfig } from '../dist/config/env.js';
import { closePool, getPool, withTransaction } from '../dist/db/pool.js';
import { initGameData } from '../dist/gamedata/loader.js';
import { runEconomy } from '../dist/domains/economy/economyService.js';
import { createCharacter, deleteCharacter } from '../dist/domains/characters/characterService.js';
import { creditFree, lockWallet } from '../dist/domains/starshop/starWallet.js';

assertTestStage();
const CLASSES = [['warrior', '전사'], ['mage', '법사'], ['rogue', '도적']];
const POTIONS = [['potion_hp', 500], ['potion_hp_hi', 500], ['potion_mp', 500], ['potion_mp_hi', 500]];
const STARS = 2000000;

const cfg = initConfig();
initGameData(cfg.gameDataDir);
const pool = getPool();

for (const arg of process.argv.slice(2)) {
  const [login, prefix] = arg.split(':');
  const acc = await pool.query("SELECT account_id FROM auth_identities WHERE provider = 'dev' AND subject = $1", [login]);
  if (acc.rows.length !== 1) throw new Error(`로그인 아이디 '${login}'을(를) 찾지 못했습니다.`);
  const accountId = Number(acc.rows[0].account_id);

  const old = await pool.query('SELECT uuid, name FROM characters WHERE account_id = $1 AND deleted_at IS NULL', [accountId]);
  for (const c of old.rows) {
    try {
      await deleteCharacter(accountId, c.uuid);
    } catch (e) {
      // 진행 중 경매·미수령 우편이 있어도 시험 계정이라 그대로 지운다
      await pool.query('UPDATE characters SET deleted_at = now() WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL', [c.uuid, accountId]);
      console.log(`  강제 삭제: ${c.name} (${e.message})`);
    }
  }
  console.log(`${login}: 기존 캐릭터 ${old.rows.length}개 삭제`);

  for (const [cls, label] of CLASSES) {
    const name = `${prefix}${label}`;
    const res = await createCharacter(accountId, { request_id: randomUUID(), name, class: cls });
    const uuid = res.body.data.character.id;
    await runEconomy({
      accountId, characterUuid: uuid, endpoint: 'SCRIPT test-reset-accounts', requestId: randomUUID(), payload: { name },
      handler: async (ctx) => {
        for (const [key, n] of POTIONS) await ctx.addItem('bag', key, n, 'test_boost', 'scripts/test-reset-accounts');
        return { status: 200, data: { delta: ctx.delta() } };
      },
    });
    console.log(`  생성: ${name} (${cls}) Lv1, 물약 ${POTIONS.length}종`);
  }

  const after = await withTransaction(async (db) => {
    await db.query("SET LOCAL dotrpg.allow_test_grant = 'on'");
    await lockWallet(db, accountId);
    return (await creditFree(db, { accountId, reason: 'test_grant', amount: STARS, ref: 'scripts/test-reset-accounts', requestId: null })).balance;
  });
  console.log(`  별조각 +${STARS} (잔액 ${after})`);
}
await closePool();
