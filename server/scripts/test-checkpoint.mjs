// 시험 서버 전용: 구간 시작 캐릭터를 만든다(플레이 시험을 처음부터 하지 않고 구간마다 30~60분씩 하려고).
// 사용: node scripts/test-checkpoint.mjs <로그인 아이디> <새 캐릭터 이름> <구간> [전직 1~4]
//   구간: career(Lv15 전직 직후) fortress(Lv20 성채 앞) winter(Lv25 눈꽃 마을) lake(Lv31 호숫가) peak(Lv35 봉우리 길목 뒤) sanctum(Lv37 성소 입구 뒤)
//   전직: 1·2 전사, 3·4 마법사. 생략하면 1(전사).
// 넣는 것: 레벨, 그 구간까지의 메인 퀘스트 완료(서버 청구 기록 + 클라이언트 퀘스트 상태 + 이야기 플래그), 처치·던전 누적,
// 전직(스테이지 0), 그 레벨 구간의 레어 장비 한 벌(무기 +5), 물약, 골드. 경험치·아이템 원장에는 test_boost로 남긴다.
// DEPLOY_STAGE=test 가 명시된 환경(server/.env 포함)이 아니면 실행을 거절한다.
// 시험 서버: cd /opt/dotrpg/app && sudo -u dotrpg bash -c 'set -a; . /opt/dotrpg/.env; node scripts/test-checkpoint.mjs <아이디> <이름> <구간> [전직]'
import { existsSync, readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import pg from 'pg';
import { assertTestStage } from './_stageGuard.mjs';

assertTestStage();

const CHECKPOINTS = {
  career: { through: 'career_path', level: 15, map: 'canyon' },
  fortress: { through: 'c1_stronger', level: 20, map: 'village' },
  winter: { through: 'c2_north', level: 25, map: 'winter' },
  lake: { through: 'w_lake', level: 31, map: 'winter' },
  peak: { through: 'w_peakpass', level: 35, map: 'winter' },
  sanctum: { through: 'w_reach', level: 37, map: 'winter' },
};
const GEAR = {
  warrior: { weapon: 'sword', top: 'plate', bottom: 'greaves' },
  mage: { weapon: 'staff', top: 'robe', bottom: 'skirt' },
};
const TIERS = [1, 10, 15, 20, 25, 30, 35, 40];

// server/.env가 없으면(시험 서버는 /opt/dotrpg/.env를 환경변수로 읽어 실행) 환경변수만 쓴다
const ENV_FILE = new URL('../.env', import.meta.url);
const env = !existsSync(ENV_FILE) ? {} : Object.fromEntries(
  readFileSync(ENV_FILE, 'utf8')
    .split(/\r?\n/)
    .filter((l) => /^[A-Z0-9_]+=/.test(l))
    .map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]),
);
const data = (f) => JSON.parse(readFileSync(new URL(`../data/${f}`, import.meta.url), 'utf8'));

const [login, name, cpId, careerArg] = process.argv.slice(2);
const cp = CHECKPOINTS[cpId];
const career = Number(careerArg ?? 1);
if (!login || !name || !cp || !(career >= 1 && career <= 4)) {
  console.error(`사용: node scripts/test-checkpoint.mjs <로그인 아이디> <새 캐릭터 이름> <${Object.keys(CHECKPOINTS).join('|')}> [전직 1~4]`);
  process.exit(1);
}
const cls = career <= 2 ? 'warrior' : 'mage';
if (!/^[가-힣A-Za-z0-9]{2,8}$/.test(name)) {
  console.error('캐릭터 이름은 한글·영문·숫자 2~8자입니다.');
  process.exit(1);
}

// 구간 퀘스트까지의 선행 퀘스트 전부(requires를 거슬러 올라간다)
const quests = new Map(data('quest_index.json').quests.map((q) => [q.id, q]));
const done = new Set();
const walk = (id) => {
  if (done.has(id)) return;
  const q = quests.get(id);
  if (!q) throw new Error(`퀘스트 데이터에 ${id}가 없습니다.`);
  done.add(id);
  q.requires.forEach(walk);
};
walk(cp.through);

const killNeeds = new Map();
let dungeonClears = 0;
const raidClears = new Map();
const flags = new Set();
for (const id of done) {
  const q = quests.get(id);
  for (const [m, n] of Object.entries(q.objectives.killNeeds)) killNeeds.set(m, (killNeeds.get(m) ?? 0) + n);
  for (const d of q.objectives.dungeonNeeds) dungeonClears += d.count;
  for (const r of q.objectives.raidNeeds) raidClears.set(r.target, (raidClears.get(r.target) ?? 0) + r.count);
  for (const f of q.reward.setFlags) flags.add(f);
}

const tier = Math.max(...TIERS.filter((t) => t <= cp.level));
const g = GEAR[cls];
const worn = [
  [0, `eq_${g.weapon}_${tier}_r+5`],
  [1, `eq_neck_${tier}_r`],
  [2, `eq_ring_${tier}_r`],
  [4, `eq_${g.top}_${tier}_r`],
  [5, `eq_${g.bottom}_${tier}_r`],
];
const items = new Map(data('items.json').items.map((i) => [i.id, i]));
for (const [, key] of worn) if (!items.has(key.split('+')[0])) throw new Error(`items.json에 없는 장비: ${key}`);
const bindOf = (key) => items.get(key.split('+')[0])?.bind ?? 'none';
const prog = data('progression.json');
const site = data('gameconfig.json').deliverySites ?? {};

const client = new pg.Client({ connectionString: process.env.DATABASE_URL ?? env.DATABASE_URL });
await client.connect();
try {
  await client.query('BEGIN');
  const acc = await client.query("SELECT account_id FROM auth_identities WHERE provider = 'dev' AND subject = $1", [login]);
  if (acc.rows.length !== 1) throw new Error(`로그인 아이디 '${login}'을(를) 찾지 못했습니다.`);
  const accountId = acc.rows[0].account_id;
  const alive = await client.query('SELECT count(*)::int AS n FROM characters WHERE account_id = $1 AND deleted_at IS NULL', [accountId]);
  if (alive.rows[0].n >= 4) throw new Error('캐릭터 슬롯(4개)이 가득 찼습니다. 다른 아이디를 쓰거나 캐릭터를 지우세요.');
  const taken = await client.query('SELECT 1 FROM characters WHERE name = $1 AND deleted_at IS NULL', [name]);
  if (taken.rows.length > 0) throw new Error(`이미 있는 이름입니다: ${name}`);

  const ch = await client.query(
    `INSERT INTO characters (account_id, name, class, level, xp) VALUES ($1, $2, $3, $4, 0) RETURNING id`,
    [accountId, name, cls, cp.level],
  );
  const id = ch.rows[0].id;
  let xpTotal = 0;
  for (let l = 1; l < cp.level; l++) xpTotal += prog.xpToNext[l - 1];
  await client.query(
    `INSERT INTO xp_ledger (character_id, delta, level_after, xp_after, reason, ref, request_id)
     VALUES ($1, $2, $3, 0, 'test_boost', 'scripts/test-checkpoint', $4)`,
    [id, xpTotal, cp.level, randomUUID()],
  );

  // 클라이언트 퀘스트 상태: 완료(4), 마지막 단계까지 진행
  const questState = [...done].map((qid) => ({ id: qid, step: quests.get(qid).stepCount, counts: [], status: 4 }));
  const careerState = { schema: 1, career, nodes: [], training: [], refunded: 0, questStage: 0, awakened: false };
  await client.query(
    `INSERT INTO character_state (character_id, map_id, pos_x, pos_y, facing, quests, story_flags, career, version)
     VALUES ($1, $2, NULL, NULL, 0, $3, $4, $5, 1)`,
    [id, cp.map, JSON.stringify(questState), [...flags], cp.level >= 15 ? JSON.stringify(careerState) : null],
  );
  if (cp.level >= 15) {
    await client.query(`INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, $2, 0, 'admin')`, [id, career]);
  }
  for (const qid of done) {
    const q = quests.get(qid);
    const reward = { xp: 0, gold: 0, items: [], consumed: [], set_flags: q.reward.setFlags, max_health: q.reward.maxHealth };
    await client.query('INSERT INTO quest_claims (character_id, quest_id, reward) VALUES ($1, $2, $3)', [id, qid, JSON.stringify(reward)]);
  }
  // 서버 퀘스트 검사는 처치·던전 목표를 청구한 퀘스트끼리 누적해서 본다: 그만큼 이미 했다고 넣는다
  for (const [m, n] of killNeeds) {
    await client.query('INSERT INTO kill_stats (character_id, monster_id, kills) VALUES ($1, $2, $3)', [id, m, n]);
  }
  const past = new Date(Date.now() - 3 * 86400_000);
  const clears = [...Array(dungeonClears).fill('gold_vein'), ...[...raidClears].flatMap(([r, n]) => Array(n).fill(r))];
  for (const dungeon of clears) {
    await client.query(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, started_at, ended_at, rank, cards, counts_entry)
       VALUES ($1, $2, 0, 'cleared', $3, $3, $3, 0, '[]'::jsonb, false)`,
      [id, dungeon, past],
    );
  }
  for (const [siteId, s] of Object.entries(site)) {
    if (!done.has(s.questId)) continue;
    for (const it of s.items) {
      await client.query('INSERT INTO site_deliveries (character_id, site_id, item_key, delivered) VALUES ($1, $2, $3, $4)', [id, siteId, it.itemKey, it.required]);
    }
  }
  // 장비(착용)와 소모품, 골드. 원장에도 남긴다(아이템 test_boost, 골드는 원장 사유에 test_boost가 없어 starter)
  const give = async (key, count, location, slot) => {
    await client.query(`INSERT INTO character_items (character_id, item_key, count, location, slot, bind) VALUES ($1, $2, $3, $4, $5, $6)`, [id, key, count, location, slot, bindOf(key)]);
    await client.query(
      `INSERT INTO item_ledger (character_id, item_key, delta, reason, ref, location, balance_after) VALUES ($1, $2, $3, 'test_boost', 'scripts/test-checkpoint', $4, $3)`,
      [id, key, count, location],
    );
  };
  for (const [slot, key] of worn) await give(key, 1, 'worn', slot);
  for (const [key, count] of [['potion_hp', 50], ['potion_mp', 50]]) await give(key, count, 'bag', null);
  const gold = cp.level * 3000;
  await client.query('UPDATE characters SET gold = $2 WHERE id = $1', [id, gold]);
  await client.query(`INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref) VALUES ($1, $2, $2, 'starter', 'scripts/test-checkpoint')`, [id, gold]);
  await client.query('COMMIT');
  console.log(`${login}/${name}: ${cpId} Lv${cp.level} ${cls} 전직${career}, 완료 퀘스트 ${done.size}개(${cp.through}까지), 장비 Lv${tier} 레어, 골드 ${gold}, 시작 맵 ${cp.map}`);
} catch (e) {
  await client.query('ROLLBACK');
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await client.end();
}
