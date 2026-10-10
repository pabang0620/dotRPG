import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { buildApp, resetDb, shutdown } from './helpers';
import { newHero, post, seedClaims, seedLevel, type Hero } from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

// career_path(1-15)는 메인 1-14(c2s_trader_guard)까지 끝내야 받는다
const CAREER_PREREQS = ['c1_morning', 'c1_festival', 'c1_burning', 'c1_ashes', 'c1_rise', 'c1_rebuild', 'c1_trail', 'c1s_patrol', 'c1s_ruins', 'c1s_firstdungeon', 'c1s_depths', 'c1_crossing', 'c2s_trader', 'c2s_trader_guard'];
const claim = (h: Hero, rid?: string) => post(app, h, '/quests/career_path/claim', {}, rid);

async function readyHero(): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, 15);
  await seedClaims(h, CAREER_PREREQS);
  return h;
}

describe('전직의 길(career_path) 완료 판정은 서버 기록(character_career)', () => {
  it('서버 전직 기록이 있으면 완료', async () => {
    const h = await readyHero();
    await getPool().query(
      "INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, 1, 0, 'promote')",
      [h.dbId],
    );
    const res = await claim(h);
    expect(res.status).toBe(200);
    expect(res.body.data.quest_id).toBe('career_path');
  });

  it('클라이언트 상태(character_state.career)만 전직이고 서버 기록이 없으면 거절', async () => {
    const h = await readyHero();
    await getPool().query(
      `UPDATE character_state SET career = '{"schema":1,"career":1,"nodes":[],"training":[],"refunded":0,"questStage":0,"awakened":false}'::jsonb WHERE character_id = $1`,
      [h.dbId],
    );
    const res = await claim(h);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('QUEST_NOT_DONE');
    expect(res.body.errors.objective).toEqual({ type: 'flag', flag: 'career_promoted' });
  });

  it('같은 request_id 재전송은 같은 응답', async () => {
    const h = await readyHero();
    await getPool().query(
      "INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, 2, 0, 'promote')",
      [h.dbId],
    );
    const rid = randomUUID();
    const a = await claim(h, rid);
    const b = await claim(h, rid);
    expect(a.status).toBe(200);
    expect(b.status).toBe(200);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
  });
});
