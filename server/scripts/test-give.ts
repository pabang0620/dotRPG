// 시험 서버 전용: 캐릭터 가방에 아이템을 넣는다(아이템 원장에 test_boost로 남긴다).
// 사용: npx tsx scripts/test-give.ts <캐릭터 이름> <item_key> <개수> [<item_key> <개수> ...]
// 운영 서버(DEPLOY_STAGE=live)에서는 실행을 거절한다. 접속 중이면 다시 접속해야 가방에 보인다.
import { randomUUID } from 'node:crypto';
import { initConfig } from '../src/config/env';
import { closePool, getPool } from '../src/db/pool';
import { initGameData } from '../src/gamedata/loader';
import { runEconomy } from '../src/domains/economy/economyService';

(async () => {
  if ((process.env.DEPLOY_STAGE ?? 'dev') === 'live') throw new Error('운영 서버(DEPLOY_STAGE=live)에서는 쓸 수 없습니다.');
  const [name, ...pairs] = process.argv.slice(2);
  if (!name || pairs.length === 0 || pairs.length % 2 !== 0) throw new Error('사용: npx tsx scripts/test-give.ts <캐릭터 이름> <item_key> <개수> ...');
  const cfg = initConfig();
  initGameData(cfg.gameDataDir);
  const r = await getPool().query<{ account_id: string; uuid: string }>(
    'SELECT account_id, uuid FROM characters WHERE name = $1 AND deleted_at IS NULL', [name]);
  const ch = r.rows[0];
  if (!ch) throw new Error(`캐릭터 '${name}'을(를) 찾지 못했습니다.`);
  const res = await runEconomy({
    accountId: Number(ch.account_id),
    characterUuid: ch.uuid,
    endpoint: 'SCRIPT test-give',
    requestId: randomUUID(),
    payload: { pairs },
    handler: async (ctx) => {
      for (let i = 0; i < pairs.length; i += 2) {
        const count = Number(pairs[i + 1]);
        if (!Number.isInteger(count) || count <= 0) throw new Error(`개수가 올바르지 않습니다: ${pairs[i + 1]}`);
        await ctx.addItem('bag', pairs[i] as string, count, 'test_boost', 'scripts/test-give');
      }
      return { status: 200, data: { delta: ctx.delta() } };
    },
  });
  console.log(`${name}: ${JSON.stringify((res.body as { data?: unknown }).data ?? res.body).slice(0, 300)}`);
  await closePool();
})().catch(async (e) => {
  console.error(e instanceof Error ? e.message : e);
  await closePool().catch(() => undefined);
  process.exit(1);
});
