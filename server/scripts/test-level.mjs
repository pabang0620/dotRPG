// 시험 서버 전용: 캐릭터를 지정 레벨까지 올린다(경험치 원장에 test_boost로 남긴다).
// 사용: node scripts/test-level.mjs <캐릭터 이름> <레벨>   (서버 .env의 DATABASE_URL 사용)
// 운영 서버(DEPLOY_STAGE=live)에서는 실행을 거절한다.
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import pg from 'pg';

const env = Object.fromEntries(
  readFileSync(new URL('../.env', import.meta.url), 'utf8')
    .split(/\r?\n/)
    .filter((l) => /^[A-Z0-9_]+=/.test(l))
    .map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]),
);
const stage = process.env.DEPLOY_STAGE ?? env.DEPLOY_STAGE ?? 'dev';
if (stage === 'live') {
  console.error('운영 서버(DEPLOY_STAGE=live)에서는 쓸 수 없습니다.');
  process.exit(1);
}
const [name, levelArg] = process.argv.slice(2);
const target = Number(levelArg);
if (!name || !Number.isInteger(target) || target < 2) {
  console.error('사용: node scripts/test-level.mjs <캐릭터 이름> <레벨>');
  process.exit(1);
}
const prog = JSON.parse(readFileSync(new URL('../data/progression.json', import.meta.url), 'utf8'));
if (target > prog.maxLevel) {
  console.error(`최대 레벨은 ${prog.maxLevel}입니다.`);
  process.exit(1);
}

const client = new pg.Client({ connectionString: process.env.DATABASE_URL ?? env.DATABASE_URL });
await client.connect();
try {
  await client.query('BEGIN');
  const r = await client.query(
    'SELECT id, level, xp FROM characters WHERE name = $1 AND deleted_at IS NULL FOR UPDATE',
    [name],
  );
  if (r.rows.length !== 1) throw new Error(`캐릭터 '${name}'을(를) 찾지 못했습니다.`);
  const ch = r.rows[0];
  if (ch.level >= target) throw new Error(`이미 Lv${ch.level}입니다.`);
  // 지금 레벨의 남은 경험치 + 그 뒤 레벨들의 필요량 = 목표 레벨 0경험치까지의 양
  let delta = prog.xpToNext[ch.level - 1] - ch.xp;
  for (let l = ch.level + 1; l < target; l++) delta += prog.xpToNext[l - 1];
  await client.query('UPDATE characters SET level = $2, xp = 0 WHERE id = $1', [ch.id, target]);
  await client.query(
    `INSERT INTO xp_ledger (character_id, delta, level_after, xp_after, reason, ref, request_id)
     VALUES ($1, $2, $3, 0, 'test_boost', 'scripts/test-level', $4)`,
    [ch.id, delta, target, randomUUID()],
  );
  await client.query('COMMIT');
  console.log(`${name}: Lv${ch.level} -> Lv${target} (+${delta} xp)`);
} catch (e) {
  await client.query('ROLLBACK');
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await client.end();
}
