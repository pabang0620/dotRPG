// 전송 전환(fallbackService): 다음 후보 선택, 허용되지 않은 전환 거절, 멤버·세대·상한 제한
// (relay/steam 기본 시나리오는 transport.test.ts 가 이미 덮는다: 여기는 그 밖의 규칙)
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { resetSwitchCooldowns, switchMapSizes } from '../src/domains/transport/fallbackService';
import { buildApp, resetDb, shutdown, pinClockToMondayFlowing } from './helpers';
import { formParty, get, newHero, post, type Hero } from './partyHelpers';

let app = buildApp();
beforeAll(async () => {
  pinClockToMondayFlowing();
  await resetDb();
});
afterAll(shutdown);
beforeEach(() => resetSwitchCooldowns());

/** order 에 맞는 판을 만든다. dev 전송은 어떤 계정이든 자격이 되므로 Steam 계정 없이 만든다 */
async function room(order: string, n = 2): Promise<{ heroes: Hero[]; runId: string; order: string[] }> {
  app = buildApp({ COMBAT_TRANSPORT_ORDER: order, TRANSPORT_SWITCH_COOLDOWN_SECONDS: '0' });
  const heroes: Hero[] = [];
  for (let i = 0; i < n; i++) heroes.push(await newHero(app));
  await formParty(app, heroes[0] as Hero, heroes.slice(1));
  const st = await post(app, heroes[0] as Hero, '/party/start', { ai_count: 0 });
  if (st.status !== 201) throw new Error(`start ${st.status} ${JSON.stringify(st.body)}`);
  return { heroes, runId: st.body.data.run.id as string, order: st.body.data.run.transport.order as string[] };
}

const sw = (h: Hero, runId: string, body: Record<string, unknown>, rid: string = randomUUID()) =>
  post(app, h, `/rooms/run/${runId}/transport`, body, rid);
const fb = (h: Hero, runId: string, body: Record<string, unknown>, rid: string = randomUUID()) =>
  post(app, h, `/rooms/run/${runId}/transport/fallback`, body, rid);
const dbRun = async (runId: string) =>
  (await getPool().query<{ transport: string; transport_epoch: number; transport_switches: number }>(
    'SELECT transport, transport_epoch, transport_switches FROM party_runs WHERE uuid = $1',
    [runId],
  )).rows[0]!;

describe('다음 전송 선택', () => {
  it('transport_order 순서대로 한 칸씩: relay -> dev, 세대와 전환 수가 1씩 오른다', async () => {
    const { heroes, runId, order } = await room('relay,dev');
    expect(order).toEqual(['relay', 'dev']);
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1, failed: 'relay', reason: 'connect_timeout' });
    expect(r.status).toBe(200);
    expect(r.body.data.transport).toMatchObject({ current: 'dev', epoch: 2, order: ['relay', 'dev'] });
    expect(await dbRun(runId)).toEqual({ transport: 'dev', transport_epoch: 2, transport_switches: 1 });
  });

  it('후보가 셋이면 relay -> steam 후보가 없을 때 건너뛴다: 주문서에 있는 것만 후보다', async () => {
    // steam 은 자격(Steam 연결+능력)이 없어 order 에서 빠진다
    const { order } = await room('relay,steam,dev');
    expect(order).toEqual(['relay', 'dev']);
  });

  it('마지막 후보에서 더 갈 곳이 없으면 409 TRANSPORT_EXHAUSTED, 상태는 그대로', async () => {
    const { heroes, runId } = await room('relay,dev', 3);
    await sw(heroes[1] as Hero, runId, { epoch: 1 });
    const r = await sw(heroes[2] as Hero, runId, { epoch: 2 });
    expect(r.status).toBe(409);
    expect(r.body.errors).toMatchObject({ code: 'TRANSPORT_EXHAUSTED', current: { transport: 'dev', epoch: 2 } });
    expect(await dbRun(runId)).toEqual({ transport: 'dev', transport_epoch: 2, transport_switches: 1 });
  });

  it('후보가 하나뿐인 방은 처음부터 TRANSPORT_EXHAUSTED', async () => {
    const { heroes, runId } = await room('relay');
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1 });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('TRANSPORT_EXHAUSTED');
  });

  it('전환 수가 4에 닿은 방은 후보가 남아 있어도 더 못 옮긴다', async () => {
    const { heroes, runId } = await room('relay,dev');
    await getPool().query('UPDATE party_runs SET transport_switches = 4 WHERE uuid = $1', [runId]);
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1 });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('TRANSPORT_EXHAUSTED');
    expect((await dbRun(runId)).transport).toBe('relay');
  });
});

describe('허용되지 않은 전환', () => {
  it('오래된 세대(epoch)는 409 TRANSPORT_CHANGED 이고 현재 전송을 알려준다', async () => {
    const { heroes, runId } = await room('relay,dev');
    await sw(heroes[0] as Hero, runId, { epoch: 1 });
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1 });
    expect(r.status).toBe(409);
    expect(r.body.errors).toMatchObject({ code: 'TRANSPORT_CHANGED', current: { transport: 'dev', epoch: 2 } });
  });

  it('미래 세대(epoch 가 서버보다 큼)도 TRANSPORT_CHANGED', async () => {
    const { heroes, runId } = await room('relay,dev');
    const r = await sw(heroes[1] as Hero, runId, { epoch: 9 });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('TRANSPORT_CHANGED');
    expect((await dbRun(runId)).transport_epoch).toBe(1);
  });

  it('실패했다고 한 전송이 현재 전송과 다르면 TRANSPORT_CHANGED (되돌아가는 전환 불가)', async () => {
    const { heroes, runId } = await room('relay,dev');
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1, failed: 'dev' });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('TRANSPORT_CHANGED');
    expect((await dbRun(runId)).transport).toBe('relay');
  });

  it('원하는 전송을 직접 지정하는 필드는 받지 않는다(400)', async () => {
    const { heroes, runId } = await room('relay,dev');
    for (const extra of [{ transport: 'dev' }, { to: 'dev' }, { next: 'dev' }]) {
      const r = await sw(heroes[1] as Hero, runId, { epoch: 1, ...extra });
      expect(r.status).toBe(400);
    }
    expect((await dbRun(runId)).transport).toBe('relay');
  });

  it('host_unreachable 은 서버가 보기에 호스트가 살아 있을 때만 받는다', async () => {
    const { heroes, runId } = await room('relay,dev');
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1, reason: 'host_unreachable' });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('HOST_NOT_ALIVE');
    expect((await dbRun(runId)).transport).toBe('relay');
  });

  it('입력 오류: 세대 0·음수·소수·문자열, 알 수 없는 사유·전송 이름, request_id 없음은 400', async () => {
    const { heroes, runId } = await room('relay,dev');
    const h = heroes[1] as Hero;
    for (const body of [{ epoch: 0 }, { epoch: -1 }, { epoch: 1.5 }, { epoch: '1' }, { epoch: 1, reason: 'bored' }, { epoch: 1, failed: 'carrier_pigeon' }, {}]) {
      expect((await sw(h, runId, body)).status).toBe(400);
    }
    expect((await fb(h, runId, { observed_epoch: 1 })).status).toBe(400); // reason 필수
    expect((await fb(h, runId, { epoch: 1, reason: 'connect_failed' })).status).toBe(400); // 형태가 다르다
    expect((await dbRun(runId)).transport_epoch).toBe(1);
  });

  it('방에 없는 사람과 없는 방은 404 ROOM_NOT_FOUND', async () => {
    const { heroes, runId } = await room('relay,dev');
    const stranger = await newHero(app);
    expect((await sw(stranger, runId, { epoch: 1 })).body.errors.code).toBe('ROOM_NOT_FOUND');
    expect((await sw(heroes[1] as Hero, randomUUID(), { epoch: 1 })).body.errors.code).toBe('ROOM_NOT_FOUND');
    expect((await dbRun(runId)).transport_epoch).toBe(1);
  });

  it('끝난 방은 409 ROOM_CLOSED', async () => {
    const { heroes, runId } = await room('relay,dev');
    await getPool().query("UPDATE party_runs SET state = 'cancelled', cancel_reason = 'host_cancel', ended_at = now() WHERE uuid = $1", [runId]);
    const r = await sw(heroes[1] as Hero, runId, { epoch: 1 });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('ROOM_CLOSED');
  });
});

describe('멤버·쿨다운 제한', () => {
  it('한 멤버가 한 방에서 세 번째 전환을 요청하면 429 TRANSPORT_SWITCH_LIMIT (후보가 남았어도)', async () => {
    const { heroes, runId } = await room('relay,dev');
    const a = heroes[1] as Hero;
    expect((await sw(a, runId, { epoch: 1 })).status).toBe(200);
    // 두 번째, 세 번째 시도는 실패해도 시도 수에 센다
    expect((await sw(a, runId, { epoch: 2 })).body.errors.code).toBe('TRANSPORT_EXHAUSTED');
    const third = await sw(a, runId, { epoch: 2 });
    expect(third.status).toBe(429);
    expect(third.body.errors.code).toBe('TRANSPORT_SWITCH_LIMIT');
    // 다른 멤버는 여전히 요청할 수 있다
    expect((await sw(heroes[0] as Hero, runId, { epoch: 2 })).body.errors.code).toBe('TRANSPORT_EXHAUSTED');
  });

  it('쿨다운 안의 두 번째 전환은 429 TRANSPORT_SWITCH_COOLDOWN 과 Retry-After', async () => {
    app = buildApp({ COMBAT_TRANSPORT_ORDER: 'relay,dev', TRANSPORT_SWITCH_COOLDOWN_SECONDS: '30' });
    const a = await newHero(app);
    const b = await newHero(app);
    await formParty(app, a, [b]);
    const runId = (await post(app, a, '/party/start', { ai_count: 0 })).body.data.run.id as string;
    expect((await sw(b, runId, { epoch: 1 })).status).toBe(200);
    const r = await sw(a, runId, { epoch: 2 });
    expect(r.status).toBe(429);
    expect(r.body.errors.code).toBe('TRANSPORT_SWITCH_COOLDOWN');
    expect(Number(r.headers['retry-after'])).toBeGreaterThan(0);
    expect(r.body.errors.retry_after_sec).toBeGreaterThan(0);
  });

  it('끝난 방을 건드리면 그 방의 쿨다운·멤버 카운터 항목이 지워진다', async () => {
    const { heroes, runId } = await room('relay,dev');
    await sw(heroes[1] as Hero, runId, { epoch: 1 });
    expect(switchMapSizes().members).toBeGreaterThan(0);
    await getPool().query("UPDATE party_runs SET state = 'cancelled', cancel_reason = 'host_cancel', ended_at = now() WHERE uuid = $1", [runId]);
    await sw(heroes[1] as Hero, runId, { epoch: 2 });
    expect(switchMapSizes()).toEqual({ cooldowns: 0, members: 0 });
  });
});

describe('재전송과 동시 요청', () => {
  it('같은 request_id 재전송은 한 번만 전환하고 같은 응답을 돌려준다(두 경로 형태 모두)', async () => {
    const { heroes, runId } = await room('relay,dev');
    const rid = randomUUID();
    const a = await fb(heroes[1] as Hero, runId, { observed_epoch: 1, reason: 'connect_timeout' }, rid);
    const b = await fb(heroes[1] as Hero, runId, { observed_epoch: 1, reason: 'connect_timeout' }, rid);
    expect(a.status).toBe(200);
    expect(a.body.data.transport).toMatchObject({ current: 'dev', epoch: 2 });
    expect(b.body).toEqual(a.body);
    expect(await dbRun(runId)).toEqual({ transport: 'dev', transport_epoch: 2, transport_switches: 1 });
  });

  it('동시 요청: 같은 request_id 두 개는 한 번만 전환한다', async () => {
    const { heroes, runId } = await room('relay,dev');
    const rid = randomUUID();
    const [a, b] = await Promise.all([sw(heroes[1] as Hero, runId, { epoch: 1 }, rid), sw(heroes[1] as Hero, runId, { epoch: 1 }, rid)]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect(b.body.data).toEqual(a.body.data);
    expect((await dbRun(runId)).transport_switches).toBe(1);
  });

  it('동시 요청: 서로 다른 멤버가 같은 세대로 요청하면 한 명만 성공(나머지 TRANSPORT_CHANGED)', async () => {
    const { heroes, runId } = await room('relay,dev', 3);
    const rs = await Promise.all((heroes as Hero[]).map((h) => sw(h, runId, { epoch: 1 })));
    expect(rs.filter((r) => r.status === 200)).toHaveLength(1);
    for (const r of rs.filter((x) => x.status !== 200)) expect(r.body.errors.code).toBe('TRANSPORT_CHANGED');
    expect((await dbRun(runId)).transport_epoch).toBe(2);
  });
});

describe('필드 세션', () => {
  it('필드 방도 같은 순서 규칙을 따른다: dev 로 한 칸 옮기고 다음은 TRANSPORT_EXHAUSTED', async () => {
    app = buildApp({ COMBAT_TRANSPORT_ORDER: 'relay,dev', TRANSPORT_SWITCH_COOLDOWN_SECONDS: '0' });
    const a = await newHero(app);
    const b = await newHero(app);
    await formParty(app, a, [b]);
    const e = await post(app, a, '/field-sessions/enter', { map_id: 'forest' });
    expect(e.status).toBe(201);
    const sid = e.body.data.session.id as string;
    expect((await post(app, b, '/field-sessions/enter', { map_id: 'forest' })).status).toBeLessThan(300);
    const r = await post(app, b, `/rooms/field/${sid}/transport`, { epoch: 1, failed: 'relay' });
    expect(r.status).toBe(200);
    expect(r.body.data.transport).toMatchObject({ current: 'dev', epoch: 2 });
    const end = await post(app, a, `/rooms/field/${sid}/transport`, { epoch: 2 });
    expect(end.body.errors.code).toBe('TRANSPORT_EXHAUSTED');
    const stale = await post(app, a, `/rooms/field/${sid}/transport`, { epoch: 1 });
    expect(stale.body.errors.code).toBe('TRANSPORT_CHANGED');
    expect((await get(app, a, `/field-sessions/${sid}`)).body.data.session.transport).toMatchObject({ current: 'dev', epoch: 2 });
  });
});
