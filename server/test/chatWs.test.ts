import { randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { getLimiterStore } from '../src/domains/chat/limiterStore';
import { hasObfuscatedBannedWord, maskBannedWords } from '../src/utils/bannedWords';
import { getGameData } from '../src/gamedata/loader';
import { newHero, type Hero } from './economyHelpers';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { chat, connect, helloFrame, sleep, startServer, until, WsClient, type TestServer } from './wsHelpers';

let app: Express;
let srv: TestServer;
const open: WsClient[] = [];

beforeAll(async () => {
  app = buildApp({ WS_HELLO_TIMEOUT_MS: '300', CHAT_FILTER_STRIKES: '3' });
  srv = await startServer(app);
});
beforeEach(async () => {
  await resetDb();
  getLimiterStore().clear();
  // 간격 규칙은 빠르게 돌려 보려고 줄여 두고, 간격 테스트가 직접 되돌린다
  getGameData().chat.minIntervalSeconds = 0.05;
});
afterEach(async () => {
  for (const c of open.splice(0)) c.close();
  await until(() => registry.size() === 0);
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

const join = async (h: Hero, o: Parameters<typeof connect>[2] = {}) => {
  const r = await connect(srv.port, h, o);
  open.push(r.c);
  return r;
};
const heroes = async (n: number): Promise<Hero[]> => {
  const out: Hero[] = [];
  for (let i = 0; i < n; i++) out.push(await newHero(app));
  return out;
};
const rest = {
  post: (h: Hero, path: string, body: Record<string, unknown> = {}) => request(app).post(path).set(auth(h.s)).send(body),
  put: (h: Hero, path: string) => request(app).put(path).set(auth(h.s)),
};
const lineCount = async (): Promise<number> =>
  Number(((await getPool().query('SELECT count(*) AS n FROM chat_messages')).rows[0] as { n: string }).n);

describe('인증과 연결', () => {
  it('정상: hello -> ready -> backlog, 시계·제한값을 알려 준다', async () => {
    const [a] = await heroes(1);
    const { ready, backlog } = await join(a as Hero);
    expect(ready.limits).toMatchObject({ max_length: getGameData().chat.maxLength, resend_seconds: 30, party_poll_seconds: 15 });
    expect((ready.character as { id: string }).id).toBe((a as Hero).id);
    expect(ready.shard).toBe(1);
    expect(backlog.lines).toEqual([]);
    const last = await getPool().query('SELECT last_character_id FROM accounts WHERE id = (SELECT account_id FROM characters WHERE uuid = $1)', [(a as Hero).id]);
    expect((last.rows[0] as { last_character_id: string | null }).last_character_id).not.toBeNull();
  });

  it('인증 실패: 토큰 무효 4004, 남의 캐릭터 4010, 구버전 4426, hello 없이 프레임 보내면 거절, hello 시간 초과 4007', async () => {
    const [a, b] = await heroes(2);
    const A = a as Hero;
    const bad = await WsClient.open(srv.port);
    bad.send(helloFrame(A, { token: 'garbage' }));
    expect(await bad.waitClose()).toBe(4004);

    const wrongChar = await WsClient.open(srv.port);
    wrongChar.send(helloFrame(A, { characterId: (b as Hero).id }));
    expect(await wrongChar.waitClose()).toBe(4010);

    const old = await WsClient.open(srv.port);
    old.send(helloFrame(A, { version: '0.0.1' }));
    expect((await old.waitT('error')).code).toBe('CLIENT_OUTDATED');
    expect(await old.waitClose()).toBe(4426);

    const noHello = await WsClient.open(srv.port);
    noHello.send({ t: 'ping', n: 1 });
    expect((await noHello.waitT('error')).code).toBe('NOT_AUTHENTICATED');
    expect(await noHello.waitClose(2000)).toBe(4007);

    const silent = await WsClient.open(srv.port);
    expect(await silent.waitClose(2000)).toBe(4007);
    // /ws가 아닌 경로의 업그레이드는 거절
    await expect(WsClient.open(srv.port, '/other')).rejects.toBeDefined();
  });

  it('같은 계정이 다시 접속하면 먼저 있던 연결은 4001로 끊긴다, ping은 pong', async () => {
    const [a] = await heroes(1);
    const first = await join(a as Hero);
    first.c.send({ t: 'ping', n: 7 });
    expect((await first.c.waitT('pong')).n).toBe(7);
    await join(a as Hero);
    expect((await first.c.waitT('bye')).code).toBe(4001);
    expect(await first.c.waitClose()).toBe(4001);
  });

  it('잘못된 프레임과 프레임 폭주', async () => {
    const [a] = await heroes(1);
    const { c } = await join(a as Hero);
    c.send({ t: 'chat.send', cid: 'x', channel: 'general', text: 'hi' });
    expect((await c.waitT('error')).code).toBe('BAD_FRAME');
    c.ws.send('not json');
    expect((await c.waitT('error')).code).toBe('BAD_FRAME');
    const [b] = await heroes(1);
    const flood = (await join(b as Hero)).c;
    for (let i = 0; i < 15; i++) flood.send({ t: 'ping', n: i });
    expect(await flood.waitClose()).toBe(4006);
  });
});

describe('일반 채팅과 따라잡기', () => {
  it('일반 채팅: 받는 사람은 줄을, 보낸 사람은 ack만 받는다. 길이·빈 문장 오류', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    const msg = chat('general', '  안녕   하세요 ');
    A.c.send(msg);
    const ack = await A.c.waitT('chat.ack');
    expect(ack).toMatchObject({ cid: msg.cid, text: '안녕 하세요', filtered: false, replay: false });
    const got = await B.c.waitT('chat.msg');
    expect(got).toMatchObject({ seq: ack.seq, channel: 'general', text: '안녕 하세요' });
    expect(got.from).toMatchObject({ id: (a as Hero).id });
    await A.c.expectNone((f) => f.t === 'chat.msg');
    A.c.send(chat('general', '   '));
    expect((await A.c.waitT('chat.err')).code).toBe('TEXT_EMPTY');
    A.c.send(chat('general', '가'.repeat(getGameData().chat.maxLength + 1)));
    expect((await A.c.waitT('chat.err')).code).toBe('TEXT_TOO_LONG');
    A.c.send(chat('shout', '안녕'));
    expect((await A.c.waitT('chat.err')).code).toBe('CHANNEL_INVALID');
  });

  it('재접속 따라잡기: since 이후의 줄만 backlog로 오고, 내 줄과 중복은 없다', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    A.c.send(chat('general', '하나'));
    await A.c.waitT('chat.ack');
    const seq1 = ((await B.c.waitT('chat.msg')).seq as number);
    B.c.close();
    await until(() => registry.size() === 1);
    for (const t of ['둘', '셋']) {
      await sleep(70);
      A.c.send(chat('general', t));
      await A.c.waitT('chat.ack');
    }
    await sleep(70);
    const again = await join(b as Hero, { since: seq1 });
    expect((again.backlog.lines as { text: string }[]).map((l) => l.text)).toEqual(['둘', '셋']);
    expect(again.backlog.gap).toBe(false);
    expect(again.backlog.cursor).toBeGreaterThan(seq1);
    // 이어서 오는 새 줄은 중복 없이 도착한다
    A.c.send(chat('general', '넷'));
    expect((await again.c.waitT('chat.msg')).text).toBe('넷');
    await again.c.expectNone((f) => f.t === 'chat.msg' && f.text === '둘');
  });

  it('같은 cid 재전송은 새 줄을 만들지 않고 replay ack를 준다(한도를 쓰지 않는다)', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    const msg = chat('general', '한 번만');
    A.c.send(msg);
    const first = await A.c.waitT('chat.ack');
    await B.c.waitT('chat.msg');
    A.c.send(msg);
    const second = await A.c.waitT('chat.ack');
    expect(second).toMatchObject({ cid: msg.cid, seq: first.seq, replay: true });
    expect(await lineCount()).toBe(1);
    await B.c.expectNone((f) => f.t === 'chat.msg');
    // 동시에 같은 cid 두 개: 한 줄만 저장된다
    const dup = chat('general', '동시');
    await sleep(70);
    A.c.send(dup);
    A.c.send(dup);
    await A.c.waitFor((f) => f.t === 'chat.ack' && f.cid === dup.cid);
    await A.c.waitFor((f) => f.t === 'chat.ack' && f.cid === dup.cid);
    expect(await lineCount()).toBe(2);
  });
});

describe('속도 제한과 반복', () => {
  it('최소 간격: 너무 빠르면 RATE_LIMITED와 retry_after_ms', async () => {
    getGameData().chat.minIntervalSeconds = 1;
    const [a] = await heroes(1);
    const { c } = await join(a as Hero);
    c.send(chat('general', '첫째'));
    await c.waitT('chat.ack');
    c.send(chat('general', '둘째'));
    const err = await c.waitT('chat.err');
    expect(err.code).toBe('RATE_LIMITED');
    expect(err.retry_after_ms as number).toBeGreaterThan(0);
    // 거절된 전송은 한도를 쓰지 않으므로 기다리면 다시 보낼 수 있다
    await sleep(850);
    c.send(chat('general', '둘째'));
    await c.waitT('chat.ack');
  });

  it('같은 말 repeatLimit번째는 보내고 이후 muteSeconds 동안 MUTED_REPEAT, 재접속해도 유지', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    const limit = getGameData().chat.repeatLimit;
    for (let i = 0; i < limit; i++) {
      A.c.send(chat('general', '도배'));
      await A.c.waitT('chat.ack');
      await B.c.waitT('chat.msg');
      await sleep(70);
    }
    A.c.send(chat('general', '다른 말'));
    const err = await A.c.waitT('chat.err');
    expect(err.code).toBe('MUTED_REPEAT');
    expect(typeof err.muted_until).toBe('string');
    A.c.close();
    await until(() => registry.size() === 1);
    const again = await join(a as Hero);
    expect(again.ready.mute).toMatchObject({ source: 'repeat' });
    again.c.send(chat('general', '다시'));
    expect((await again.c.waitT('chat.err')).code).toBe('MUTED_REPEAT');
  });
});

describe('차단 필터와 귓속말', () => {
  it('차단한 계정의 일반 채팅은 오지 않고, 따라잡기에도 없다', async () => {
    const [a, b, c] = await heroes(3);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    const C = await join(c as Hero);
    expect((await rest.put(b as Hero, `/blocks/${(a as Hero).id}`)).status).toBe(201);
    A.c.send(chat('general', 'A의 말'));
    await A.c.waitT('chat.ack');
    expect((await C.c.waitT('chat.msg')).text).toBe('A의 말');
    await B.c.expectNone((f) => f.t === 'chat.msg');
    // 차단 해제 전에 재접속해도 따라잡기에서 빠진다
    B.c.close();
    await until(() => registry.size() === 2);
    const again = await join(b as Hero, { since: 0 });
    expect(again.backlog.lines).toEqual([]);
    // C의 말은 B에게 보인다
    C.c.send(chat('general', 'C의 말'));
    expect((await again.c.waitT('chat.msg')).text).toBe('C의 말');
  });

  it('귓속말: 이름으로 보내면 접속 중인 그 캐릭터만 받고 저장된다', async () => {
    const [a, b, c] = await heroes(3);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    const C = await join(c as Hero);
    const name = (await getPool().query<{ name: string }>('SELECT name FROM characters WHERE uuid = $1', [(b as Hero).id])).rows[0]?.name as string;
    A.c.send(chat('whisper', '비밀이야', { to_name: name.toUpperCase() }));
    const ack = await A.c.waitT('chat.ack');
    const got = await B.c.waitT('chat.msg');
    expect(got).toMatchObject({ seq: ack.seq, channel: 'whisper', text: '비밀이야' });
    expect(got.to).toMatchObject({ id: (b as Hero).id });
    await C.c.expectNone((f) => f.t === 'chat.msg');
    // id로도 보낼 수 있고, 일반 채널에 to를 붙이면 BAD_FRAME
    B.c.send(chat('whisper', '답장', { to: (a as Hero).id }));
    expect((await A.c.waitT('chat.msg')).text).toBe('답장');
    await sleep(70);
    A.c.send(chat('general', '공개', { to: (b as Hero).id }));
    expect((await A.c.waitT('error')).code).toBe('BAD_FRAME');
  });

  it('귓속말 오류: 없는 사람, 접속 중이 아님, 자기 자신, 상대 없음, 내가 차단한 사람', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const err = async (extra: Record<string, unknown>) => {
      await sleep(70);
      A.c.send(chat('whisper', '안녕', extra));
      return (await A.c.waitT('chat.err')).code;
    };
    expect(await err({ to_name: '없는사람' })).toBe('TARGET_NOT_FOUND');
    expect(await err({ to: (b as Hero).id })).toBe('TARGET_OFFLINE');
    expect(await err({ to: (a as Hero).id })).toBe('SELF_WHISPER');
    await sleep(70);
    A.c.send(chat('whisper', '안녕'));
    expect((await A.c.waitT('chat.err')).code).toBe('TARGET_REQUIRED');
    await join(b as Hero);
    await rest.put(a as Hero, `/blocks/${(b as Hero).id}`);
    expect(await err({ to: (b as Hero).id })).toBe('TARGET_BLOCKED');
  });

  it('나를 차단한 사람에게 보낸 귓속말은 오류 없이 ack만 받고, 저장도 전달도 되지 않는다', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    await rest.put(b as Hero, `/blocks/${(a as Hero).id}`);
    A.c.send(chat('whisper', '들리니', { to: (b as Hero).id }));
    expect((await A.c.waitT('chat.ack')).replay).toBe(false);
    await B.c.expectNone((f) => f.t === 'chat.msg');
    expect(await lineCount()).toBe(0);
  });
});

describe('금칙어와 자동 제재', () => {
  it('금칙어는 *로 가려 보내고, 숨기려는 변형은 거절한다', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    A.c.send(chat('general', '너 시발 뭐야'));
    const ack = await A.c.waitT('chat.ack');
    expect(ack).toMatchObject({ text: '너 ** 뭐야', filtered: true });
    expect((await B.c.waitT('chat.msg')).text).toBe('너 ** 뭐야');
    await sleep(70);
    A.c.send(chat('general', '시 발'));
    expect((await A.c.waitT('chat.err')).code).toBe('MESSAGE_BLOCKED');
    expect(await lineCount()).toBe(1);
  });

  it('금칙어를 기준 횟수만큼 쓰면 자동 채팅 금지(MUTED_SANCTION)와 sanction 알림', async () => {
    const [a] = await heroes(1);
    const { c } = await join(a as Hero);
    for (let i = 0; i < 3; i++) {
      await sleep(70);
      c.send(chat('general', `병신${i}`));
      await c.waitFor((f) => f.t === 'chat.ack');
    }
    const sanction = await c.waitT('sanction');
    expect(sanction).toMatchObject({ kind: 'chat_mute', reason_code: 'filter_strikes' });
    await sleep(70);
    c.send(chat('general', '이제 말할래'));
    const err = await c.waitT('chat.err');
    expect(err.code).toBe('MUTED_SANCTION');
    const rows = await getPool().query("SELECT source, created_by FROM account_sanctions WHERE kind = 'chat_mute'");
    expect(rows.rows).toEqual([{ source: 'auto_filter', created_by: 'system' }]);
  });

  it('운영자가 SQL로 제재를 넣으면 접속 중인 소켓에 바로 반영되고, ban이면 끊긴다', async () => {
    const [a, b] = await heroes(2);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    const insert = (acc: Hero, kind: string, ends: string | null) =>
      getPool().query(
        `INSERT INTO account_sanctions (account_id, kind, source, reason_code, ends_at, created_by)
         SELECT account_id, $2, 'admin', 'abuse', ${ends ?? 'NULL'}, 'tester' FROM characters WHERE uuid = $1`,
        [acc.id, kind],
      );
    await insert(a as Hero, 'chat_mute', "now() + interval '1 hour'");
    await A.c.waitT('sanction');
    A.c.send(chat('general', '안녕'));
    expect((await A.c.waitT('chat.err')).code).toBe('MUTED_SANCTION');
    await insert(b as Hero, 'ban', null);
    await getPool().query("UPDATE accounts SET banned_until = now() + interval '1 day' WHERE id = (SELECT account_id FROM characters WHERE uuid = $1)", [(b as Hero).id]);
    expect((await B.c.waitT('sanction')).kind).toBe('ban');
    expect(await B.c.waitClose()).toBe(4003);
    // 정지된 계정은 접속할 수 없다
    const denied = await WsClient.open(srv.port);
    denied.send(helloFrame(b as Hero));
    expect(await denied.waitClose()).toBe(4003);
  });
});

describe('친구 접속 상태, 파티 알림과 초대', () => {
  it('친구에게만 접속·종료·맵 이동이 보인다', async () => {
    const [a, b, c] = await heroes(3);
    const A = a as Hero;
    const B = b as Hero;
    const req = await rest.post(A, '/friends/requests', { request_id: randomUUID(), character_id: A.id, target: B.id });
    const ok = await rest.post(B, `/friends/requests/${req.body.data.request.id}/respond`, { request_id: randomUUID(), accept: true });
    expect(ok.status).toBe(200);
    const bConn = await join(B);
    const cConn = await join(c as Hero);
    const aConn = await join(A);
    // B가 먼저 접속 중이었으므로 A의 hello 직후 목록에 online이 담긴다
    const list = await aConn.c.waitT('friends.presence');
    expect((list.list as { online: boolean }[])[0]?.online).toBe(true);
    // A의 접속은 B에게 푸시된다
    expect(((await bConn.c.waitFor((f) => f.t === 'friends.presence' && (f.list as { online: boolean }[])[0]?.online === true)).list as { id: string }[])[0]?.id).toBe(ok.body.data.friend.id);
    const mapId = [...getGameData().maps.keys()][0] as string;
    aConn.c.send({ t: 'presence.set', map_id: mapId });
    const moved = await bConn.c.waitFor((f) => f.t === 'friends.presence' && (f.list as { where: unknown }[])[0]?.where !== null);
    expect((moved.list as { where: { map_id: string } }[])[0]?.where.map_id).toBe(mapId);
    aConn.c.send({ t: 'presence.set', map_id: 'no_such_map' });
    expect((await aConn.c.waitT('error')).code).toBe('BAD_FRAME');
    // 친구 요청이 오면 friends.changed 힌트
    await rest.post(A, '/friends/requests', { request_id: randomUUID(), character_id: A.id, target: (c as Hero).id });
    expect((await cConn.c.waitT('friends.changed')).reason).toBe('request');
    // 친구가 아닌 C에게는 A의 접속 상태가 가지 않는다
    await cConn.c.expectNone((f) => f.t === 'friends.presence' && (f.list as unknown[]).length > 0);
    // A가 나가면 B에게 offline이 간다
    aConn.c.close();
    await bConn.c.waitFor((f) => f.t === 'friends.presence' && (f.list as { online: boolean }[])[0]?.online === false);
  });

  it('파티 채팅은 파티원만, 파티 변화는 party.changed 힌트로, 초대는 party.invite로 온다', async () => {
    const [h, m, o] = await heroes(3);
    const H = h as Hero;
    const M = m as Hero;
    const O = o as Hero;
    const H1 = await join(H);
    const M1 = await join(M);
    const O1 = await join(O);
    const create = await rest.post(H, `/characters/${H.id}/parties`, {
      request_id: randomUUID(), dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, listed: false,
    });
    expect(create.status).toBe(201);
    H1.c.send(chat('party', '파티 시작'));
    await H1.c.waitT('chat.ack');
    await sleep(70);
    // 아직 파티가 없는 M은 파티 채널로 보낼 수 없다
    M1.c.send(chat('party', '나도'));
    expect((await M1.c.waitT('chat.err')).code).toBe('NOT_IN_PARTY');
    // 초대: 상대가 접속 중이어야 하고, 푸시와 폴링 안전망 둘 다로 온다
    const inv = await rest.post(H, `/characters/${H.id}/party/invites`, { request_id: randomUUID(), target: M.id });
    expect(inv.status).toBe(201);
    const pushed = await M1.c.waitT('party.invite');
    expect(pushed).toMatchObject({ id: inv.body.data.invite.id, from: { id: H.id } });
    const poll = await request(app).get(`/characters/${M.id}/party`).set(auth(M.s));
    expect(poll.body.data.invites_incoming[0].id).toBe(inv.body.data.invite.id);
    const offline = await rest.post(H, `/characters/${H.id}/party/invites`, { request_id: randomUUID(), target: randomUUID() });
    expect(offline.status).toBe(404);
    // 수락하면 파티원이 되고, 방장은 party.changed와 invite.closed를 받는다
    const acc = await rest.post(M, `/characters/${M.id}/party/invites/${inv.body.data.invite.id}/respond`, { request_id: randomUUID(), accept: true });
    expect(acc.status).toBe(200);
    expect(acc.body.data.party.members).toHaveLength(2);
    expect((await H1.c.waitT('party.invite.closed')).state).toBe('accepted');
    expect((await H1.c.waitT('party.changed')).scope).toBe('party');
    await sleep(70);
    M1.c.send(chat('party', '들어왔어요'));
    await M1.c.waitT('chat.ack');
    expect((await H1.c.waitFor((f) => f.t === 'chat.msg' && f.text === '들어왔어요')).channel).toBe('party');
    await O1.c.expectNone((f) => f.t === 'chat.msg' && f.channel === 'party');
    // 가입 전 대화는 따라잡기에 없다
    M1.c.close();
    await until(() => registry.size() === 2);
    const rejoin = await join(M, { since: 0 });
    expect((rejoin.backlog.lines as { text: string }[]).map((l) => l.text)).not.toContain('파티 시작');
    // 이미 파티에 있는 사람은 초대할 수 없다(TARGET_BUSY)
    const busy = await rest.post(H, `/characters/${H.id}/party/invites`, { request_id: randomUUID(), target: M.id });
    expect(busy.body.errors.code).toBe('TARGET_BUSY');
  });

  it('초대 수락도 전투력·정원 검사를 건너뛰지 못하고, 거절·취소는 상대에게 알려진다', async () => {
    const [h, m] = await heroes(2);
    const H = h as Hero;
    const M = m as Hero;
    const H1 = await join(H);
    const M1 = await join(M);
    await rest.post(H, `/characters/${H.id}/parties`, {
      request_id: randomUUID(), dungeon_id: 'gold_vein', difficulty: 0, max_members: 2, min_power: 0, listed: false,
    });
    const inv = await rest.post(H, `/characters/${H.id}/party/invites`, { request_id: randomUUID(), target: M.id });
    await M1.c.waitT('party.invite');
    // 최소 전투력을 올려 두면 수락이 거절된다
    await getPool().query("UPDATE parties SET min_power = 99999 WHERE leader_character_id = (SELECT id FROM characters WHERE uuid = $1)", [H.id]);
    const low = await rest.post(M, `/characters/${M.id}/party/invites/${inv.body.data.invite.id}/respond`, { request_id: randomUUID(), accept: true });
    expect(low.status).toBe(422);
    expect(low.body.errors.code).toBe('POWER_TOO_LOW');
    const no = await rest.post(M, `/characters/${M.id}/party/invites/${inv.body.data.invite.id}/respond`, { request_id: randomUUID(), accept: false });
    expect(no.body.data.declined).toBe(true);
    expect((await H1.c.waitT('party.invite.closed')).state).toBe('declined');
    // 다시 초대하고 방장이 취소
    await sleep(10);
    await getPool().query('UPDATE party_invites SET created_at = created_at - interval \'1 minute\'');
    const inv2 = await rest.post(H, `/characters/${H.id}/party/invites`, { request_id: randomUUID(), target: M.id });
    expect(inv2.status).toBe(201);
    await M1.c.waitT('party.invite');
    const cancel = await request(app).delete(`/characters/${H.id}/party/invites/${inv2.body.data.invite.id}`).set(auth(H.s));
    expect(cancel.status).toBe(200);
    expect((await M1.c.waitT('party.invite.closed')).state).toBe('cancelled');
    // 입력 오류와 재전송
    expect((await rest.post(H, `/characters/${H.id}/party/invites`, { target: M.id })).status).toBe(400);
  });
});

describe('보안 보강', () => {
  it('같은 계정의 hello가 동시에 둘 와도 연결은 하나만 남고 제재가 그 하나에 닿는다', async () => {
    const [a] = await heroes(1);
    const A = a as Hero;
    const c1 = await WsClient.open(srv.port);
    const c2 = await WsClient.open(srv.port);
    open.push(c1, c2);
    c1.send(helloFrame(A));
    c2.send(helloFrame(A));
    await until(() => c1.closed !== null || c2.closed !== null);
    await sleep(300);
    expect(registry.size()).toBe(1);
    expect(registry.inShard(1)).toHaveLength(1);
    const winner = registry.ofAccount(Number((await getPool().query('SELECT account_id FROM characters WHERE uuid = $1', [A.id])).rows[0].account_id));
    expect(winner).toBeDefined();
    const [survivor, loser] = c1.closed === null ? [c1, c2] : [c2, c1];
    expect(loser.closed?.code).toBe(4001);
    expect(survivor.closed).toBeNull();
    await getPool().query(
      `INSERT INTO account_sanctions (account_id, kind, source, reason_code, ends_at, created_by)
       SELECT account_id, 'chat_mute', 'admin', 'abuse', now() + interval '1 hour', 'tester' FROM characters WHERE uuid = $1`,
      [A.id],
    );
    expect((await survivor.waitT('sanction')).kind).toBe('chat_mute');
    survivor.send(chat('general', '안녕'));
    expect((await survivor.waitFor((f) => f.t === 'chat.err')).code).toBe('MUTED_SANCTION');
  });

  it('나를 차단한 사람이 접속 중이 아니어도 귓속말은 정상 ack(접속·차단이 드러나지 않는다), 차단 안 한 오프라인은 TARGET_OFFLINE', async () => {
    const [a, b, c] = await heroes(3);
    const A = await join(a as Hero);
    const B = await join(b as Hero);
    await rest.put(b as Hero, `/blocks/${(a as Hero).id}`);
    B.c.close();
    await until(() => registry.size() === 1);
    A.c.send(chat('whisper', '거기 있어?', { to: (b as Hero).id }));
    expect((await A.c.waitT('chat.ack')).replay).toBe(false);
    await sleep(70);
    A.c.send(chat('whisper', '거기 있어?', { to: (c as Hero).id }));
    expect((await A.c.waitT('chat.err')).code).toBe('TARGET_OFFLINE');
    expect(await lineCount()).toBe(0);
  });

  it('원문 금칙어와 변형을 함께 섞어도 거절된다', async () => {
    expect(hasObfuscatedBannedWord('시발 시 발')).toBe(true);
    expect(hasObfuscatedBannedWord('시발')).toBe(false);
    expect(maskBannedWords('시발').text).toBe('**');
    const [a] = await heroes(1);
    const { c } = await join(a as Hero);
    c.send(chat('general', '시발 시.발'));
    expect((await c.waitT('chat.err')).code).toBe('MESSAGE_BLOCKED');
    expect(await lineCount()).toBe(0);
  });

  it('친구가 아닌 사람을 차단해도 friends.changed는 가지 않고, 친구를 차단하면 간다', async () => {
    const [a, b, c] = await heroes(3);
    const B = await join(b as Hero);
    const C = await join(c as Hero);
    await rest.put(a as Hero, `/blocks/${(b as Hero).id}`);
    await B.c.expectNone((f) => f.t === 'friends.changed');
    const req = await rest.post(c as Hero, '/friends/requests', { request_id: randomUUID(), character_id: (c as Hero).id, target: (a as Hero).id });
    const ok = await rest.post(a as Hero, `/friends/requests/${req.body.data.request.id}/respond`, { request_id: randomUUID(), accept: true });
    expect(ok.status).toBe(200);
    await C.c.waitT('friends.changed');
    await rest.put(a as Hero, `/blocks/${(c as Hero).id}`);
    expect((await C.c.waitFor((f) => f.t === 'friends.changed' && f.reason === 'removed')).reason).toBe('removed');
  });

  it('신고 증거: 신고자가 접속하기 전의 일반 채팅은 담기지 않고, 접속 뒤의 줄은 담긴다', async () => {
    const [a, b] = await heroes(2);
    const B = await join(b as Hero);
    B.c.send(chat('general', '접속 전 대화'));
    await B.c.waitT('chat.ack');
    await sleep(30);
    const A = await join(a as Hero);
    await sleep(70);
    B.c.send(chat('general', '접속 후 대화'));
    await A.c.waitT('chat.msg');
    const r = await rest.post(a as Hero, '/reports', { request_id: randomUUID(), character_id: (a as Hero).id, target: (b as Hero).id, reason: 'abuse' });
    expect(r.status).toBe(201);
    const lines = await getPool().query('SELECT text FROM report_lines ORDER BY seq');
    expect(lines.rows).toEqual([{ text: '접속 후 대화' }]);
  });
});
