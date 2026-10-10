// scripts/test-checkpoint.mjs: 구간 시작 캐릭터가 서버 검사(선행·레벨·누적 처치)를 그대로 통과하는 상태로 만들어지는지.
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { buildApp, registerAccount, resetDb, shutdown } from './helpers';
import { expectLedgerConsistent, get, post, type Hero } from './economyHelpers';

let app: Express;
beforeEach(async () => {
  await resetDb();
  app = buildApp();
});
afterAll(async () => {
  await shutdown();
});

const SCRIPT = path.join(__dirname, '..', 'scripts', 'test-checkpoint.mjs');

async function checkpoint(cp: string, career = 1): Promise<Hero> {
  const s = await registerAccount(app);
  const name = `cp${String(Date.now() % 1000000).padStart(6, '0')}`;
  execFileSync('node', [SCRIPT, s.loginId, name, cp, String(career)], { env: { ...process.env, DEPLOY_STAGE: 'test' }, stdio: 'pipe' });
  const r = await getPool().query<{ id: string; uuid: string; class: 'warrior' | 'mage' }>('SELECT id, uuid, class FROM characters WHERE name = $1', [name]);
  const row = r.rows[0] as { id: string; uuid: string; class: 'warrior' | 'mage' };
  return { s, id: row.uuid, dbId: Number(row.id), cls: row.class };
}

const addKills = (h: Hero, monster: string, n: number) =>
  getPool().query('UPDATE kill_stats SET kills = kills + $3 WHERE character_id = $1 AND monster_id = $2', [h.dbId, monster, n]);

describe('test-checkpoint', () => {
  test('winter: Lv25, 2-7까지 완료, 다음 메인은 새 처치만 채우면 받는다', async () => {
    const h = await checkpoint('winter');
    const detail = await request(h);
    expect(detail.level).toBe(25);
    const early = await post(app, h, '/quests/w_edge/claim', {});
    expect(early.status).toBe(422);
    expect(early.body.errors.code).toBe('QUEST_LEVEL'); // w_edge는 Lv27부터
    await getPool().query('UPDATE characters SET level = 27 WHERE id = $1', [h.dbId]);
    const notDone = await post(app, h, '/quests/w_edge/claim', {});
    expect(notDone.status).toBe(422);
    expect(notDone.body.errors.code).toBe('QUEST_NOT_DONE');
    await addKills(h, 'skel_warrior', 50);
    await addKills(h, 'skel_shield', 30);
    const ok = await post(app, h, '/quests/w_edge/claim', {});
    expect(ok.status).toBe(200);
    const dailies = await get(app, h, '/dailies');
    expect(dailies.body.data.unlocked).toBe(true);
    await expectLedgerConsistent(h);
  });

  test('sanctum: 마법사 전직, Lv37, 성소 회랑 직전', async () => {
    const h = await checkpoint('sanctum', 3);
    expect(h.cls).toBe('mage');
    const detail = await request(h);
    expect(detail.level).toBe(37);
    const r = await post(app, h, '/quests/w_reach/claim', {});
    expect(r.status).toBe(409); // 이미 완료
    await getPool().query('UPDATE characters SET level = 38 WHERE id = $1', [h.dbId]);
    const hall = await post(app, h, '/quests/s_hall/claim', {});
    expect(hall.body.errors.code).toBe('QUEST_NOT_DONE');
    await expectLedgerConsistent(h);
  });
});

async function request(h: Hero): Promise<{ level: number }> {
  const r = await get(app, h, '');
  expect(r.status).toBe(200);
  return r.body.data.character as { level: number };
}
