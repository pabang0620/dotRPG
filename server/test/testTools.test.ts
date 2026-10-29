// 시험 도구(Docs/PLAN_TEST_TOOLS_2026-10-29.md): 메인 퀘스트 밀기·경험치 밀기. 운영(live)에서는 경로가 없고, 시험 서버에서도 개발 로그인 계정만
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { CHAPTER2_LEVEL_CAP } from '../src/gamedata/chapterGate';
import { getGameData } from '../src/gamedata/loader';
import { pushLevel } from '../src/domains/testtools/testToolsService';
import { buildApp, resetDb, shutdown } from './helpers';
import { expectLedgerConsistent, newHero, post, type Hero } from './economyHelpers';

beforeAll(resetDb);
afterAll(shutdown);

async function levelXp(h: Hero): Promise<{ level: number; xp: number }> {
  const r = await getPool().query<{ level: number; xp: number }>('SELECT level, xp FROM characters WHERE id = $1', [h.dbId]);
  return r.rows[0] as { level: number; xp: number };
}

async function claimedOf(h: Hero): Promise<string[]> {
  const r = await getPool().query<{ quest_id: string }>('SELECT quest_id FROM quest_claims WHERE character_id = $1', [h.dbId]);
  return r.rows.map((x) => x.quest_id);
}

const mainIds = (): string[] => [...getGameData().economy.quests.values()].filter((q) => q.kind === 'main').map((q) => q.id);

describe('운영 스테이지(live)', () => {
  let live: Express;
  beforeAll(() => {
    live = buildApp();
  });

  it('경로가 등록되지 않고(404) /meta의 test_tools는 false', async () => {
    const h = await newHero(live);
    expect((await post(live, h, '/test-tools/main-quest', { mode: 'one' })).status).toBe(404);
    expect((await post(live, h, '/test-tools/level', { mode: 'one' })).status).toBe(404);
    expect((await request(live).get('/meta')).body.data.test_tools).toBe(false);
    expect(await claimedOf(h)).toEqual([]);
  });

  it('서비스를 직접 불러도 403 TEST_TOOLS_DISABLED', async () => {
    const h = await newHero(live);
    const accountId = Number((await getPool().query('SELECT account_id FROM characters WHERE id = $1', [h.dbId])).rows[0].account_id);
    expect(() => pushLevel(accountId, h.id, { request_id: randomUUID(), mode: 'one' })).toThrow(
      expect.objectContaining({ status: 403, code: 'TEST_TOOLS_DISABLED' }),
    );
  });
});

describe('시험 스테이지(test)', () => {
  let app: Express;
  beforeAll(() => {
    app = buildApp({ DEPLOY_STAGE: 'test' });
  });

  it('/meta의 test_tools는 true', async () => {
    expect((await request(app).get('/meta')).body.data.test_tools).toBe(true);
  });

  it('메인 퀘스트 한 칸: 보상·청구 기록·서버 퀘스트 상태·이야기 플래그', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/test-tools/main-quest', { mode: 'one' });
    expect(res.status).toBe(200);
    expect(res.body.data.completed).toEqual(['c1_morning']);
    expect(res.body.data.quests).toEqual([{ id: 'c1_morning', step: expect.any(Number), counts: [], status: 4 }]);
    expect(res.body.data.story_flags).toContain('festival_eve');
    expect(res.body.data.delta.xp).toBe(getGameData().economy.quests.get('c1_morning')!.reward.xp);
    expect(await claimedOf(h)).toEqual(['c1_morning']);
    const st = await getPool().query('SELECT quests, story_flags FROM character_state WHERE character_id = $1', [h.dbId]);
    expect(st.rows[0].quests).toEqual(expect.arrayContaining([expect.objectContaining({ id: 'c1_morning', status: 4 })]));
    expect(st.rows[0].story_flags).toContain('festival_eve');
    await expectLedgerConsistent(h);
  });

  it('챕터 끝까지: 전직 앞에서 멈추고, 전직 뒤에는 2장 끝(c2_end)까지. 처치·던전·레이드·납품·레벨을 채운다', async () => {
    const h = await newHero(app);
    const first = await post(app, h, '/test-tools/main-quest', { mode: 'chapter' });
    expect(first.status).toBe(200);
    expect(first.body.data.stopped).toEqual({ code: 'TEST_NEEDS_CAREER', quest_id: 'career_path' });
    expect(first.body.data.completed).toContain('c2s_trader_guard');
    // 전직 전에 한 칸 더 누르면 422
    const blocked = await post(app, h, '/test-tools/main-quest', { mode: 'one' });
    expect(blocked.status).toBe(422);
    expect(blocked.body.errors.code).toBe('TEST_NEEDS_CAREER');

    await getPool().query("INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, 1, 0, 'admin')", [h.dbId]);
    const rest = await post(app, h, '/test-tools/main-quest', { mode: 'chapter' });
    expect(rest.status).toBe(200);
    expect(rest.body.data.stopped).toBeNull();
    const claimed = new Set(await claimedOf(h));
    // 3장이 닫혀 있으면 1·2장 메인 퀘스트 전부(3장 제외)
    for (const id of mainIds().filter((q) => !q.startsWith('c3_'))) expect(claimed.has(id)).toBe(true);
    expect(claimed.has('c3_depart')).toBe(false);
    expect((await levelXp(h)).level).toBe(CHAPTER2_LEVEL_CAP);
    const boost = await getPool().query("SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1 AND reason = 'test_boost'", [h.dbId]);
    expect(boost.rows[0].n).toBeGreaterThan(0);
    // 납품 퀘스트(c2s_trader_ore)의 재료는 test_boost로 넣고 청구가 소모했다
    const ore = await getPool().query("SELECT reason FROM item_ledger WHERE character_id = $1 AND item_key = 'mat_ore' ORDER BY id", [h.dbId]);
    expect(ore.rows.map((r) => r.reason)).toEqual(['test_boost', 'quest_consume']);

    const none = await post(app, h, '/test-tools/main-quest', { mode: 'one' });
    expect(none.status).toBe(409);
    expect(none.body.errors.code).toBe('TEST_NO_MAIN_QUEST');
    await expectLedgerConsistent(h);
  });

  it('경험치 한 칸: 다음 레벨 0경험치, 원장 사유 test_boost', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/test-tools/level', { mode: 'one' });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ level_before: 1, level: 2, delta: { level: 2, xp: 0 } });
    const led = await getPool().query("SELECT delta, reason, ref FROM xp_ledger WHERE character_id = $1", [h.dbId]);
    expect(led.rows).toEqual([{ delta: getGameData().economy.progression.xpToNext[0], reason: 'test_boost', ref: 'test-tools' }]);
  });

  it('만렙까지: 레벨 상한(3장 잠금이면 40), 그 뒤에는 409 TEST_LEVEL_MAX', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/test-tools/level', { mode: 'max' });
    expect(res.status).toBe(200);
    expect(await levelXp(h)).toEqual({ level: CHAPTER2_LEVEL_CAP, xp: 0 });
    const again = await post(app, h, '/test-tools/level', { mode: 'one' });
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('TEST_LEVEL_MAX');
  });

  it('멱등: 같은 request_id를 다시 보내면 처음 응답 그대로, 한 번만 반영', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, h, '/test-tools/level', { mode: 'one' }, rid);
    const b = await post(app, h, '/test-tools/level', { mode: 'one' }, rid);
    expect(b.status).toBe(a.status);
    expect(b.body).toEqual(a.body);
    expect((await levelXp(h)).level).toBe(2);
    const q1 = randomUUID();
    await post(app, h, '/test-tools/main-quest', { mode: 'one' }, q1);
    await post(app, h, '/test-tools/main-quest', { mode: 'one' }, q1);
    expect(await claimedOf(h)).toEqual(['c1_morning']);
  });

  it('개발 로그인 계정이 아니면 403 TEST_TOOLS_DEV_ONLY', async () => {
    const h = await newHero(app);
    await getPool().query(
      "UPDATE auth_identities SET provider = 'steam' WHERE account_id = (SELECT account_id FROM characters WHERE id = $1)",
      [h.dbId],
    );
    for (const path of ['/test-tools/main-quest', '/test-tools/level']) {
      const res = await post(app, h, path, { mode: 'one' });
      expect(res.status).toBe(403);
      expect(res.body.errors.code).toBe('TEST_TOOLS_DEV_ONLY');
    }
    expect(await claimedOf(h)).toEqual([]);
    expect((await levelXp(h)).level).toBe(1);
  });
});
