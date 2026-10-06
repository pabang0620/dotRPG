// 9단계 4절: 프레즌스(P1, P2), 활동 시간, 기기 동시 접속 제한, IP 감시 점수, 처치 보고의 맵·체류 확인
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { sendPresence } from '../src/domains/antiabuse/presenceService';
import { hmacDevice } from '../src/domains/antiabuse/deviceRecords';
import { sweepPresence } from '../src/domains/antiabuse/presenceService';
import { advance, HOUR, MIN, resetClock, setNowAt } from './auctionHelpers';
import { anomalyKinds, post, type Hero } from './economyHelpers';
import { auth, buildApp, createChar, randomLoginId, randomName, resetDb, shutdown, ver, type Session } from './helpers';

interface Player {
  s: Session;
  id: string;
  dbId: number;
  accountUuid: string;
}

const device = (hash = createHash('sha256').update(randomBytes(8)).digest('hex')) => ({ install_id: randomUUID(), device_hash: hash });
const T0 = '2026-10-07T10:00:05Z';

let app: Express;
beforeEach(async () => {
  await resetDb();
  resetClock();
  setNowAt(new Date(T0));
  app = buildApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

async function player(a: Express, dev?: object, extraChars = 0): Promise<Player & { extra: Player[] }> {
  const loginId = randomLoginId();
  const reg = await request(a).post('/auth/dev/register').set(ver()).send({ login_id: loginId, password: 'password-1234', ...(dev ? { device: dev } : {}) });
  const s: Session = { loginId, password: 'password-1234', accountId: reg.body.data.account.id, access: reg.body.data.access_token, refresh: reg.body.data.refresh_token };
  const make = async (): Promise<Player> => {
    const c = await createChar(a, s, randomName());
    const id = c.body.data.character.id as string;
    const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
    return { s, id, dbId: Number((r.rows[0] as { id: string }).id), accountUuid: s.accountId };
  };
  const first = await make();
  const extra: Player[] = [];
  for (let i = 0; i < extraChars; i++) extra.push(await make());
  return { ...first, extra };
}
const hero = (p: Player): Hero => ({ s: p.s, id: p.id, dbId: p.dbId, cls: 'warrior' });
const beat = (a: Express, p: Player, body: Record<string, unknown> = {}) =>
  request(a).post(`/characters/${p.id}/presence`).set(auth(p.s)).send({ map_id: 'village', auto_play: false, input_recent: true, ...body });
const online = async (p: Player): Promise<Record<string, unknown> | undefined> =>
  (await getPool().query('SELECT * FROM online_sessions WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1)', [p.accountUuid])).rows[0];
const seconds = async (p: Player): Promise<number> =>
  Number(((await getPool().query('SELECT coalesce(sum(active_seconds), 0) AS n FROM play_time_hourly WHERE character_id = $1', [p.dbId])).rows[0] as { n: string }).n);

describe('P1 프레즌스와 활동 시간', () => {
  it('첫 신호가 online_sessions와 login_events(enter)를 만들고, 30초 간격 신호가 활동 시간에 30초를 더한다', async () => {
    const p = await player(app, device());
    const first = await beat(app, p);
    expect(first.status).toBe(200);
    expect(first.body.data).toMatchObject({ interval_seconds: 30, counted: false, device: { online: 1, max: 2 } });
    expect(await online(p)).toMatchObject({ map_id: 'village', ended_at: null });
    const ev = await getPool().query("SELECT kind, character_id::text FROM login_events WHERE kind = 'enter'");
    expect(ev.rows).toEqual([{ kind: 'enter', character_id: String(p.dbId) }]);
    advance(30_000);
    const second = await beat(app, p);
    expect(second.body.data.counted).toBe(true);
    expect(await seconds(p)).toBe(30);
    const row = await getPool().query('SELECT active_seconds, auto_seconds, unattended_seconds, beats FROM play_time_hourly WHERE character_id = $1', [p.dbId]);
    expect(row.rows).toEqual([{ active_seconds: 30, auto_seconds: 0, unattended_seconds: 0, beats: 1 }]);
    // 자동 진행 + 입력 없음은 auto·unattended에도 쌓인다
    advance(30_000);
    await beat(app, p, { auto_play: true, input_recent: false });
    const row2 = await getPool().query('SELECT active_seconds, auto_seconds, unattended_seconds FROM play_time_hourly WHERE character_id = $1', [p.dbId]);
    expect(row2.rows).toEqual([{ active_seconds: 60, auto_seconds: 30, unattended_seconds: 30 }]);
  });

  it('61초 간격은 활동 시간에 더하지 않고 map_since를 초기화한다. 5초보다 촘촘한 신호는 counted=false, 합쳐서 다음에 센다', async () => {
    const p = await player(app, device());
    await beat(app, p);
    advance(61_000);
    const gap = await beat(app, p);
    expect(gap.body.data.counted).toBe(false);
    expect(await seconds(p)).toBe(0);
    const r = await online(p);
    expect(new Date(r?.map_since as string).getTime()).toBe(new Date(r?.last_seen_at as string).getTime());
    advance(3_000);
    expect((await beat(app, p)).body.data.counted).toBe(false);
    advance(3_000);
    // 직전 "센 신호"로부터 6초: 3초 + 3초가 합쳐져 6초로 센다
    const merged = await beat(app, p);
    expect(merged.body.data.counted).toBe(true);
    expect(await seconds(p)).toBe(6);
  });

  it('벽시계보다 많이 쌓을 수 없다: 5초마다 1시간 넘게 신호를 보내도 시간 버킷은 3600초 이하(CHECK), 합계는 경과 시간 이하', async () => {
    const p = await player(app, device());
    const accountId = Number((await getPool().query('SELECT id FROM accounts WHERE uuid = $1', [p.accountUuid])).rows[0].id);
    await sendPresence(accountId, p.id, { map_id: 'village', auto_play: false, input_recent: true }, '127.0.0.1');
    for (let i = 0; i < 1000; i++) {
      advance(5_000);
      await sendPresence(accountId, p.id, { map_id: 'village', auto_play: false, input_recent: true }, '127.0.0.1');
    }
    const rows = await getPool().query('SELECT active_seconds FROM play_time_hourly WHERE character_id = $1', [p.dbId]);
    for (const r of rows.rows) expect(r.active_seconds).toBeLessThanOrEqual(3600);
    expect(await seconds(p)).toBeLessThanOrEqual(1000 * 5);
    expect(rows.rows.length).toBeGreaterThanOrEqual(2);
  });

  it('입력 오류: 알 수 없는 맵 422 INVALID_MAP, 필드 누락·추가 필드 400, 남의 캐릭터 404, 정지 계정 403', async () => {
    const a = await player(app, device());
    const b = await player(app, device());
    const bad = await beat(app, a, { map_id: 'nowhere' });
    expect(bad.status).toBe(422);
    expect(bad.body.errors.code).toBe('INVALID_MAP');
    expect((await request(app).post(`/characters/${a.id}/presence`).set(auth(a.s)).send({ map_id: 'village' })).status).toBe(400);
    expect((await beat(app, a, { gold: 5 })).status).toBe(400);
    expect((await request(app).post(`/characters/${b.id}/presence`).set(auth(a.s)).send({ map_id: 'village', auto_play: false, input_recent: true })).status).toBe(404);
    await getPool().query("UPDATE accounts SET banned_until = now() + interval '1 hour' WHERE uuid = $1", [a.accountUuid]);
    expect((await beat(app, a)).status).toBe(403);
  });

  it('속도 제한: 계정당 10초에 RATE_PRESENCE_PER_10S회까지, 넘으면 429', async () => {
    const limited = buildApp({ RATE_PRESENCE_PER_10S: '2' });
    const p = await player(limited, device());
    expect((await beat(limited, p)).status).toBe(200);
    expect((await beat(limited, p)).status).toBe(200);
    const third = await beat(limited, p);
    expect(third.status).toBe(429);
    expect(third.body.errors.code).toBe('RATE_LIMITED');
  });
});

describe('기기당 동시 접속 (4.3)', () => {
  const sameDevice = async (a: Express, n: number): Promise<Player[]> => {
    const d = device();
    const out: Player[] = [];
    for (let i = 0; i < n; i++) out.push(await player(a, d));
    return out;
  };

  it('enforce: 같은 기기 세 번째 계정은 409 DEVICE_LIMIT, 둘째까지는 통과, 같은 계정이 캐릭터를 바꿔도 기기 수는 늘지 않는다', async () => {
    const strict = buildApp({ DEVICE_LIMIT_MODE: 'enforce' });
    const d = device();
    const a = await player(strict, d, 1);
    const b = await player(strict, d);
    const c = await player(strict, d);
    expect((await beat(strict, a)).body.data.device).toEqual({ online: 1, max: 2 });
    expect((await beat(strict, b)).body.data.device).toEqual({ online: 2, max: 2 });
    const third = await beat(strict, c);
    expect(third.status).toBe(409);
    expect(third.body.errors).toMatchObject({ code: 'DEVICE_LIMIT', limit: 2, online_on_device: 2 });
    // 같은 계정이 다른 캐릭터로 진입: 자기 계정은 세지 않는다
    expect((await beat(strict, a.extra[0] as Player)).status).toBe(200);
    expect(await anomalyKinds(hero(c))).toEqual([]);
  });

  it('log(기본): 세 번째도 통과하고 device_limit 이상 기록만 남는다', async () => {
    const [a, b, c] = (await sameDevice(app, 3)) as [Player, Player, Player];
    for (const p of [a, b, c]) expect((await beat(app, p)).status).toBe(200);
    expect(await anomalyKinds(hero(c))).toEqual(['device_limit']);
    const d = await getPool().query("SELECT detail FROM anomaly_log WHERE kind = 'device_limit'");
    expect(d.rows[0]?.detail).toMatchObject({ device_slot_count: 3 });
  });

  it('남은 마지막 자리를 동시에 노리는 두 계정 중 정확히 하나만 통과한다(같은 기기 advisory lock)', async () => {
    const strict = buildApp({ DEVICE_LIMIT_MODE: 'enforce' });
    const [a, , c, d] = (await sameDevice(strict, 4)) as [Player, Player, Player, Player];
    expect((await beat(strict, a)).status).toBe(200);
    const res = await Promise.all([beat(strict, c), beat(strict, d)]);
    expect(res.map((r) => r.status).sort()).toEqual([200, 409]);
  });

  it('leave 뒤에 즉시 새 계정이 진입할 수 있고, 90초 무신호도 칸을 반환한다', async () => {
    const strict = buildApp({ DEVICE_LIMIT_MODE: 'enforce' });
    const [a, b, c, d] = (await sameDevice(strict, 4)) as [Player, Player, Player, Player];
    await beat(strict, a);
    await beat(strict, b);
    expect((await beat(strict, c)).status).toBe(409);
    const left = await request(strict).post(`/characters/${a.id}/presence/leave`).set(auth(a.s)).send({});
    expect(left.body.data).toEqual({ left: true });
    expect((await online(a))?.end_reason).toBe('leave');
    expect((await beat(strict, c)).status).toBe(200);
    // 멱등: 다시 보내도 성공
    expect((await request(strict).post(`/characters/${a.id}/presence/leave`).set(auth(a.s)).send({})).body.data).toEqual({ left: true });
    // 90초 무신호: b와 c가 신호를 끊으면 d가 들어간다
    expect((await beat(strict, d)).status).toBe(409);
    advance(91_000);
    expect((await beat(strict, d)).status).toBe(200);
    // 청소 작업은 신선도를 잃은 행을 timeout으로 닫는다
    advance(MIN);
    expect(await sweepPresence()).toBeGreaterThanOrEqual(2);
    expect((await online(b))?.end_reason).toBe('timeout');
  });

  it('같은 IP 4개 동시 진입이면 ip_cluster가 시간대당 한 번만 기록되고 거절은 없다', async () => {
    const players: Player[] = [];
    for (let i = 0; i < 5; i++) players.push(await player(app, device()));
    for (const p of players) expect((await beat(app, p)).status).toBe(200);
    const rows = await getPool().query("SELECT detail FROM anomaly_log WHERE kind = 'ip_cluster'");
    expect(rows.rows).toHaveLength(1);
    expect(rows.rows[0]?.detail.online).toBe(4);
    // 다음 시간대에는 다시 한 번(옛 행은 신선도를 잃었으므로 4명이 다시 진입한다)
    advance(HOUR);
    for (const p of players.slice(0, 4)) await beat(app, p);
    expect((await getPool().query("SELECT count(*) AS n FROM anomaly_log WHERE kind = 'ip_cluster'")).rows[0]?.n).toBe('2');
  });

  it('기기를 모르는 세션(device 없음)은 한도를 적용하지 않는다', async () => {
    const strict = buildApp({ DEVICE_LIMIT_MODE: 'enforce' });
    const ps = [await player(strict), await player(strict), await player(strict)];
    for (const p of ps) expect((await beat(strict, p)).status).toBe(200);
    expect(hmacDevice('x')).toHaveLength(64);
  });
});

describe('처치 보고의 프레즌스 확인 (4.5)', () => {
  const kill = (a: Express, p: Player, map: string, monster: string, extra: Record<string, unknown> = {}) =>
    post(a, hero(p), '/kills', { map_id: map, monster_id: monster, ...extra });

  it('enforce: 프레즌스가 없으면 409 PRESENCE_REQUIRED, 있으면 통과, 다른 맵 보고는 422 KILL_REJECTED(kill_presence)', async () => {
    const strict = buildApp({ PRESENCE_KILL_MODE: 'enforce' });
    const p = await player(strict, device());
    const none = await kill(strict, p, 'forest', 'skeleton');
    expect(none.status).toBe(409);
    expect(none.body.errors.code).toBe('PRESENCE_REQUIRED');
    expect(await anomalyKinds(hero(p))).toEqual([]);
    await beat(strict, p, { map_id: 'forest' });
    expect((await kill(strict, p, 'forest', 'skeleton')).status).toBe(200);
    // 프레즌스 맵은 forest인데 forest_ruins 몬스터를 보고(맵 위조)
    advance(2_000);
    const forged = await kill(strict, p, 'forest_ruins', 'skel_warrior');
    expect(forged.status).toBe(422);
    expect(forged.body.errors.code).toBe('KILL_REJECTED');
    expect(await anomalyKinds(hero(p))).toEqual(['kill_presence']);
    // 신호가 120초 넘게 없으면 다시 PRESENCE_REQUIRED
    advance(121_000);
    expect((await kill(strict, p, 'forest', 'skeleton')).body.errors.code).toBe('PRESENCE_REQUIRED');
  });

  it('맵 이동 직후 20초 안의 옛 맵 보고는 통과하고 21초 뒤는 거절한다', async () => {
    const strict = buildApp({ PRESENCE_KILL_MODE: 'enforce' });
    const p = await player(strict, device());
    await beat(strict, p, { map_id: 'forest' });
    advance(10_000);
    await beat(strict, p, { map_id: 'forest_ruins' });
    advance(19_000);
    expect((await kill(strict, p, 'forest', 'skeleton')).status).toBe(200);
    advance(2_000);
    const late = await kill(strict, p, 'forest', 'skeleton');
    expect(late.status).toBe(422);
    expect(late.body.errors.code).toBe('KILL_REJECTED');
    // 새 맵 보고는 계속 통과
    await beat(strict, p, { map_id: 'forest_ruins' });
    expect((await kill(strict, p, 'forest_ruins', 'skel_warrior')).status).toBe(200);
  });

  it('필드 보스: 맵 도착 29초 뒤는 거절, 31초 뒤는 통과', async () => {
    const strict = buildApp({ PRESENCE_KILL_MODE: 'enforce' });
    const p = await player(strict, device());
    await beat(strict, p, { map_id: 'forest_depths' });
    advance(29_000);
    await beat(strict, p, { map_id: 'forest_depths' });
    const early = await kill(strict, p, 'forest_depths', 'fboss_forest');
    expect(early.status).toBe(422);
    expect(early.body.errors.code).toBe('KILL_REJECTED');
    advance(2_000);
    await beat(strict, p, { map_id: 'forest_depths' });
    expect((await kill(strict, p, 'forest_depths', 'fboss_forest')).status).toBe(200);
  });

  it('log 모드(기본)는 어느 경우에도 거절하지 않고 기록만 한다', async () => {
    const p = await player(app, device());
    expect((await kill(app, p, 'forest', 'skeleton')).status).toBe(200);
    await beat(app, p, { map_id: 'forest' });
    advance(2_000);
    expect((await kill(app, p, 'forest_ruins', 'skel_warrior')).status).toBe(200);
    expect(await anomalyKinds(hero(p))).toEqual(['kill_presence']);
    const sev = await getPool().query("SELECT severity FROM anomaly_log WHERE kind = 'kill_presence'");
    expect(sev.rows).toEqual([{ severity: 1 }]);
  });
});
