import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { buildApp, resetDb, shutdown } from './helpers';
import { newHero, post, seedLevel, type Hero } from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
afterAll(shutdown);

const claim = (h: Hero, rid?: string) => post(app, h, '/quests/career_path/claim', {}, rid);

async function readyHero(): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, 15);
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
