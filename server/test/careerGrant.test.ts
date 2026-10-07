// 9단계 8절: 전직·각성의 서버 기록(C1~C4), PUT state 거절 규칙, 백필, 뽑기 장비 계정 귀속
import { randomUUID } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { resetAntiAbuseDataCache } from '../src/gamedata/antiAbuseData';
import { advance, resetClock, setNowAt } from './auctionHelpers';
import { anomalyKinds, get, newHero, post, seedLevel, type Hero } from './economyHelpers';
import { auth, buildApp, DATA_DIR, emptyState, resetDb, shutdown, ver } from './helpers';
import { formParty, startAndBegin } from './partyHelpers';

let app: Express;
beforeEach(async () => {
  await resetDb();
  resetClock();
  setNowAt(new Date('2026-10-05T03:00:00Z'));
  app = buildApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const beat = (a: Express, h: Hero, map = 'village') =>
  request(a).post(`/characters/${h.id}/presence`).set(auth(h.s)).send({ map_id: map, auto_play: false, input_recent: true });
const promote = (a: Express, h: Hero, career: number, rid: string = randomUUID()) => post(a, h, '/career/promote', { career }, rid);
const start = (a: Express, h: Hero) => post(a, h, '/career/awakening/trial/start', {});
const finish = (a: Express, h: Hero, result: 'success' | 'fail') => post(a, h, '/career/awakening/trial/finish', { result });
const adv = (a: Express, h: Hero, from: number) => post(a, h, '/career/awakening/advance', { from_stage: from });
const grant = async (h: Hero): Promise<{ career: number; stage: number; source: string } | undefined> =>
  (await getPool().query('SELECT career, stage, source FROM character_career WHERE character_id = $1', [h.dbId])).rows[0];
const stateCareer = async (h: Hero): Promise<Record<string, unknown> | null> =>
  ((await getPool().query('SELECT career FROM character_state WHERE character_id = $1', [h.dbId])).rows[0] as { career: Record<string, unknown> | null }).career;
const putState = (a: Express, h: Hero, career: object | null, version = 0, extra: Record<string, unknown> = {}) =>
  request(a).put(`/characters/${h.id}/state`).set(auth(h.s)).send({ ...emptyState(version), ...(career ? { career } : {}), ...extra });

const warrior = async (level = 15): Promise<Hero> => {
  const h = await newHero(app);
  await seedLevel(h, level);
  return h;
};
/** 대화 단계를 거쳐 시련 대기(stage 2)까지 */
async function toStage2(a: Express, h: Hero, career = 1): Promise<void> {
  expect((await promote(a, h, career)).status).toBe(200);
  for (const from of [0, 1]) {
    advance(11_000);
    await beat(a, h);
    const r = await adv(a, h, from);
    expect(r.status).toBe(200);
  }
  expect((await grant(h))?.stage).toBe(2);
}

describe('C1 전직', () => {
  it('정상: character_career와 character_state.career를 서버가 쓴다. 같은 request_id 재전송은 같은 응답, 다른 직업은 409', async () => {
    const h = await warrior();
    const rid = randomUUID();
    const res = await promote(app, h, 1, rid);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ career: 1, stage: 0 });
    expect(await grant(h)).toEqual({ career: 1, stage: 0, source: 'promote' });
    expect(await stateCareer(h)).toEqual({ schema: 1, career: 1, nodes: [], training: [], refunded: 0, questStage: 0, awakened: false });
    const again = await promote(app, h, 1, rid);
    expect(again.headers['idempotent-replay']).toBe('true');
    expect(again.body).toEqual(res.body);
    const other = await promote(app, h, 2);
    expect(other.status).toBe(409);
    expect(other.body.errors.code).toBe('ALREADY_PROMOTED');
    // 같은 request_id에 다른 본문
    expect((await promote(app, h, 2, rid)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });

  it('입력 오류와 규칙 오류: 레벨 14는 LEVEL_TOO_LOW, 직업 불일치는 CAREER_BASE_MISMATCH, 범위 밖은 400', async () => {
    const low = await warrior(14);
    const r = await promote(app, low, 1);
    expect(r.status).toBe(422);
    expect(r.body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: 15, have: 14 });
    const h = await warrior();
    expect((await promote(app, h, 3)).body.errors.code).toBe('CAREER_BASE_MISMATCH');
    expect((await promote(app, h, 5)).status).toBe(400);
    expect((await post(app, h, '/career/promote', { career: 1, cost: 0 })).status).toBe(400);
    expect(await grant(h)).toBeUndefined();
  });

  it('동시 전직 두 개(다른 request_id): 하나만 성공한다', async () => {
    const h = await warrior();
    const res = await Promise.all([promote(app, h, 1), promote(app, h, 2)]);
    expect(res.map((r) => r.status).sort()).toEqual([200, 409]);
    expect((await getPool().query('SELECT count(*) AS n FROM character_career WHERE character_id = $1', [h.dbId])).rows[0]?.n).toBe('1');
  });
});

describe('PUT state의 서버 진실 규칙 (8.3)', () => {
  const career = (over: Record<string, unknown> = {}) => ({ schema: 1, career: 1, nodes: [], training: [], refunded: 0, questStage: 0, awakened: false, ...over });

  it('서버가 부여하지 않은 전직(career 1)과 각성 값은 422 INVALID_CAREER', async () => {
    const h = await warrior();
    const no = await putState(app, h, career());
    expect(no.status).toBe(422);
    expect(no.body.errors).toMatchObject({ code: 'INVALID_CAREER', reason: 'NOT_GRANTED' });
    await promote(app, h, 1);
    const stage = await putState(app, h, career({ questStage: 5, awakened: true }));
    expect(stage.body.errors).toMatchObject({ code: 'INVALID_CAREER', reason: 'STAGE_NOT_GRANTED' });
    expect((await putState(app, h, career({ career: 2 }))).body.errors.reason).toBe('NOT_GRANTED');
    // 전직 취소 시도
    expect((await putState(app, h, career({ career: 0 }))).body.errors.reason).toBe('MISSING_STATE');
    // 서버 기록과 일치하는 값은 통과
    expect((await putState(app, h, career())).status).toBe(200);
    // 서버 값보다 뒤처진 값은 거절(서버가 PUT보다 먼저 쓴다)
    await getPool().query('UPDATE character_career SET stage = 3 WHERE character_id = $1', [h.dbId]);
    await getPool().query("UPDATE character_state SET career = career || '{\"questStage\": 3}'::jsonb WHERE character_id = $1", [h.dbId]);
    expect((await putState(app, h, career({ questStage: 1 }), 1)).body.errors.reason).toBe('QUEST_REGRESSION');
  });

  it('CAREER_SERVER_TRUTH=log는 거절하지 않고 기록만, off는 옛 동작', async () => {
    const h = await warrior();
    const log = buildApp({ CAREER_SERVER_TRUTH: 'log' });
    expect((await putState(log, h, career())).status).toBe(200);
    const h2 = await warrior();
    const off = buildApp({ CAREER_SERVER_TRUTH: 'off' });
    expect((await putState(off, h2, career())).status).toBe(200);
    app = buildApp();
  });

  it('백필: 이 마이그레이션 전에 저장된 전직 상태는 legacy_backfill로 이전되고 PUT이 통과한다', async () => {
    const h = await warrior(20);
    // 옛 클라이언트가 서버 기록 없이 저장했던 상태(전직 + 각성 3단계)
    await getPool().query('UPDATE character_state SET career = $2::jsonb WHERE character_id = $1', [h.dbId, JSON.stringify(career({ career: 1, questStage: 3 }))]);
    const sql = fs.readFileSync(path.resolve(__dirname, '../migrations/0020_anti_abuse.sql'), 'utf8');
    const backfill = /INSERT INTO character_career \(character_id[\s\S]*?;\n/.exec(sql)?.[0] as string;
    expect(backfill).toContain('legacy_backfill');
    await getPool().query(backfill);
    expect(await grant(h)).toEqual({ career: 1, stage: 3, source: 'legacy_backfill' });
    expect((await putState(app, h, career({ questStage: 3 }))).status).toBe(200);
    // 백필은 상태가 없거나 career 0인 캐릭터를 건드리지 않는다
    const other = await warrior();
    expect(await grant(other)).toBeUndefined();
  });

  it('career_path 퀘스트는 서버가 부여한 전직만 인정한다(상태 JSON만 고친 값은 PUT이 막는다)', async () => {
    const h = await warrior();
    expect((await post(app, h, '/quests/career_path/claim', {})).body.errors.code).toBe('QUEST_NOT_DONE');
    expect((await putState(app, h, career())).status).toBe(422);
    expect((await post(app, h, '/quests/career_path/claim', {})).body.errors.code).toBe('QUEST_NOT_DONE');
    await promote(app, h, 1);
    expect((await post(app, h, '/quests/career_path/claim', {})).status).toBe(200);
  });
});

describe('C2~C4 각성 흐름', () => {
  it('정상: 0->1->2, 시련(성공)->3->4->5, 각성 슬롯 PUT 통과. 단계마다 character_state.career가 서버 값으로 갱신된다', async () => {
    const h = await warrior(40);
    await toStage2(app, h);
    expect(await stateCareer(h)).toMatchObject({ questStage: 2, awakened: false });
    await beat(app, h);
    const st = await start(app, h);
    expect(st.status).toBe(200);
    expect(st.body.data.expires_in_seconds).toBe(120);
    expect(Object.keys(st.body.data).sort()).toEqual(['expires_in_seconds', 'started_at']);
    advance(21_000);
    await beat(app, h);
    const fin = await finish(app, h, 'success');
    expect(fin.status).toBe(200);
    expect(fin.body.data).toEqual({ stage: 3 });
    for (const from of [3, 4]) {
      advance(11_000);
      await beat(app, h);
      const r = await adv(app, h, from);
      expect(r.body.data).toEqual({ stage: from + 1, awakened: from === 4 });
    }
    expect(await grant(h)).toMatchObject({ career: 1, stage: 5 });
    expect(await stateCareer(h)).toMatchObject({ questStage: 5, awakened: true });
    const put = await putState(app, h, { schema: 1, career: 1, nodes: [], training: [], refunded: 0, questStage: 5, awakened: true }, 0, {
      skill_gems: [{ slot: 4, active: 'f_awake', supports: [null, null] }],
    });
    expect(put.status).toBe(200);
    expect((await get(app, h, '')).body.data.character.state.career).toMatchObject({ awakened: true });
  });

  it('C4: 시련(2)은 TRIAL_REQUIRED, 현재 단계와 다르면 STAGE_MISMATCH(현재 값 반환), 10초 안에 연속 호출은 429 STAGE_TOO_FAST', async () => {
    const h = await warrior();
    expect((await adv(app, h, 0)).body.errors.code).toBe('NOT_PROMOTED');
    await promote(app, h, 1);
    advance(11_000);
    await beat(app, h);
    expect((await adv(app, h, 2)).body.errors.code).toBe('TRIAL_REQUIRED');
    expect((await post(app, h, '/career/awakening/advance', { from_stage: 5 })).status).toBe(400);
    const mismatch = await adv(app, h, 1);
    expect(mismatch.status).toBe(409);
    expect(mismatch.body.errors).toMatchObject({ code: 'STAGE_MISMATCH', stage: 0 });
    expect((await adv(app, h, 0)).status).toBe(200);
    await beat(app, h);
    const fast = await adv(app, h, 1);
    expect(fast.status).toBe(429);
    expect(fast.body.errors).toMatchObject({ code: 'STAGE_TOO_FAST' });
    expect(fast.body.errors.retry_after_sec).toBeGreaterThan(0);
    expect(fast.headers['retry-after']).toBeDefined();
  });

  it('프레즌스가 마을이 아니거나 신선하지 않으면 C2~C4가 409 NOT_IN_TOWN', async () => {
    const h = await warrior();
    await promote(app, h, 1);
    advance(11_000);
    expect((await adv(app, h, 0)).body.errors.code).toBe('NOT_IN_TOWN'); // 프레즌스 없음
    await beat(app, h, 'forest');
    expect((await adv(app, h, 0)).body.errors.code).toBe('NOT_IN_TOWN'); // 사냥터
    await beat(app, h, 'village');
    expect((await adv(app, h, 0)).status).toBe(200);
    advance(11_000);
    await beat(app, h);
    await adv(app, h, 1);
    advance(120_000); // 신호가 90초 넘게 없다
    expect((await start(app, h)).body.errors.code).toBe('NOT_IN_TOWN');
  });

  it('C3: 시작 없이 finish는 TRIAL_NOT_STARTED, 최소 시간 전 성공은 TRIAL_TOO_FAST(+이상 기록, 시도는 그대로), 120초 뒤는 TRIAL_EXPIRED, 실패는 재도전', async () => {
    const h = await warrior();
    await toStage2(app, h);
    await beat(app, h);
    expect((await finish(app, h, 'success')).body.errors.code).toBe('TRIAL_NOT_STARTED');
    expect((await start(app, h)).status).toBe(200);
    advance(2_000);
    await beat(app, h);
    const fast = await finish(app, h, 'success');
    expect(fast.status).toBe(422);
    expect(fast.body.errors.code).toBe('TRIAL_TOO_FAST');
    expect(await anomalyKinds(h)).toEqual(['career_state']);
    expect((await grant(h))?.stage).toBe(2);
    // 실패는 열린 시도를 닫는다(재도전 가능)
    expect((await finish(app, h, 'fail')).body.data).toEqual({ stage: 2 });
    expect((await getPool().query("SELECT outcome FROM character_career_trials WHERE character_id = $1", [h.dbId])).rows).toEqual([{ outcome: 'fail' }]);
    // 다시 시작하고 120초를 넘기면 만료: 성공 보고는 거절되고, 다음 C2가 만료로 닫고 새로 시작한다
    const again = await start(app, h);
    advance(125_000);
    await beat(app, h);
    expect((await finish(app, h, 'success')).body.errors.code).toBe('TRIAL_EXPIRED');
    const restart = await start(app, h);
    expect(restart.status).toBe(200);
    expect(restart.body.data.started_at).not.toBe(again.body.data.started_at);
    expect((await getPool().query('SELECT outcome FROM character_career_trials WHERE character_id = $1 ORDER BY id', [h.dbId])).rows).toEqual([{ outcome: 'fail' }, { outcome: 'expired' }, { outcome: null }]);
    // 시도 내내 같은 마을에 있었는지: 사냥터를 다녀오면 성공이 거절된다
    advance(10_000);
    await beat(app, h, 'forest');
    await beat(app, h, 'village');
    expect((await finish(app, h, 'success')).body.errors.code).toBe('NOT_IN_TOWN');
  });

  it('데이터 trials: 가디언은 최소 20초와 필수 노드(g_taunt, g_wall)가 필요하다. 시련 시작은 열린 시도를 돌려주는 멱등', async () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gd-'));
    for (const f of fs.readdirSync(DATA_DIR)) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
    const careers = JSON.parse(fs.readFileSync(path.join(dir, 'careers.json'), 'utf8')) as Record<string, unknown>;
    careers.trials = { '1': { minSeconds: 6, requiredNodes: [] }, '2': { minSeconds: 20, requiredNodes: ['g_taunt', 'g_wall'] } };
    fs.writeFileSync(path.join(dir, 'careers.json'), JSON.stringify(careers));
    resetAntiAbuseDataCache();
    const withTrials = buildApp({ GAME_DATA_DIR: dir });
    const h = await warrior();
    await toStage2(withTrials, h, 2);
    await beat(withTrials, h);
    const noNodes = await start(withTrials, h);
    expect(noNodes.status).toBe(422);
    expect(noNodes.body.errors).toMatchObject({ code: 'NODES_REQUIRED', missing: ['g_taunt', 'g_wall'] });
    await getPool().query("UPDATE character_state SET career = career || $2::jsonb WHERE character_id = $1", [h.dbId, JSON.stringify({ nodes: [{ id: 'g_taunt', rank: 1 }, { id: 'g_wall', rank: 1 }] })]);
    const a = await start(withTrials, h);
    const b = await start(withTrials, h);
    expect(a.body.data.started_at).toBe(b.body.data.started_at);
    advance(10_000);
    await beat(withTrials, h);
    expect((await finish(withTrials, h, 'success')).body.errors.code).toBe('TRIAL_TOO_FAST'); // 10초 < 20초
    advance(11_000);
    await beat(withTrials, h);
    expect((await finish(withTrials, h, 'success')).body.data).toEqual({ stage: 3 });
    resetAntiAbuseDataCache();
    app = buildApp();
  });

  it('파티 판 참여 중에는 시련을 시작할 수 없다(IN_PARTY_CONTENT)', async () => {
    const host = await warrior(17);
    const member = await newHero(app);
    await formParty(app, host, [member]);
    await startAndBegin(app, host, [member]);
    await getPool().query("INSERT INTO character_career (character_id, career, stage, source) VALUES ($1, 1, 2, 'promote')", [host.dbId]);
    await beat(app, host);
    expect((await start(app, host)).body.errors.code).toBe('IN_PARTY_CONTENT');
  });

  it('입력 오류: request_id 없음 400, 남의 캐릭터 404, 정지 계정 403', async () => {
    const h = await warrior();
    const o = await newHero(app);
    expect((await request(app).post(`/characters/${h.id}/career/awakening/trial/start`).set(auth(h.s)).send({})).status).toBe(400);
    expect((await request(app).post(`/characters/${o.id}/career/promote`).set(auth(h.s)).send({ request_id: randomUUID(), career: 1 })).status).toBe(404);
    await getPool().query("UPDATE accounts SET banned_until = now() + interval '1 hour' WHERE uuid = $1", [h.s.accountId]);
    expect((await promote(app, h, 1)).status).toBe(403);
    void ver;
  });
});

describe('8.6 뽑기 장비 계정 귀속', () => {
  it('gacha 장비는 bind=account로 가방에 들어가고 경매 등록은 ITEM_BOUND, 이전에 가진 귀속 없는 스택은 그대로', async () => {
    const h = await newHero(app);
    const a = await getPool().query('SELECT account_id FROM characters WHERE id = $1', [h.dbId]);
    await getPool().query('INSERT INTO star_wallets (account_id, balance) VALUES ($1, 1000) ON CONFLICT (account_id) DO UPDATE SET balance = 1000', [a.rows[0]?.account_id]);
    const res = await post(app, h, '/starshop/pull', { count: 1, banner: 'weapon' });
    expect(res.status).toBe(200);
    const rows = await getPool().query("SELECT item_key, bind FROM character_items WHERE character_id = $1 AND location = 'bag' AND item_key LIKE 'eq\\_%'", [h.dbId]);
    expect(rows.rows.length).toBeGreaterThan(0);
    expect(rows.rows.every((r) => r.bind === 'account')).toBe(true);
    const key = rows.rows[0]?.item_key as string;
    resetClock(); // 계정 나이 판정은 서버 시계를 쓴다(앞 설정의 과거 시각을 되돌린다)
    const list = await post(app, h, '/auction/listings', { item_key: key, count: 1, buyout: 1000, hours: 12 });
    expect(list.status).toBe(422);
    expect(list.body.errors.code).toBe('ITEM_BOUND');
  });
});
