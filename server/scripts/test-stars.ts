// 시험 서버 전용: 캐릭터의 계정에 별조각을 넣는다(별조각 원장에 test_grant로 남긴다). 실제 결제 연동 전 시험용.
// 사용: npx tsx scripts/test-stars.ts <캐릭터 이름> <개수>
// DEPLOY_STAGE=test 가 명시된 환경(server/.env 포함)이 아니면 실행을 거절한다. 게임에서는 캐시샵 창을 다시 열면 보인다.
import { initConfig } from '../src/config/env';
import { closePool, getPool, withTransaction } from '../src/db/pool';
import { changeBalance, lockWallet } from '../src/domains/starshop/starshopRepository';

import { assertTestStage } from './_stageGuard.mjs';

(async () => {
  // 시험 서버 전용 가드: DEPLOY_STAGE=test 가 명시된 환경에서만 실행한다(맨 앞에서 확인)
  assertTestStage();
  if (!process.env.DATABASE_URL) process.loadEnvFile(new URL('../.env', import.meta.url));
  const [name, amountText] = process.argv.slice(2);
  const amount = Number(amountText);
  if (!name || !Number.isInteger(amount) || amount <= 0) throw new Error('사용: npx tsx scripts/test-stars.ts <캐릭터 이름> <개수>');
  initConfig();
  const r = await getPool().query<{ account_id: string }>('SELECT account_id FROM characters WHERE name = $1 AND deleted_at IS NULL', [name]);
  const accountId = Number(r.rows[0]?.account_id ?? 0);
  if (!accountId) throw new Error(`캐릭터 '${name}'을(를) 찾지 못했습니다.`);
  const after = await withTransaction(async (db) => {
    await lockWallet(db, accountId);
    return changeBalance(db, accountId, amount, 'test_grant', 'scripts/test-stars', null);
  });
  console.log(`${name}: 별조각 +${amount} (잔액 ${after})`);
  await closePool();
})().catch(async (e) => {
  console.error(e instanceof Error ? e.message : e);
  await closePool().catch(() => undefined);
  process.exit(1);
});
