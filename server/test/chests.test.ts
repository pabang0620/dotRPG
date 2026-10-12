import { randomUUID } from 'node:crypto';
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

beforeAll(resetDb);
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});
beforeEach(() => {
  fixed = new Date('2026-10-05T03:00:00Z');
  setClockOverride(() => fixed);
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
