// 시험 서버 전용: 캐릭터의 오늘 던전 입장 횟수를 되돌린다(오늘 판의 counts_entry를 false로).
// 사용: node scripts/test-reset-entries.mjs <캐릭터 이름> [<캐릭터 이름> ...]   (서버 .env의 DATABASE_URL 사용)
// 판 기록과 보상은 그대로 두고 횟수 계산에서만 뺀다. 운영 서버(DEPLOY_STAGE=live)에서는 실행을 거절한다.
import { readFileSync } from 'node:fs';
import pg from 'pg';

const env = Object.fromEntries(
  readFileSync(new URL('../.env', import.meta.url), 'utf8')
    .split(/\r?\n/)
    .filter((l) => /^[A-Z0-9_]+=/.test(l))
    .map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]),
);
if ((process.env.DEPLOY_STAGE ?? env.DEPLOY_STAGE ?? 'dev') === 'live') {
  console.error('운영 서버(DEPLOY_STAGE=live)에서는 쓸 수 없습니다.');
  process.exit(1);
}
const names = process.argv.slice(2);
if (names.length === 0) {
  console.error('사용: node scripts/test-reset-entries.mjs <캐릭터 이름> ...');
  process.exit(1);
}
const client = new pg.Client({ connectionString: process.env.DATABASE_URL ?? env.DATABASE_URL });
await client.connect();
try {
  for (const name of names) {
    // 오늘 = 가장 최근 판의 reset_day(서버가 06:00 KST 기준으로 넣은 값)
    const r = await client.query(
      `UPDATE dungeon_runs d SET counts_entry = false
         FROM characters c
        WHERE c.id = d.character_id AND c.name = $1 AND c.deleted_at IS NULL AND d.counts_entry
          AND d.reset_day = (SELECT max(reset_day) FROM dungeon_runs WHERE character_id = c.id)
      RETURNING d.id`,
      [name],
    );
    console.log(`${name}: 오늘 입장 ${r.rowCount}회를 되돌렸습니다.`);
  }
} finally {
  await client.end();
}
