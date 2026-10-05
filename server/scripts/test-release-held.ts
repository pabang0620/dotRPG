// 시험 서버 전용: 보류(held)된 캐릭터의 던전 판을 확정해 보상 카드를 받을 수 있게 한다(운영 해제와 같은 계산).
// 사용: npx tsx scripts/test-release-held.ts <캐릭터 이름>
// 운영 서버(DEPLOY_STAGE=live)에서는 실행을 거절한다. 운영은 관리자 held release를 쓴다.
import { randomUUID } from 'node:crypto';
import { initConfig } from '../src/config/env';
import { closePool, getPool } from '../src/db/pool';
import { initGameData } from '../src/gamedata/loader';
import { finalizeCleared, minClearSeconds, runContext } from '../src/domains/dungeons/dungeonResult';
import * as dungeonRepo from '../src/domains/dungeons/dungeonRepository';
import { runEconomy } from '../src/domains/economy/economyService';

(async () => {
  if (!process.env.DATABASE_URL) process.loadEnvFile(new URL('../.env', import.meta.url));
  if ((process.env.DEPLOY_STAGE ?? 'dev') === 'live') throw new Error('운영 서버(DEPLOY_STAGE=live)에서는 쓸 수 없습니다.');
  const [name] = process.argv.slice(2);
  if (!name) throw new Error('사용: npx tsx scripts/test-release-held.ts <캐릭터 이름>');
  const cfg = initConfig();
  initGameData(cfg.gameDataDir);
  const c = (await getPool().query<{ id: string; account_id: string; uuid: string }>(
    'SELECT id, account_id, uuid FROM characters WHERE name = $1 AND deleted_at IS NULL', [name])).rows[0];
  if (!c) throw new Error(`캐릭터 '${name}'을(를) 찾지 못했습니다.`);
  const held = (await getPool().query<{ id: string; uuid: string }>(
    "SELECT id, uuid FROM dungeon_runs WHERE character_id = $1 AND state = 'held' ORDER BY id", [c.id])).rows;
  for (const h of held) {
    const res = await runEconomy({
      accountId: Number(c.account_id),
      characterUuid: c.uuid,
      endpoint: 'SCRIPT test-release-held',
      requestId: randomUUID(),
      payload: { run: h.uuid },
      handler: async (ctx) => {
        const run = (await dungeonRepo.findRunById(ctx.client, Number(h.id))) as dungeonRepo.RunRow;
        if (run.state !== 'held') return { status: 200, data: { skipped: true } };
        const stats = (run.stats ?? {}) as { elapsed_ms?: number; hits_taken?: number; max_combo?: number; revives_used?: number };
        const { eco, diff } = runContext(run);
        const elapsedS = (stats.elapsed_ms ?? 0) / 1000;
        return finalizeCleared(ctx, run, {
          hitsTaken: stats.hits_taken ?? 0,
          maxCombo: Math.max(0, Math.min(stats.max_combo ?? 0, Math.floor(elapsedS / eco.player.attackCooldown))),
          revivesUsed: Math.max(0, Math.min(stats.revives_used ?? 0, diff.revives)),
          scoredSeconds: Math.max(elapsedS, minClearSeconds(run)),
          stats: stats as Record<string, unknown>,
          at: ctx.now,
        });
      },
    });
    const d = (res.body as { data?: { rank?: string; card_count?: number; granted_xp?: number } }).data;
    console.log(`${name} 판 ${h.uuid.slice(0, 8)}: 랭크 ${d?.rank}, 경험치 ${d?.granted_xp}, 카드 ${d?.card_count}장`);
  }
  if (held.length === 0) console.log(`${name}: 보류된 판이 없습니다.`);
  await closePool();
})().catch(async (e) => {
  console.error(e instanceof Error ? e.message : e);
  await closePool().catch(() => undefined);
  process.exit(1);
});
