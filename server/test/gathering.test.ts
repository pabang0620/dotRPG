import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { buildApp, resetDb, shutdown } from './helpers';
import {
  anomalyKinds,
  countOf,
  expectLedgerConsistent,
  get,
  newHero,
  post,
} from './economyHelpers';

const app = buildApp();
let fixed = new Date('2026-10-05T03:00:00Z');
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeAll(resetDb);
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});
beforeEach(() => {
  fixed = new Date('2026-10-05T03:00:00Z');
  setClockOverride(() => fixed);
});

const crop = { map_id: 'village', node_id: 'village:6:10' }; // 당근 밭: 당근 1개, 40초 재생 (나무·바위 채집은 2026-10-12 폐지)

describe('POST /characters/:id/gathers', () => {
  it('정상: 서버 데이터대로 지급하고 재생 시각을 알려 준다', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/gathers', crop);
    expect(res.status).toBe(200);
    expect(res.body.data.granted).toEqual({ item_key: 'carrot', count: 1 });
    expect(res.body.data.ready_at).toBe(new Date(fixed.getTime() + 40_000).toISOString());
    expect(await countOf(h, 'carrot')).toBe(1);
    await expectLedgerConsistent(h);
  });

  it('입력 오류: 수량을 보내거나 노드 id 형식이 틀리면 400', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/gathers', { ...crop, count: 99 })).status).toBe(400);
    expect((await post(app, h, '/gathers', { ...crop, node_id: 'x' })).status).toBe(400);
    expect((await post(app, h, '/gathers', { map_id: 'forest' })).status).toBe(400);
  });

  it('없는 노드, 다른 맵의 노드는 422 NODE_UNKNOWN과 이상 기록', async () => {
    const h = await newHero(app);
    const a = await post(app, h, '/gathers', { map_id: 'forest', node_id: 'forest:1:1' });
    expect(a.status).toBe(422);
    expect(a.body.errors.code).toBe('NODE_UNKNOWN');
    const b = await post(app, h, '/gathers', { map_id: 'forest', node_id: 'village:6:10' });
    expect(b.body.errors.code).toBe('NODE_UNKNOWN');
    expect(await anomalyKinds(h)).toEqual(['gather_node', 'gather_node']);
    expect(await countOf(h, 'carrot')).toBe(0);
  });

  it('재생 전 재채집은 409 NODE_NOT_READY(ready_at 포함), 서버 시계로 재생된다', async () => {
    const h = await newHero(app);
    await post(app, h, '/gathers', crop);
    const early = await post(app, h, '/gathers', crop);
    expect(early.status).toBe(409);
    expect(early.body.errors.code).toBe('NODE_NOT_READY');
    expect(early.body.errors.ready_at).toBe(new Date(fixed.getTime() + 40_000).toISOString());
    expect(await anomalyKinds(h)).toEqual(['gather_early']);
    expect(await countOf(h, 'carrot')).toBe(1);
    // 보고 지연 여유(3초) 안쪽은 허용
    advance(37);
    expect((await post(app, h, '/gathers', crop)).status).toBe(200);
    expect(await countOf(h, 'carrot')).toBe(2);
  });

  it('재전송: 같은 request_id는 한 번만 지급', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, h, '/gathers', crop, rid);
    const b = await post(app, h, '/gathers', crop, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await countOf(h, 'carrot')).toBe(1);
  });

  it('동시 요청: 같은 노드를 동시에 캐도 한 번만 성공', async () => {
    const h = await newHero(app);
    const [a, b] = await Promise.all([post(app, h, '/gathers', crop), post(app, h, '/gathers', crop)]);
    expect([a.status, b.status].sort()).toEqual([200, 409]);
    expect(await countOf(h, 'carrot')).toBe(1);
    await expectLedgerConsistent(h);
  });
});

describe('채집 분당 상한', () => {
  it('1분 안에 40개 노드를 캤으면 다음 채집은 429 GATHER_RATE_LIMITED, 1분 뒤 다시 받는다', async () => {
    const h = await newHero(app);
    for (let i = 0; i < 40; i++) {
      await getPool().query(
        'INSERT INTO character_node_state (character_id, node_id, map_id, last_gathered_at) VALUES ($1, $2, $3, $4)',
        [h.dbId, `canyon:${i}:0`, 'canyon', new Date(fixed.getTime() - 10_000)],
      );
    }
    const res = await post(app, h, '/gathers', crop);
    expect(res.status).toBe(429);
    expect(res.body.errors.code).toBe('GATHER_RATE_LIMITED');
    expect(await anomalyKinds(h)).toEqual(['gather_rate']);
    expect(await countOf(h, 'carrot')).toBe(0);
    advance(60);
    expect((await post(app, h, '/gathers', crop)).status).toBe(200);
  });
});

describe('GET /characters/:id/maps/:map_id/nodes', () => {
  it('재생 중인 노드만 돌려준다', async () => {
    const h = await newHero(app);
    await post(app, h, '/gathers', crop);
    let res = await get(app, h, '/maps/village/nodes');
    expect(res.status).toBe(200);
    expect(res.body.data.cooling).toEqual([
      { node_id: 'village:6:10', kind: 'crop', ready_at: new Date(fixed.getTime() + 40_000).toISOString() },
    ]);
    advance(41);
    res = await get(app, h, '/maps/village/nodes');
    expect(res.body.data.cooling).toEqual([]);
  });

  it('던전 방이나 없는 맵은 422 MAP_INVALID, 남의 캐릭터는 404', async () => {
    const h = await newHero(app);
    expect((await get(app, h, '/maps/dgn_canyon_1/nodes')).body.errors.code).toBe('MAP_INVALID');
    expect((await get(app, h, '/maps/nowhere/nodes')).status).toBe(422);
    const other = await newHero(app);
    const res = await get(app, { ...other, id: h.id }, '/maps/forest/nodes');
    expect(res.status).toBe(404);
  });
});

describe('POST /characters/:id/chests/open', () => {
  const chest = { chest_id: 'canyon:46:40' };

  it('정상: 서버 데이터의 보상을 한 번만', async () => {
    const h = await newHero(app);
    const res = await post(app, h, '/chests/open', chest);
    expect(res.status).toBe(200);
    expect(res.body.data.granted).toEqual({ item_key: 'eq_neck_10_r', count: 1 });
    expect(await countOf(h, 'eq_neck_10_r')).toBe(1);
    const detail = await get(app, h, '');
    expect(detail.body.data.character.opened_chests).toEqual(['canyon:46:40']);
  });

  it('두 번째는 409 CHEST_ALREADY_OPENED(다른 request_id여도)', async () => {
    const h = await newHero(app);
    await post(app, h, '/chests/open', chest);
    const again = await post(app, h, '/chests/open', chest);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('CHEST_ALREADY_OPENED');
    expect(await countOf(h, 'eq_neck_10_r')).toBe(1);
  });

  it('입력 오류와 없는 상자(이상 기록)', async () => {
    const h = await newHero(app);
    expect((await post(app, h, '/chests/open', {})).status).toBe(400);
    expect((await post(app, h, '/chests/open', { ...chest, reward: 'x' })).status).toBe(400);
    const res = await post(app, h, '/chests/open', { chest_id: 'forest:1:2' });
    expect(res.body.errors.code).toBe('CHEST_UNKNOWN');
    expect(await anomalyKinds(h)).toEqual(['chest_unknown']);
  });

  it('재전송은 같은 응답, 동시 요청은 한 번만', async () => {
    const h = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, h, '/chests/open', chest, rid);
    const b = await post(app, h, '/chests/open', chest, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    const h2 = await newHero(app);
    const [c, d] = await Promise.all([post(app, h2, '/chests/open', chest), post(app, h2, '/chests/open', chest)]);
    expect([c.status, d.status].sort()).toEqual([200, 409]);
    expect(await countOf(h2, 'eq_neck_10_r')).toBe(1);
    await expectLedgerConsistent(h2);
  });
});
