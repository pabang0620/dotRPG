import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { newHero, type Hero } from './economyHelpers';
import { auth, buildApp, resetDb, shutdown, ver } from './helpers';

let app: Express;
beforeAll(() => {
  app = buildApp();
});
beforeEach(async () => {
  await resetDb();
});
afterAll(async () => {
  await shutdown();
});

const heroes = async (n: number): Promise<Hero[]> => {
  const out: Hero[] = [];
  for (let i = 0; i < n; i++) out.push(await newHero(app));
  return out;
};
const api = {
  get: (h: Hero, path: string) => request(app).get(path).set(auth(h.s)),
  post: (h: Hero, path: string, body: Record<string, unknown>) => request(app).post(path).set(auth(h.s)).send(body),
  put: (h: Hero, path: string) => request(app).put(path).set(auth(h.s)),
  del: (h: Hero, path: string) => request(app).delete(path).set(auth(h.s)),
};
const friendReq = (from: Hero, to: Hero, requestId: string = randomUUID()) =>
  api.post(from, '/friends/requests', { request_id: requestId, character_id: from.id, target: to.id });
const befriend = async (a: Hero, b: Hero): Promise<string> => {
  const r = await friendReq(a, b);
  const res = await api.post(b, `/friends/requests/${r.body.data.request.id}/respond`, { request_id: randomUUID(), accept: true });
  expect(res.status).toBe(200);
  return res.body.data.friend.id as string;
};

describe('친구 (F1~F5)', () => {
  it('요청 -> 수락으로 친구가 되고, 목록에 양쪽이 보인다', async () => {
    const [a, b] = await heroes(2);
    const r = await friendReq(a as Hero, b as Hero);
    expect(r.status).toBe(201);
    expect(r.body.data.status).toBe('pending');
    const mineB = await api.get(b as Hero, '/friends');
    expect(mineB.body.data.incoming).toHaveLength(1);
    const reqId = mineB.body.data.incoming[0].id as string;
    const ok = await api.post(b as Hero, `/friends/requests/${reqId}/respond`, { request_id: randomUUID(), accept: true });
    expect(ok.status).toBe(200);
    const la = await api.get(a as Hero, '/friends');
    expect(la.body.data.friends).toHaveLength(1);
    expect(la.body.data.friends[0].character.id).toBe((b as Hero).id);
    expect(la.body.data.friends[0].online).toBe(false);
    expect(la.body.data.outgoing).toHaveLength(0);
    // 삭제는 한쪽이 하면 끝나고, 또 해도 성공(멱등)
    const fid = la.body.data.friends[0].id as string;
    expect((await api.del(a as Hero, `/friends/${fid}`)).status).toBe(200);
    expect((await api.del(a as Hero, `/friends/${fid}`)).status).toBe(200);
    expect((await api.get(b as Hero, '/friends')).body.data.friends).toHaveLength(0);
  });

  it('입력 오류: request_id 없음, 알 수 없는 필드, 자기 자신, 없는 사람, 데이터 버전 헤더는 요구하지 않는다', async () => {
    const [a, b] = await heroes(2);
    const A = a as Hero;
    expect((await api.post(A, '/friends/requests', { character_id: A.id, target: (b as Hero).id })).status).toBe(400);
    expect(
      (await api.post(A, '/friends/requests', { request_id: randomUUID(), character_id: A.id, target: (b as Hero).id, extra: 1 })).status,
    ).toBe(400);
    const self = await friendReq(A, A);
    expect(self.status).toBe(422);
    expect(self.body.errors.code).toBe('CANNOT_TARGET_SELF');
    const none = await api.post(A, '/friends/requests', { request_id: randomUUID(), character_id: A.id, target: randomUUID() });
    expect(none.status).toBe(404);
    expect(none.body.errors.code).toBe('PLAYER_NOT_FOUND');
    const noData = await request(app).get('/friends').set({ 'X-Client-Version': '0.2.0', Authorization: `Bearer ${A.s.access}` });
    expect(noData.status).toBe(200);
    const old = await request(app).get('/friends').set({ ...ver(), 'X-Client-Version': '0.0.1', Authorization: `Bearer ${A.s.access}` });
    expect(old.status).toBe(426);
  });

  it('재전송: 같은 request_id는 처음 응답을 그대로, 다른 내용이면 422', async () => {
    const [a, b, c] = await heroes(3);
    const rid = randomUUID();
    const first = await friendReq(a as Hero, b as Hero, rid);
    const again = await friendReq(a as Hero, b as Hero, rid);
    expect(again.status).toBe(first.status);
    expect(again.headers['idempotent-replay']).toBe('true');
    expect(again.body.data.request.id).toBe(first.body.data.request.id);
    const mismatch = await friendReq(a as Hero, c as Hero, rid);
    expect(mismatch.status).toBe(422);
    expect(mismatch.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    const rows = await getPool().query("SELECT count(*) AS n FROM friendships WHERE state = 'pending'");
    expect(Number((rows.rows[0] as { n: string }).n)).toBe(1);
  });

  it('동시 요청: A->B와 B->A가 동시에 와도 친구 관계 하나로 끝난다', async () => {
    const [a, b] = await heroes(2);
    const [r1, r2] = await Promise.all([friendReq(a as Hero, b as Hero), friendReq(b as Hero, a as Hero)]);
    expect([r1.status, r2.status].every((s) => s === 200 || s === 201)).toBe(true);
    const live = await getPool().query("SELECT state FROM friendships WHERE state IN ('pending', 'accepted')");
    expect(live.rows).toHaveLength(1);
    expect((live.rows[0] as { state: string }).state).toBe('accepted');
    // 같은 요청을 동시에 두 번(request_id 같음): 행 하나
    const [c, d] = await heroes(2);
    const rid = randomUUID();
    const both = await Promise.all([friendReq(c as Hero, d as Hero, rid), friendReq(c as Hero, d as Hero, rid)]);
    expect(both.map((x) => x.body.data.request.id).every((id) => id === both[0]?.body.data.request.id)).toBe(true);
  });

  it('중복 요청, 거절 뒤 쿨다운, 취소는 멱등', async () => {
    const [a, b, c] = await heroes(3);
    const r = await friendReq(a as Hero, b as Hero);
    const dup = await friendReq(a as Hero, b as Hero);
    expect(dup.status).toBe(200);
    expect(dup.body.data.request.id).toBe(r.body.data.request.id);
    const declined = await api.post(b as Hero, `/friends/requests/${r.body.data.request.id}/respond`, { request_id: randomUUID(), accept: false });
    expect(declined.body.data.declined).toBe(true);
    const cool = await friendReq(a as Hero, b as Hero);
    expect(cool.status).toBe(429);
    expect(cool.body.errors.code).toBe('REREQUEST_COOLDOWN');
    // 취소는 멱등
    const q = await friendReq(a as Hero, c as Hero);
    expect((await api.del(a as Hero, `/friends/requests/${q.body.data.request.id}`)).status).toBe(200);
    expect((await api.del(a as Hero, `/friends/requests/${q.body.data.request.id}`)).status).toBe(200);
    // 남의 요청은 건드릴 수 없다
    expect((await api.del(b as Hero, `/friends/requests/${q.body.data.request.id}`)).status).toBe(404);
  });
});

describe('차단 (B1~B3)', () => {
  it('차단하면 친구가 끊기고 친구 요청은 조용히 무시된다, 해제는 멱등', async () => {
    const [a, b] = await heroes(2);
    const A = a as Hero;
    const B = b as Hero;
    await befriend(A, B);
    const blocked = await api.put(A, `/blocks/${B.id}`);
    expect(blocked.status).toBe(201);
    expect((await api.put(A, `/blocks/${B.id}`)).status).toBe(200);
    expect((await api.get(A, '/friends')).body.data.friends).toHaveLength(0);
    const list = await api.get(A, '/blocks');
    expect(list.body.data.blocks).toHaveLength(1);
    // 차단당한 B의 요청은 정상 요청과 같은 모양으로 보이지만 A에게는 나타나지 않는다
    const silent = await friendReq(B, A);
    expect(silent.status).toBe(201);
    expect(Object.keys(silent.body.data.request).sort()).toEqual(['created_at', 'id', 'to']);
    expect((await api.get(A, '/friends')).body.data.incoming).toHaveLength(0);
    // 내가 차단한 사람에게는 요청할 수 없다
    const mine = await friendReq(A, B);
    expect(mine.body.errors.code).toBe('YOU_BLOCKED_TARGET');
    const id = list.body.data.blocks[0].id as string;
    expect((await api.del(A, `/blocks/${id}`)).status).toBe(200);
    expect((await api.del(A, `/blocks/${id}`)).status).toBe(200);
    expect((await api.get(A, '/blocks')).body.data.blocks).toHaveLength(0);
    expect((await api.del(B, `/blocks/${id}`)).status).toBe(404);
  });

  it('입력 오류와 동시 차단', async () => {
    const [a, b] = await heroes(2);
    expect((await api.put(a as Hero, '/blocks/not-a-uuid')).status).toBe(400);
    expect((await api.put(a as Hero, `/blocks/${(a as Hero).id}`)).status).toBe(422);
    expect((await api.put(a as Hero, `/blocks/${randomUUID()}`)).status).toBe(404);
    const rs = await Promise.all([api.put(a as Hero, `/blocks/${(b as Hero).id}`), api.put(a as Hero, `/blocks/${(b as Hero).id}`)]);
    expect(rs.map((r) => r.status).sort()).toEqual([200, 201]);
    const n = await getPool().query('SELECT count(*) AS n FROM blocks WHERE deleted_at IS NULL');
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
  });
});

describe('신고 (X1)', () => {
  const seedChat = async (from: Hero, shard: number, text: string, extra: { channel?: string } = {}) => {
    await getPool().query(
      `INSERT INTO chat_messages (channel, shard, sender_account_id, sender_character_id, sender_name, text, client_msg_id)
       SELECT $1, $2, c.account_id, c.id, c.name, $3, $4 FROM characters c WHERE c.uuid = $5`,
      [extra.channel ?? 'general', shard, text, randomUUID(), from.id],
    );
  };
  const report = (from: Hero, to: Hero, reason = 'abuse', requestId: string = randomUUID()) =>
    api.post(from, '/reports', { request_id: requestId, character_id: from.id, target: to.id, reason });

  it('만난 적 있는 사람만 신고할 수 있고, 증거는 서버가 채팅에서 복사한다', async () => {
    const [a, b] = await heroes(2);
    const A = a as Hero;
    const B = b as Hero;
    // 만난 적이 없으면 거절
    const none = await report(A, B);
    expect(none.status).toBe(422);
    expect(none.body.errors.code).toBe('REPORT_NO_CONTEXT');
    // 같은 shard의 일반 채팅은 접속 중인 세션이 있어야 보인다. 접속 없이 친구가 되면 접수된다
    await befriend(A, B);
    await seedChat(B, 1, '나쁜 말');
    const ok = await report(A, B);
    expect(ok.status).toBe(201);
    expect(ok.body.data.report.state).toBe('open');
    const lines = await getPool().query('SELECT count(*) AS n FROM report_lines');
    expect(Number((lines.rows[0] as { n: string }).n)).toBe(ok.body.data.report.line_count);
    // 같은 사유의 처리 전 신고가 있으면 기존 신고를 돌려준다
    const dup = await report(A, B);
    expect(dup.status).toBe(200);
    expect(dup.body.data.duplicate).toBe(true);
    // 사유 코드가 chat.json에 없으면 400
    expect((await report(A, B, 'whatever')).status).toBe(400);
    expect(getGameData().chat.reportReasons.map((r) => r.code)).toContain('abuse');
  });

  it('재전송과 동시 요청은 신고 한 건', async () => {
    const [a, b] = await heroes(2);
    await befriend(a as Hero, b as Hero);
    const rid = randomUUID();
    const rs = await Promise.all([report(a as Hero, b as Hero, 'spam', rid), report(a as Hero, b as Hero, 'spam', rid)]);
    expect(rs.map((r) => r.body.data.report.id).every((id) => id === rs[0]?.body.data.report.id)).toBe(true);
    const again = await report(a as Hero, b as Hero, 'spam', rid);
    expect(again.headers['idempotent-replay']).toBe('true');
    const n = await getPool().query('SELECT count(*) AS n FROM reports');
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
  });

  it('시간당 한도를 넘으면 429 REPORT_LIMIT', async () => {
    const [a, b] = await heroes(2);
    await befriend(a as Hero, b as Hero);
    const reasons = getGameData().chat.reportReasons.map((r) => r.code);
    for (const r of reasons) expect((await report(a as Hero, b as Hero, r)).status).toBe(201);
    const over = await report(a as Hero, b as Hero, 'abuse', randomUUID());
    // 5개 사유를 모두 썼으므로 처리 전 중복(200)이 아니라 한도(429)가 먼저 나온다
    expect(over.status).toBe(429);
    expect(over.body.errors.code).toBe('REPORT_LIMIT');
  });
});

describe('조용한 무시와 보안 보강', () => {
  it('차단당한 친구 요청은 내 보낸 목록에만 남고, 재요청(멱등)·취소가 정상 요청처럼 동작하며 상대는 수락할 수 없다', async () => {
    const [a, b, c] = await heroes(3);
    const A = a as Hero;
    const B = b as Hero;
    await api.put(A, `/blocks/${B.id}`);
    const first = await friendReq(B, A);
    const again = await friendReq(B, A);
    expect(again.status).toBe(200);
    expect(again.body.data.request.id).toBe(first.body.data.request.id);
    expect((await api.get(B, '/friends')).body.data.outgoing).toHaveLength(1);
    expect((await api.get(A, '/friends')).body.data.incoming).toHaveLength(0);
    const sneaky = await api.post(A, `/friends/requests/${first.body.data.request.id}/respond`, { request_id: randomUUID(), accept: true });
    expect(sneaky.status).toBe(404);
    expect((await api.del(B, `/friends/requests/${first.body.data.request.id}`)).status).toBe(200);
    // 정상 요청과 응답 모양이 같다
    const normal = await friendReq(B, c as Hero);
    expect(Object.keys(normal.body.data.request).sort()).toEqual(Object.keys(first.body.data.request).sort());
  });

  it('삭제된 캐릭터에게는 친구 요청을 보낼 수 없다(404), 차단은 가능', async () => {
    const [a, b] = await heroes(2);
    await getPool().query('UPDATE characters SET deleted_at = now() WHERE uuid = $1', [(b as Hero).id]);
    const r = await friendReq(a as Hero, b as Hero);
    expect(r.status).toBe(404);
    expect(r.body.errors.code).toBe('PLAYER_NOT_FOUND');
    expect((await api.put(a as Hero, `/blocks/${(b as Hero).id}`)).status).toBe(201);
  });

  it('나를 차단한 사람에게 보낸 파티 초대는 접속·상태와 무관하게 정상 응답이고, 멱등·취소가 같게 동작한다', async () => {
    const [h, m, busyHero] = await heroes(3);
    const H = h as Hero;
    const M = m as Hero;
    const created = await request(app).post(`/characters/${H.id}/parties`).set(auth(H.s)).send({
      request_id: randomUUID(), dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, listed: false,
    });
    expect(created.status).toBe(201);
    const invite = (target: Hero) =>
      request(app).post(`/characters/${H.id}/party/invites`).set(auth(H.s)).send({ request_id: randomUUID(), target: target.id });
    // 차단하지 않은 오프라인 대상은 TARGET_OFFLINE
    expect((await invite(M)).body.errors.code).toBe('TARGET_OFFLINE');
    await api.put(M, `/blocks/${H.id}`);
    const silent = await invite(M);
    expect(silent.status).toBe(201);
    expect(Object.keys(silent.body.data.invite).sort()).toEqual(['expires_at', 'id', 'state']);
    // 상대가 다른 파티에 있어도(BUSY) 차단했다면 같은 응답
    const other = await request(app).post(`/characters/${M.id}/parties`).set(auth(M.s)).send({
      request_id: randomUUID(), dungeon_id: 'gold_vein', difficulty: 0, max_members: 2, min_power: 0, listed: false,
    });
    expect(other.status).toBe(201);
    const again = await invite(M);
    expect(again.status).toBe(200);
    expect(again.body.data.invite.id).toBe(silent.body.data.invite.id);
    // 방장 쪽 취소는 정상 초대처럼 성공하고, 받는 사람에게는 목록도 응답 대상도 없다
    const poll = await request(app).get(`/characters/${M.id}/party`).set(auth(M.s));
    expect(poll.body.data.invites_incoming).toEqual([]);
    const respond = await request(app).post(`/characters/${M.id}/party/invites/${silent.body.data.invite.id}/respond`).set(auth(M.s))
      .send({ request_id: randomUUID(), accept: true });
    expect(respond.status).toBe(404);
    const del = await request(app).delete(`/characters/${H.id}/party/invites/${silent.body.data.invite.id}`).set(auth(H.s));
    expect(del.status).toBe(200);
    expect(busyHero).toBeDefined();
  });

  it('차단 시 친구 관계가 실제로 끝났을 때만 friends.changed를 보낸다(행 상태로 확인)', async () => {
    const [a, b, c] = await heroes(3);
    await befriend(a as Hero, b as Hero);
    await api.put(a as Hero, `/blocks/${(b as Hero).id}`);
    await api.put(a as Hero, `/blocks/${(c as Hero).id}`);
    const rows = await getPool().query("SELECT state FROM friendships ORDER BY id");
    expect(rows.rows).toEqual([{ state: 'removed' }]);
  });

  it('신고 증거의 일반 채팅은 신고자가 접속한 뒤의 줄만 담긴다', async () => {
    const [a, b] = await heroes(2);
    await befriend(a as Hero, b as Hero);
    await getPool().query(
      `INSERT INTO chat_messages (channel, shard, sender_account_id, sender_character_id, sender_name, text, client_msg_id)
       SELECT 'general', 1, c.account_id, c.id, c.name, '접속 전 대화', $2 FROM characters c WHERE c.uuid = $1`,
      [(b as Hero).id, randomUUID()],
    );
    // 접속 세션이 없으면 일반 채팅은 증거 후보가 아니다
    const r = await api.post(a as Hero, '/reports', { request_id: randomUUID(), character_id: (a as Hero).id, target: (b as Hero).id, reason: 'abuse' });
    expect(r.status).toBe(201);
    expect(r.body.data.report.line_count).toBe(0);
  });
});
