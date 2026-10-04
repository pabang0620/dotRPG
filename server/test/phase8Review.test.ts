import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { loadConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { MemoryTicketReplayStore, resetSteamStats, steamStats, webApiVerifier } from '../src/domains/auth/steamProvider';
import * as killRepo from '../src/domains/kills/killRepository';
import { MemoryTicketStore } from '../src/domains/relay/relayTicket';
import { switchMapSizes, resetSwitchCooldowns } from '../src/domains/transport/fallbackService';
import { setMaintenance } from '../src/ops/maintenanceState';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { buildApp, resetDb, shutdown, ver } from './helpers';
import { anomalyKinds, fakeRng } from './economyHelpers';
import { adminApp, adminPost, makeAdmin } from './opsHelpers';
import { formParty, newHero, post, raw, type Hero } from './partyHelpers';
import { joinRoom, RelayClient, ticketFor } from './relayHelpers';
import { sleep, startServer, type TestServer } from './wsHelpers';

const ENV = { RELAY_MEMBER_PKT_PER_SEC: '10', RELAY_STRIKES_PER_MIN: '6', RELAY_HOST_GRACE_MS: '300', TRANSPORT_SWITCH_COOLDOWN_SECONDS: '0' };
let app = buildApp(ENV);
let srv: TestServer;
let fixed = new Date('2026-10-05T03:00:00Z');
const advance = (s: number) => {
  fixed = new Date(fixed.getTime() + s * 1000);
};

beforeAll(async () => {
  await resetDb();
  srv = await startServer(app);
});
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  setMaintenance(null);
  await srv.stop();
  await shutdown();
});
beforeEach(() => {
  fixed = new Date('2026-10-05T03:00:00Z');
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
  resetSwitchCooldowns();
});

const enter = (h: Hero, rid?: string) => post(app, h, '/field-sessions/enter', { map_id: 'forest' }, rid);
const hb = (h: Hero, sid: string, synced = true) => raw(app, h, `/field-sessions/${sid}/heartbeat`, { seen_epoch: 1, synced });
const kill = (h: Hero, sid: string, ref: number, over: Record<string, unknown> = {}) =>
  post(app, h, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: sid, monster_ref: ref, ...over });
const observe = (h: Hero, sid: string, credits: { seat: number; kills: number }[], windowMs = 10_000) =>
  post(app, h, `/field-sessions/${sid}/observe`, { host_epoch: 1, window_ms: windowMs, credits });

async function team(n = 2, sync = true) {
  const all: Hero[] = [];
  for (let i = 0; i < n; i++) all.push(await newHero(app));
  await formParty(app, all[0] as Hero, all.slice(1));
  const sid = (await enter(all[0] as Hero)).body.data.session.id as string;
  for (const h of all.slice(1)) await enter(h);
  if (sync) for (const h of all) await hb(h, sid, true);
  return { L: all[0] as Hero, M: all.slice(1), all, sid };
}

describe('경제 E1~E7', () => {
  it('E1: 활성 세션 멤버가 같은 맵에서 session_id 없이 보고하면 409 FIELD_SESSION_REQUIRED, 세션을 떠나면 솔로 보고가 된다', async () => {
    const { L, sid } = await team(2);
    advance(1);
    const r = await post(app, L, '/kills', { map_id: 'forest', monster_id: 'skeleton' });
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('FIELD_SESSION_REQUIRED');
    await post(app, L, `/field-sessions/${sid}/leave`, {});
    advance(1);
    expect((await post(app, L, '/kills', { map_id: 'forest', monster_id: 'skeleton' })).status).toBe(200);
  });

  it('E2: 화력 상한은 playing 멤버만 합친다(보고자 본인 포함), 개인 상한은 자기 attack_cap x (1 + 알파)', async () => {
    const { L, M, sid } = await team(2, false); // 하트비트 없음: 둘 다 joined
    const m = M[0] as Hero;
    // 한도가 눈에 띄도록 상한을 낮춘다. 상대가 joined 면 합에 들지 않는다
    const SET = 'UPDATE field_session_members SET attack_cap = 0.5 WHERE';
    await getPool().query(`${SET} session_id = (SELECT id FROM field_sessions WHERE uuid = $1)`, [sid]);
    const eco = (await import('../src/gamedata/loader')).getGameData().economy;
    const fits = (cap: number, n: number) => {
      const dps = (cap / eco.player.attackCooldown) * 2;
      return n * 30 <= dps * 10 * 5 + 30;
    };
    let max = 0;
    while (fits(0.5 * 1.15, max + 1)) max++; // 합산 대상이 보고자뿐일 때(상대가 joined)
    let maxBoth = 0;
    while (fits(Math.min(1.0 * 1.15, 0.5 * 2), maxBoth + 1)) maxBoth++; // 상대도 playing 이면 min(1.15, 개인 1.0)
    expect(max).toBeLessThan(maxBoth);
    // 지금은 상대가 joined: max 마리까지만
    let ok = 0;
    for (let i = 0; i < 20; i++) {
      advance(0.2);
      const r = await kill(m, sid, 100 + i);
      if (r.status !== 200) break;
      ok++;
    }
    expect(ok).toBe(max);
    expect(await anomalyKinds(m)).toContain('kill_power');
    // 상대가 playing 이 되고(하트비트가 attack_cap 을 다시 계산하므로 직접 고정) 창이 지나면 더 받는다
    await hb(L, sid, true);
    await getPool().query(`${SET} session_id = (SELECT id FROM field_sessions WHERE uuid = $1)`, [sid]);
    advance(31);
    let ok2 = 0;
    for (let i = 0; i < 20; i++) {
      advance(0.2);
      const r = await kill(m, sid, 200 + i);
      if (r.status !== 200) break;
      ok2++;
    }
    expect(ok2).toBe(maxBoth);
  });

  it('E3: observe 창은 서버가 정한다(요청 window_ms 와 마지막 관찰 이후 실제 경과 중 작은 값)', async () => {
    const { L, M, sid } = await team(2);
    // 시계가 흐르지 않았는데 30초 창을 주장해도 경과 0: 공급 한도는 17(+1창)이라 20마리는 거절
    const big = await observe(L, sid, [{ seat: 1, kills: 20 }], 30_000);
    expect(big.status).toBe(200);
    const row = async () => Number((await getPool().query('SELECT kills_credited AS n FROM field_session_members WHERE character_id = $1', [(M[0] as Hero).dbId])).rows[0].n);
    expect(await row()).toBe(0);
    expect(await anomalyKinds(L)).toContain('field_host');
    const detail = (await getPool().query("SELECT detail FROM anomaly_log WHERE kind = 'field_host' AND character_id = $1 ORDER BY id DESC LIMIT 1", [L.dbId])).rows[0].detail;
    expect(detail).toMatchObject({ window_used_ms: 0, window_ms: 30_000 });
    // 실제로 10초가 지났으면 같은 주장도 통과(경과 10초 -> 17 x (10/25 + 1) = 24)
    advance(10);
    await observe(L, sid, [{ seat: 1, kills: 20 }], 30_000);
    expect(await row()).toBe(8); // 받아들인 처치 0 + 크레딧 여유 8
  });

  it('E4: session_id 가 있으면 monster_ref 가 필요하다(400)', async () => {
    const { L, sid } = await team(2);
    const r = await post(app, L, '/kills', { map_id: 'forest', monster_id: 'skeleton', session_id: sid });
    expect(r.status).toBe(400);
    expect(JSON.stringify(r.body.errors.fields)).toContain('monster_ref');
  });

  it('E5: 다인 세션에서 호스트 관찰이 3분 넘게 없으면 경험치 배율이 0.5로 내려가고 field_host(2)를 한 번 남긴다. 관찰이 오면 풀린다', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    advance(60);
    const early = await kill(m, sid, 1);
    expect(early.body.data.field.xp_factor).toBe(1);
    advance(150); // 마지막 활동(세션 생성) 후 3분 넘음
    const a = await kill(m, sid, 2);
    expect(a.body.data).toMatchObject({ granted_xp: 10, field: { xp_factor: 0.5 } });
    advance(1);
    await kill(m, sid, 3);
    const n = await getPool().query("SELECT count(*) AS n FROM anomaly_log WHERE kind = 'field_host' AND severity = 2 AND detail->>'why' = 'observe_lapsed' AND detail->>'session_id' = $1", [sid]);
    expect(n.rows[0].n).toBe('1');
    advance(1);
    await observe(L, sid, [{ seat: 1, kills: 3 }], 30_000);
    advance(1);
    expect((await kill(m, sid, 4)).body.data.field.xp_factor).toBe(1);
  });

  it('E6: kill_log_field_ref_uq 위반(확인을 뚫은 중복)은 500이 아니라 409 KILL_DUPLICATE', async () => {
    const { M, sid } = await team(2);
    const m = M[0] as Hero;
    advance(2);
    expect((await kill(m, sid, 9)).status).toBe(200);
    const spy = jest.spyOn(killRepo, 'fieldRefExists').mockResolvedValue(false);
    advance(2);
    const dup = await kill(m, sid, 9);
    spy.mockRestore();
    expect(dup.status).toBe(409);
    expect(dup.body.errors.code).toBe('KILL_DUPLICATE');
  });

  it('E6: 같은 파티·맵의 첫 입장 두 개가 동시에 와도 세션은 하나이고 둘 다 성공한다', async () => {
    const [a, b] = [await newHero(app), await newHero(app)];
    await formParty(app, a, [b]);
    const [x, y] = await Promise.all([enter(a), enter(b)]);
    expect([x.status, y.status].sort()).toEqual([200, 201]);
    expect(x.body.data.session.id).toBe(y.body.data.session.id);
    const n = await getPool().query('SELECT count(*) AS n FROM field_sessions WHERE uuid = $1', [x.body.data.session.id]);
    expect(n.rows[0].n).toBe('1');
  });

  it('E7: 세션을 닫았다 다시 열어도(10분 안) 직전 세션의 기여 부채가 이어진다', async () => {
    const { L, M, sid } = await team(2);
    const m = M[0] as Hero;
    for (let i = 0; i < 5; i++) {
      advance(1);
      expect((await kill(m, sid, 50 + i)).status).toBe(200);
    }
    await post(app, L, `/field-sessions/${sid}/leave`, {});
    await post(app, m, `/field-sessions/${sid}/leave`, {});
    advance(60);
    const s2 = (await enter(L)).body.data.session.id as string;
    expect(s2).not.toBe(sid);
    await enter(m);
    const debt = async (h: Hero) => Number((await getPool().query('SELECT kills_accepted - kills_credited AS d FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 AND m.character_id = $2', [s2, h.dbId])).rows[0].d);
    expect(await debt(m)).toBe(5);
    expect(await debt(L)).toBe(0);
    // 10분이 지난 뒤의 새 세션은 이어지지 않는다
    await post(app, L, `/field-sessions/${s2}/leave`, {});
    await post(app, m, `/field-sessions/${s2}/leave`, {});
    advance(11 * 60);
    const s3 = (await enter(L)).body.data.session.id as string;
    await enter(m);
    const d3 = await getPool().query('SELECT kills_accepted AS d FROM field_session_members m JOIN field_sessions s ON s.id = m.session_id WHERE s.uuid = $1 AND m.character_id = $2', [s3, m.dbId]);
    expect(d3.rows[0].d).toBe(0);
  });
});

async function runRoom(n = 2) {
  const heroes: Hero[] = [];
  for (let i = 0; i < n; i++) heroes.push(await newHero(app));
  await formParty(app, heroes[0] as Hero, heroes.slice(1));
  const { startAndBegin } = await import('./partyHelpers');
  const { runId } = await startAndBegin(app, heroes[0] as Hero, heroes.slice(1));
  return { heroes, runId };
}

describe('보안 S1~S12', () => {
  it('S1: 관리자 kick(PL6)은 /ws 접속이 없어도 전투 중계 연결을 끊는다(4011)', async () => {
    const { heroes, runId } = await runRoom(2);
    const c = await joinRoom(app, srv.port, heroes[1] as Hero, 'run', runId);
    setClockOverride(null); // 관리자 세션은 실제 시각 기준이다
    const admin = await makeAdmin('operator');
    const k = await adminPost(adminApp(), admin, `/admin/accounts/${(heroes[1] as Hero).s.accountId}/kick`);
    expect(k.status).toBe(200);
    expect(await c.c.waitClose()).toBe(4011);
  });

  it('S2: PING도 속도 한도를 받는다(초과분은 PONG 없음, 반복하면 4006)', async () => {
    const { heroes, runId } = await runRoom(2);
    const c = await joinRoom(app, srv.port, heroes[1] as Hero, 'run', runId);
    for (let i = 0; i < 60; i++) c.c.ping(i);
    expect(await c.c.waitClose()).toBe(4006);
    expect(c.c.pongs.length).toBeLessThan(60);
    expect(c.c.pongs.length).toBeGreaterThan(0);
  });

  it('S3: 티켓 수명은 120초까지만 설정할 수 있고, 소비 기록 보관은 수명 + 여유를 따른다', () => {
    const base = { DATABASE_URL: 'x', MIN_CLIENT_VERSION: '0.1.0', JWT_SECRET: 'a'.repeat(40) };
    expect(() => loadConfig({ ...base, RELAY_TICKET_TTL_SECONDS: '121' })).toThrow(/RELAY_TICKET_TTL_SECONDS/);
    expect(loadConfig({ ...base, RELAY_TICKET_TTL_SECONDS: '120' }).relay.ticketTtlSeconds).toBe(120);
    const s = new MemoryTicketStore(); // 기본 60초 + 60초 = 120초 보관
    expect(s.consume('j', 0)).toBe(true);
    expect(s.consume('j', 119_000)).toBe(false);
    expect(s.consume('j', 121_000)).toBe(true);
  });

  describe('Steam(S4, S9)', () => {
    let spy: jest.SpyInstance;
    beforeEach(() => {
      buildApp({ STEAM_AUTH_MODE: 'web_api', STEAM_APP_ID: '480', STEAM_WEB_API_KEY: 'k', DEPLOY_STAGE: 'test', STEAM_WEB_API_RETRIES: '0' });
      resetSteamStats();
      spy = jest.spyOn(globalThis, 'fetch');
    });
    afterEach(() => {
      spy.mockRestore();
      app = buildApp(ENV);
    });
    const res = (status: number) => Promise.resolve(new Response('{}', { status }));

    it('S4: 400은 무효 티켓(401)이고 회로 차단·설정 오류에 세지 않는다. 401/403 과 5xx 만 센다', async () => {
      spy.mockImplementation(() => res(400));
      for (let i = 0; i < 6; i++) await expect(webApiVerifier.verify('ab')).rejects.toMatchObject({ status: 401, code: 'STEAM_TICKET_INVALID' });
      expect(steamStats()).toMatchObject({ breaker_open: false, misconfigured_recent: false });
      spy.mockImplementation(() => res(403));
      for (let i = 0; i < 5; i++) await expect(webApiVerifier.verify('ab')).rejects.toMatchObject({ status: 503 });
      expect(steamStats()).toMatchObject({ breaker_open: true, misconfigured_recent: true });
    });

    it('S4: POST /auth/steam 의 티켓 길이 상한은 2048', async () => {
      const a = buildApp({ STEAM_AUTH_MODE: 'mock' });
      const r = await request(a).post('/auth/steam').set(ver()).send({ ticket: 'ab'.repeat(1100) });
      expect(r.status).toBe(400);
    });

    it('S9: 티켓 재사용 방지 저장소는 만료된 앞쪽만 지우고 첫 미만료 항목에서 멈춘다', () => {
      const s = new MemoryTicketReplayStore();
      expect(s.consume('a', 0)).toBe(true);
      expect(s.consume('b', 300_000)).toBe(true);
      expect(s.consume('c', 700_000)).toBe(true); // a(0)는 만료, b(300s)는 아직
      expect(s.consume('a', 700_001)).toBe(true);
      expect(s.consume('b', 700_002)).toBe(false);
    });
  });

  it('S7: 한 멤버는 한 방에서 전환을 2번까지만 요청할 수 있다(429 TRANSPORT_SWITCH_LIMIT)', async () => {
    const a = buildApp({ ...ENV, COMBAT_TRANSPORT_ORDER: 'relay,dev,steam' });
    const { heroes, runId } = await (async () => {
      const hs = [await newHero(a), await newHero(a)];
      await formParty(a, hs[0] as Hero, [hs[1] as Hero]);
      const st = await post(a, hs[0] as Hero, '/party/start', { ai_count: 0 });
      return { heroes: hs, runId: st.body.data.run.id as string };
    })();
    const t2 = (h: Hero, epoch: number) => post(a, h, `/rooms/run/${runId}/transport`, { epoch });
    // 이 구성에서 steam 은 자격이 없어 순서가 [relay, dev]: 전환은 1번만 가능
    expect((await post(a, heroes[1] as Hero, `/rooms/run/${runId}/transport`, { epoch: 1 })).status).toBe(200);
    expect((await t2(heroes[1] as Hero, 2)).body.errors.code).toBe('TRANSPORT_EXHAUSTED');
    expect((await t2(heroes[1] as Hero, 2)).body.errors.code).toBe('TRANSPORT_SWITCH_LIMIT');
    // 다른 멤버는 자기 몫이 있다
    expect((await t2(heroes[0] as Hero, 2)).body.errors.code).toBe('TRANSPORT_EXHAUSTED');
    app = buildApp(ENV);
  });

  it('S8: 쿨다운 맵은 쿨다운이 지난 항목과 닫힌 방의 항목을 정리한다', async () => {
    const a = buildApp({ ...ENV, COMBAT_TRANSPORT_ORDER: 'relay,dev', TRANSPORT_SWITCH_COOLDOWN_SECONDS: '1' });
    const mk = async () => {
      const hs = [await newHero(a), await newHero(a)];
      await formParty(a, hs[0] as Hero, [hs[1] as Hero]);
      const st = await post(a, hs[0] as Hero, '/party/start', { ai_count: 0 });
      return { h: hs[1] as Hero, id: st.body.data.run.id as string };
    };
    const r1 = await mk();
    expect((await post(a, r1.h, `/rooms/run/${r1.id}/transport`, { epoch: 1 })).status).toBe(200);
    expect(switchMapSizes().cooldowns).toBe(1);
    await sleep(1100);
    const r2 = await mk();
    expect((await post(a, r2.h, `/rooms/run/${r2.id}/transport`, { epoch: 1 })).status).toBe(200);
    expect(switchMapSizes().cooldowns).toBe(1); // r1 항목은 쿨다운이 지나 지워졌다
    // 방이 끝나면 그 방의 멤버 카운터도 지워진다
    await getPool().query("UPDATE party_runs SET state = 'ended', begun_at = now(), ended_at = now() WHERE uuid = $1", [r2.id]);
    const before = switchMapSizes().members;
    expect((await post(a, r2.h, `/rooms/run/${r2.id}/transport`, { epoch: 2 })).body.errors.code).toBe('ROOM_CLOSED');
    expect(switchMapSizes().members).toBe(before - 1);
    app = buildApp(ENV);
  });

  it('S10: hello 전의 leave 도 형식 오류로 세어 3번이면 4005로 끊는다', async () => {
    const c = await RelayClient.open(srv.port);
    for (let i = 0; i < 3; i++) c.sendText({ t: 'leave' });
    expect(await c.waitClose()).toBe(4005);
  });

  it('S11: 점검·종료 중 hello 는 티켓을 소비하지 않는다(풀린 뒤 같은 티켓이 유효)', async () => {
    const { heroes, runId } = await runRoom(2);
    const h = heroes[1] as Hero;
    const t = await ticketFor(app, h, 'run', runId);
    const c = await RelayClient.open(srv.port);
    const now = fixed.getTime(); // 서버 시계(시험 시계)가 기준이다
    setMaintenance({ uuid: randomUUID(), notice: '', blockLoginAt: new Date(now - 60_000), startsAt: new Date(now - 1000), endsAt: new Date(now + 600_000) });
    c.hello(t.body.data.ticket);
    expect(await c.waitClose()).toBe(4016);
    setMaintenance(null);
    const again = await RelayClient.open(srv.port);
    again.hello(t.body.data.ticket);
    expect((await again.waitT('ready')).t).toBe('ready');
    again.close();
  });

  it('S12: .env.example 에 DEPLOY_STAGE=test 의 480 허용 설명이 있다', async () => {
    const fs = await import('node:fs');
    expect(fs.readFileSync('.env.example', 'utf8')).toMatch(/DEPLOY_STAGE=test 에서 STEAM_APP_ID=480/);
  });
});

