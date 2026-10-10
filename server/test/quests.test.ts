import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { buildApp, resetDb, shutdown } from './helpers';
import {
  anomalyKinds,
  countOf,
  expectLedgerConsistent,
  get,
  goldOf,
  newHero,
  post,
  seedClaims,
  seedItem,
  seedLevel,
  starterCount,
  type Hero,
} from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

// 보상 XP는 레벨 곡선을 따라 바뀌므로(Docs/BALANCE_EARLY_PROGRESSION.md) 숫자를 박지 않고 서버 데이터에서 읽는다
const questXp = (questId: string): number => getGameData().economy.quests.get(questId)!.reward.xp;
const claim = (h: Hero, questId: string, rid?: string) => post(app, h, `/quests/${questId}/claim`, {}, rid);

// 1-8 ~ 1-18 (2026-10-10): 사냥터마다 메인 단계가 하나씩 c1_trail과 c1_stronger 사이에 있다(Docs/BALANCE_EARLY_PROGRESSION.md)
const MAIN_TO_STRONGER = [
  'c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise', 'c1_rebuild', 'c1_trail',
  'c1s_patrol', 'c1s_ruins', 'c1s_firstdungeon', 'c1s_depths', 'c1_crossing', 'c2s_trader', 'c2s_trader_guard',
  'career_path', 'c2s_trader_ore', 'c1_quarry', 'c1_scout',
  'c1_stronger',
];

async function setKills(h: Hero, monster: string, kills: number): Promise<void> {
  await getPool().query(
    `INSERT INTO kill_stats (character_id, monster_id, kills) VALUES ($1, $2, $3)
     ON CONFLICT (character_id, monster_id) DO UPDATE SET kills = EXCLUDED.kills`,
    [h.dbId, monster, kills],
  );
}

describe('POST /characters/:id/quests/:quest_id/claim', () => {
  it('정상: 서버 데이터의 보상이 지급되고 청구 기록이 남는다', async () => {
    const h = await newHero(app);
    const res = await claim(h, 'c1_morning');
    expect(res.status).toBe(200);
    expect(res.body.data.reward).toMatchObject({ xp: questXp('c1_morning'), gold: 0, items: [], set_flags: ['festival_eve'] });
    expect(res.body.data.delta).toEqual({ level: 1, xp: questXp('c1_morning') });
    const detail = await get(app, h, '');
    expect(detail.body.data.character.claimed_quests).toEqual(['c1_morning']);
    await expectLedgerConsistent(h);
  });

  it('골드·아이템 보상과 원장(퀘스트 id가 ref)', async () => {
    const h = await newHero(app);
    await seedClaims(h, ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes']);
    await setKills(h, 'skeleton', 3);
    const res = await claim(h, 'c1_rise');
    expect(res.status).toBe(200);
    expect(res.body.data.reward).toMatchObject({ xp: questXp('c1_rise'), gold: 300, items: [{ item_key: 'potion_hp', count: 5 }] });
    expect(await goldOf(h)).toBe(400);
    expect(await countOf(h, 'potion_hp')).toBe(starterCount('potion_hp') + 5);
    const gl = await getPool().query("SELECT ref FROM gold_ledger WHERE character_id = $1 AND reason = 'quest_reward'", [h.dbId]);
    expect(gl.rows).toEqual([{ ref: 'c1_rise' }]);
    await expectLedgerConsistent(h);
  });

  it('입력 오류: 본문에 보상을 보내면 400, quest_id 형식 오류도 400', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/quests/c1_morning/claim', { xp: 99999 })).status).toBe(400);
    expect((await post(app, h, '/quests/bad%20id/claim', {})).status).toBe(400);
    expect((await claim(h, 'no_such_quest')).body.errors.code).toBe('QUEST_UNKNOWN');
  });

  it('두 번 청구: 다른 request_id로 다시 청구해도 409이고 보상은 한 번', async () => {
    const h = await newHero(app);
    await claim(h, 'c1_morning');
    const again = await claim(h, 'c1_morning');
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('QUEST_ALREADY_CLAIMED');
    const xp = await getPool().query('SELECT xp FROM characters WHERE id = $1', [h.dbId]);
    expect(xp.rows[0].xp).toBe(questXp('c1_morning'));
  });

  it('재전송: 같은 request_id는 같은 응답', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await claim(h, 'c1_morning', rid);
    const b = await claim(h, 'c1_morning', rid);
    expect(b.status).toBe(200);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
  });

  it('동시 요청: 같은 퀘스트를 동시에 청구하면 한쪽만 성공', async () => {
    const h = await newHero(app);
    const [a, b] = await Promise.all([claim(h, 'c1_morning'), claim(h, 'c1_morning')]);
    expect([a.status, b.status].sort()).toEqual([200, 409]);
    const n = await getPool().query('SELECT count(*)::int AS n FROM xp_ledger WHERE character_id = $1', [h.dbId]);
    expect(n.rows[0].n).toBe(1);
  });

  it('선행 퀘스트 누락은 422 QUEST_PREREQ(missing)와 이상 기록', async () => {
    const h = await newHero(app);
    const res = await claim(h, 'c1_festival');
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('QUEST_PREREQ');
    expect(res.body.errors.missing).toEqual(['c1_morning']);
    expect(await anomalyKinds(h)).toEqual(['quest_denied']);
  });

  it('처치 목표: 서버가 받아들인 누적 처치가 모자라면 QUEST_NOT_DONE, 채우면 통과', async () => {
    const h = await newHero(app);
    await seedClaims(h, ['c1_morning', 'c1_festival']);
    await setKills(h, 'skel_warrior', 4);
    const no = await claim(h, 'c1_burning');
    expect(no.status).toBe(422);
    expect(no.body.errors.code).toBe('QUEST_NOT_DONE');
    expect(no.body.errors.objective).toMatchObject({ type: 'kill', monster_id: 'skel_warrior', need: 5, have: 4 });
    await setKills(h, 'skel_warrior', 5);
    expect((await claim(h, 'c1_burning')).status).toBe(200);
  });

  it('처치 목표는 이미 청구한 퀘스트의 같은 몬스터 목표와 합산한다', async () => {
    const h = await newHero(app);
    await seedClaims(h, ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise']);
    await setKills(h, 'skeleton', 5); // c1_rise(3) + c1_rebuild(3) = 6 필요
    const res = await claim(h, 'c1_rebuild');
    expect(res.body.errors.code).toBe('QUEST_NOT_DONE');
    expect(res.body.errors.objective).toMatchObject({ type: 'kill', need: 6, have: 5 });
  });

  it('공방 완공 플래그는 납품 기록으로 직접 확인하고, 최대 체력 보너스가 반영된다', async () => {
    const h = await newHero(app);
    await seedClaims(h, ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise']);
    await setKills(h, 'skeleton', 6);
    const notBuilt = await claim(h, 'c1_rebuild');
    expect(notBuilt.body.errors.objective).toEqual({ type: 'flag', flag: 'workshop_built' });
    await getPool().query(
      "INSERT INTO site_deliveries (character_id, site_id, item_key, delivered) VALUES ($1, 'workshop', 'wood', 6), ($1, 'workshop', 'stone', 4)",
      [h.dbId],
    );
    const res = await claim(h, 'c1_rebuild');
    expect(res.status).toBe(200);
    expect(res.body.data.bonus_max_health).toBe(20);
    expect(res.body.data.reward.items).toEqual([{ item_key: 'eq_ring_1_r', count: 1 }]);
    const detail = await get(app, h, '');
    expect(detail.body.data.character.bonus_max_health).toBe(20);
  });

  it('collect+consume 목표: 청구 시점에 재료를 가져가고, 모자라면 NOT_DONE', async () => {
    const h = await newHero(app);
    await seedClaims(h, ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise', 'c1_rebuild']); // 약초밭은 공방 재건 뒤에 열린다
    await seedItem(h, 'wood', 4);
    const no = await claim(h, 'c1s_herbs');
    expect(no.body.errors.code).toBe('QUEST_NOT_DONE');
    expect(no.body.errors.objective).toMatchObject({ type: 'collect', item_key: 'wood', need: 5, have: 4 });
    await seedItem(h, 'wood', 2);
    const ok = await claim(h, 'c1s_herbs');
    expect(ok.status).toBe(200);
    expect(ok.body.data.consumed).toEqual([{ item_key: 'wood', count: 5 }]);
    expect(await countOf(h, 'wood')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('레벨·던전 목표: c1_stronger는 레벨 20과 던전 1회가 필요', async () => {
    const h = await newHero(app);
    await seedClaims(h, MAIN_TO_STRONGER.slice(0, -1));
    const lv = await claim(h, 'c1_stronger');
    expect(lv.body.errors.objective).toMatchObject({ type: 'level', need: 20, have: 1 });
    await seedLevel(h, 20);
    const dg = await claim(h, 'c1_stronger');
    // 던전 목표는 받은 퀘스트끼리 누적된다: 1-10 요일 던전 1회 + 1-19 1회 = 2회
    expect(dg.body.errors.objective).toMatchObject({ type: 'dungeon', target: '*', need: 2, have: 0 });
    for (let i = 0; i < 12; i++) {
      await getPool().query(
        `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, started_at, ended_at, rank, cards)
         VALUES ($1, 'gold_vein', 0, 'cleared', now() - ($2 || ' days')::interval, now(), now(), 3, '[]'::jsonb)`,
        [h.dbId, String(i + 1)],
      );
    }
    const ok = await claim(h, 'c1_stronger');
    expect(ok.status).toBe(200);
    expect(await goldOf(h)).toBe(3100);
  });

  it('레이드 목표: 해당 레이드를 깬 판이 있어야 하고, 보상 잠긴 연습 클리어도 인정', async () => {
    const h = await newHero(app);
    await seedClaims(h, MAIN_TO_STRONGER);
    const no = await claim(h, 'c1_fortress');
    expect(no.status).toBe(422);
    expect(no.body.errors.code).toBe('QUEST_NOT_DONE');
    expect(no.body.errors.objective).toMatchObject({ type: 'raid', target: 'raid_skeleton_king', need: 1, have: 0 });
    await getPool().query(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, started_at, ended_at, rank, cards, reward_locked, lock_reason)
       VALUES ($1, 'raid_skeleton_king', 0, 'cleared', now(), now(), now(), 3, '[]'::jsonb, true, 'TOO_FEW_HUMANS')`,
      [h.dbId],
    );
    const ok = await claim(h, 'c1_fortress');
    expect(ok.status).toBe(200);
  });
});
