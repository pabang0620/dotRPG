import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { runMatchTick } from '../src/domains/match/matchService';
import { getQueueStore } from '../src/domains/match/queueStore';
import { setBannedWords } from '../src/utils/bannedWords';
import { setClockOverride } from '../src/utils/clock';
import { setRng } from '../src/utils/rng';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { fakeRng, seedLevel, seedWorn } from './economyHelpers';
import { createParty, del, formParty, get, newHero, patch, post, raw, type Hero } from './partyHelpers';
import request from 'supertest';

const app = buildApp();
const MONDAY = '2026-10-05T03:00:00Z';
let fixed = new Date(MONDAY);
const advance = (sec: number) => {
  fixed = new Date(fixed.getTime() + sec * 1000);
};

beforeEach(resetDb);
afterAll(async () => {
  setClockOverride(null);
  setRng(null);
  setBannedWords(null);
  await shutdown();
});
beforeEach(() => {
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  setRng(fakeRng({ unit: 1 }));
  getQueueStore().clear();
  setBannedWords(null);
});

describe('모집 게시판: 만들기, 목록, 신청, 수락', () => {
  it('정상: 만들기 -> 목록 -> 신청 -> 수락, 방장 화면에 신청이 보이고 version이 오른다', async () => {
    const host = await newHero(app);
    const guest = await newHero(app);
    const c = await createParty(app, host, { message: '빠르게 돌아요', min_power: 300 });
    expect(c.status).toBe(201);
    const party = c.body.data.party;
    expect(party).toMatchObject({ state: 'forming', max_members: 4, min_power: 300, message: '빠르게 돌아요', listed: true, source: 'board' });
    expect(party.members).toHaveLength(1);
    expect(party.members[0]).toMatchObject({ is_leader: true, is_me: true, ready: true });
    expect(JSON.stringify(c.body)).not.toMatch(/"id":\s*\d+/); // 내부 숫자 키는 나가지 않는다

    const list = await get(app, guest, '/parties');
    expect(list.status).toBe(200);
    expect(list.body.meta).toMatchObject({ total: 1, page: 1, limit: 20 });
    expect(list.body.data.posts[0]).toMatchObject({ id: party.id, members: 1, mine: false, applied: false, full: false });
    expect(list.body.data.posts[0].leader.name).toBe(party.members[0].name);

    const ap = await post(app, guest, `/parties/${party.id}/apply`, {});
    expect(ap.status).toBe(201);
    expect(ap.body.data.application.state).toBe('pending');
    expect((await get(app, guest, '/parties')).body.data.posts[0].applied).toBe(true);

    const hostView = await get(app, host, '/party');
    expect(hostView.body.data.party.applications).toHaveLength(1);
    const v = hostView.body.data.party.version;
    const rs = await post(app, host, `/party/applications/${ap.body.data.application.id}/respond`, { accept: true });
    expect(rs.status).toBe(200);
    expect(rs.body.data.party.members).toHaveLength(2);
    expect(rs.body.data.party.version).toBeGreaterThan(v);
    // 신청자는 폴링으로 파티를 본다
    const gv = await get(app, guest, '/party');
    expect(gv.body.data.party.id).toBe(party.id);
    expect(gv.body.data.applications_mine[0].state).toBe('accepted');
  });

  it('폴링: after_version이 같으면 changed=false만 돌려준다', async () => {
    const host = await newHero(app);
    const c = await createParty(app, host);
    const v = c.body.data.party.version as number;
    const same = await get(app, host, `/party?after_version=${v}`);
    expect(same.body.data).toEqual({ changed: false });
    await patch(app, host, '/party', { message: '바뀜' });
    const newer = await get(app, host, `/party?after_version=${v}`);
    expect(newer.body.data.changed).toBe(true);
    expect(newer.body.data.party.version).toBeGreaterThan(v);
    expect((await get(app, host, '/party?after_version=-1')).status).toBe(400);
  });

  it('입력 오류와 규칙 오류', async () => {
    const host = await newHero(app);
    expect((await createParty(app, host, { max_members: 5 })).status).toBe(400);
    expect((await createParty(app, host, { leader_power: 9999 })).status).toBe(400);
    expect((await createParty(app, host, { message: 'x'.repeat(31) })).status).toBe(400);
    expect((await createParty(app, host, { dungeon_id: 'nope' })).body.errors.code).toBe('DUNGEON_UNKNOWN');
    expect((await createParty(app, host, { dungeon_id: 'smelter' })).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    const high = await createParty(app, host, { min_power: 99999 });
    expect(high.status).toBe(422);
    expect(high.body.errors.code).toBe('MIN_POWER_TOO_HIGH');
    setBannedWords(['나쁜말']);
    expect((await createParty(app, host, { message: '이건 나쁜말 입니다' })).body.errors.code).toBe('MESSAGE_BLOCKED');
    // 이미 파티가 있으면 다시 만들 수 없다
    expect((await createParty(app, host)).status).toBe(201);
    const again = await createParty(app, host);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('ALREADY_IN_PARTY');
    expect((await get(app, host, '/parties?limit=500')).status).toBe(400);
  });

  it('권장 레벨 - 5 미만은 LEVEL_TOO_LOW (레이드는 레이드 자체의 권장 레벨)', async () => {
    const h = await newHero(app);
    fixed = new Date('2026-10-07T03:00:00Z'); // 수요일: 해골왕 열림
    await getPool().query("INSERT INTO quest_claims (character_id, quest_id, reward) VALUES ($1, 'c1_fortress', '{}'::jsonb)", [h.dbId]);
    const res = await createParty(app, h, { dungeon_id: 'raid_skeleton_king', difficulty: 0 });
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'LEVEL_TOO_LOW', need: 17, have: 1 });
    await seedLevel(h, 17);
    expect((await createParty(app, h, { dungeon_id: 'raid_skeleton_king', difficulty: 0, max_members: 4 })).status).toBe(201);
  });

  it('전투력 부족: 최소 전투력보다 약한 캐릭터의 신청은 POWER_TOO_LOW(need, have)', async () => {
    const host = await newHero(app);
    const guest = await newHero(app);
    await seedWorn(host, 0, 'eq_sword_dragon');
    const me = await get(app, host, '');
    const power = me.body.data.character.power_estimate as number;
    expect(power).toBeGreaterThan((await get(app, guest, '')).body.data.character.power_estimate);
    const c = await createParty(app, host, { min_power: power });
    expect(c.status).toBe(201);
    const ap = await post(app, guest, `/parties/${c.body.data.party.id}/apply`, {});
    expect(ap.status).toBe(422);
    expect(ap.body.errors).toMatchObject({ code: 'POWER_TOO_LOW', need: power });
  });

  it('재전송: 같은 request_id의 만들기·신청·수락은 같은 결과이고 중복이 생기지 않는다', async () => {
    const host = await newHero(app);
    const guest = await newHero(app);
    const rid = randomUUID();
    const a = await post(app, host, '/parties', { dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, listed: true }, rid);
    const b = await post(app, host, '/parties', { dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, listed: true }, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    const n = await getPool().query('SELECT count(*) AS n FROM parties WHERE leader_character_id = $1', [host.dbId]);
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
    // 같은 request_id로 다른 내용은 422
    const diff = await post(app, host, '/parties', { dungeon_id: 'gold_vein', difficulty: 0, max_members: 3, min_power: 0, listed: true }, rid);
    expect(diff.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');

    const arid = randomUUID();
    const p1 = await post(app, guest, `/parties/${a.body.data.party.id}/apply`, {}, arid);
    const p2 = await post(app, guest, `/parties/${a.body.data.party.id}/apply`, {}, arid);
    expect(p2.body).toEqual(p1.body);
    // 다른 request_id로 또 신청해도 기존 신청이 돌아온다
    const p3 = await post(app, guest, `/parties/${a.body.data.party.id}/apply`, {});
    expect(p3.body.data.application.id).toBe(p1.body.data.application.id);
    const rrid = randomUUID();
    const r1 = await post(app, host, `/party/applications/${p1.body.data.application.id}/respond`, { accept: true }, rrid);
    const r2 = await post(app, host, `/party/applications/${p1.body.data.application.id}/respond`, { accept: true }, rrid);
    expect(r2.body).toEqual(r1.body);
    expect(r1.body.data.party.members).toHaveLength(2);
    // 이미 처리된 신청을 다른 request_id로 처리하면 409
    const r3 = await post(app, host, `/party/applications/${p1.body.data.application.id}/respond`, { accept: true });
    expect(r3.body.errors.code).toBe('APPLICATION_NOT_PENDING');
  });

  it('동시 요청: 남은 자리가 하나일 때 두 신청을 동시에 수락하면 한 명만 들어온다', async () => {
    const host = await newHero(app);
    const g1 = await newHero(app);
    const g2 = await newHero(app);
    const c = await createParty(app, host, { max_members: 2 });
    const id = c.body.data.party.id as string;
    const a1 = await post(app, g1, `/parties/${id}/apply`, {});
    const a2 = await post(app, g2, `/parties/${id}/apply`, {});
    const [r1, r2] = await Promise.all([
      post(app, host, `/party/applications/${a1.body.data.application.id}/respond`, { accept: true }),
      post(app, host, `/party/applications/${a2.body.data.application.id}/respond`, { accept: true }),
    ]);
    expect([r1.status, r2.status].sort()).toEqual([200, 409]);
    const loser = r1.status === 409 ? r1 : r2;
    expect(loser.body.errors.code).toBe('PARTY_FULL');
    const members = await getPool().query('SELECT count(*) AS n FROM party_members WHERE left_at IS NULL');
    expect(Number((members.rows[0] as { n: string }).n)).toBeGreaterThanOrEqual(2);
    const mine = await getPool().query('SELECT count(*) AS n FROM party_members pm JOIN parties p ON p.id = pm.party_id WHERE p.uuid = $1 AND pm.left_at IS NULL', [id]);
    expect(Number((mine.rows[0] as { n: string }).n)).toBe(2);
  });

  it('동시 요청: 같은 캐릭터가 파티 두 개를 동시에 만들면 하나만 성공한다', async () => {
    const host = await newHero(app);
    const [a, b] = await Promise.all([createParty(app, host), createParty(app, host)]);
    expect([a.status, b.status].sort()).toEqual([201, 409]);
    const n = await getPool().query("SELECT count(*) AS n FROM party_members WHERE character_id = $1 AND left_at IS NULL", [host.dbId]);
    expect(Number((n.rows[0] as { n: string }).n)).toBe(1);
  });

  it('신청 만료(30초), 취소, 거절, 정원, 동시 신청 3건 제한', async () => {
    const host = await newHero(app);
    const guest = await newHero(app);
    const c = await createParty(app, host, { max_members: 2 });
    const id = c.body.data.party.id as string;
    const ap = await post(app, guest, `/parties/${id}/apply`, {});
    advance(31);
    const late = await post(app, host, `/party/applications/${ap.body.data.application.id}/respond`, { accept: true });
    expect(late.status).toBe(410);
    expect(late.body.errors.code).toBe('APPLICATION_EXPIRED');
    expect((await get(app, guest, '/party')).body.data.applications_mine[0].state).toBe('expired');
    // 취소는 멱등
    const ap2 = await post(app, guest, `/parties/${id}/apply`, {});
    expect((await del(app, guest, `/party/applications/${ap2.body.data.application.id}`)).body.data.cancelled).toBe(true);
    expect((await del(app, guest, `/party/applications/${ap2.body.data.application.id}`)).body.data.cancelled).toBe(true);
    expect((await del(app, host, `/party/applications/${ap2.body.data.application.id}`)).status).toBe(404);
    // 거절
    const ap3 = await post(app, guest, `/parties/${id}/apply`, {});
    const no = await post(app, host, `/party/applications/${ap3.body.data.application.id}/respond`, { accept: false });
    expect(no.body.data.party.members).toHaveLength(1);
    // 내 글에는 신청할 수 없다
    expect((await post(app, host, `/parties/${id}/apply`, {})).body.errors.code).toBe('OWN_PARTY');
    expect((await post(app, guest, `/parties/${randomUUID()}/apply`, {})).body.errors.code).toBe('PARTY_NOT_FOUND');
    // 게시 10분 만료: 목록에서 빠지고 신청도 404
    advance(11 * 60);
    expect((await get(app, guest, '/parties')).body.data.posts).toHaveLength(0);
    expect((await post(app, guest, `/parties/${id}/apply`, {})).body.errors.code).toBe('PARTY_NOT_FOUND');
  });
});

describe('로비: 준비, 설정 변경, 나가기, 강퇴, 방장 위임', () => {
  it('정상: 준비 토글, PATCH 설정(준비 해제), 위임, 강퇴 알림, 방장 나가기', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    const m2 = await newHero(app);
    const partyId = await formParty(app, host, [m1, m2]);
    const me = (await get(app, m1, '/party')).body.data.party;
    expect(me.members.find((m: { is_me: boolean }) => m.is_me).ready).toBe(true);
    expect((await raw(app, host, '/party/ready', { ready: true })).body.errors.code).toBe('LEADER_ALWAYS_READY');
    expect((await raw(app, m1, '/party/ready', { ready: false })).body.data.party.members.find((m: { is_me: boolean }) => m.is_me).ready).toBe(false);

    // 설정 변경은 방장만, 준비가 풀린다
    expect((await patch(app, m1, '/party', { message: '안녕' })).body.errors.code).toBe('NOT_LEADER');
    expect((await patch(app, host, '/party', {})).status).toBe(400);
    expect((await patch(app, host, '/party', { max_members: 2 })).body.errors.code).toBe('MAX_MEMBERS_TOO_LOW');
    const pt = await patch(app, host, '/party', { message: '안녕', listed: false });
    expect(pt.status).toBe(200);
    expect(pt.body.data.party).toMatchObject({ message: '안녕', listed: false });

    // 위임
    const lead = await post(app, host, '/party/leader', { target: m1.id });
    expect(lead.body.data.party.members.find((m: { character_id: string }) => m.character_id === m1.id).is_leader).toBe(true);
    expect((await post(app, host, '/party/leader', { target: m2.id })).body.errors.code).toBe('NOT_LEADER');
    // 강퇴: 자기 자신 불가, 대상은 다음 폴링에서 KICKED 알림
    expect((await post(app, m1, '/party/kick', { target: m1.id })).body.errors.code).toBe('CANNOT_KICK_SELF');
    expect((await post(app, m1, '/party/kick', { target: randomUUID() })).body.errors.code).toBe('MEMBER_NOT_FOUND');
    expect((await post(app, m1, '/party/kick', { target: m2.id })).status).toBe(200);
    const kicked = await get(app, m2, '/party');
    expect(kicked.body.data.party).toBeNull();
    expect(kicked.body.data.notice.code).toBe('KICKED');
    // 방장이 나가면 남은 사람이 방장, 마지막 사람이 나가면 해산
    expect((await post(app, m1, '/party/leave', {})).status).toBe(200);
    const left = await get(app, host, '/party');
    expect(left.body.data.party.members).toHaveLength(1);
    expect(left.body.data.party.members[0].is_leader).toBe(true);
    expect((await post(app, host, '/party/leave', {})).body.data.party).toBeNull();
    expect((await get(app, host, '/party')).body.data.notice).toBeNull();
    expect((await post(app, host, '/party/leave', {})).body.errors.code).toBe('NOT_IN_PARTY');
    void partyId;
  });

  it('해산 알림: 마지막 사람이 나가면 파티가 닫히고 방치(30분) 파티는 지연 해산된다', async () => {
    const host = await newHero(app);
    await createParty(app, host);
    advance(31 * 60);
    const v = await get(app, host, '/party');
    expect(v.body.data.party).toBeNull();
    expect(v.body.data.notice.code).toBe('PARTY_CLOSED');
    const closed = await getPool().query("SELECT close_reason FROM parties WHERE leader_character_id = $1", [host.dbId]);
    expect((closed.rows[0] as { close_reason: string }).close_reason).toBe('idle_timeout');
  });

  it('재전송: 같은 request_id의 leave와 kick은 같은 응답', async () => {
    const host = await newHero(app);
    const m1 = await newHero(app);
    await formParty(app, host, [m1]);
    const rid = randomUUID();
    const a = await post(app, host, '/party/kick', { target: m1.id }, rid);
    const b = await post(app, host, '/party/kick', { target: m1.id }, rid);
    expect(b.body).toEqual(a.body);
    expect(b.headers['idempotent-replay']).toBe('true');
  });
});

describe('자동 매칭', () => {
  const queue = (h: Hero, over: Record<string, unknown> = {}, rid?: string) =>
    post(app, h, '/match/queue', { dungeon_id: 'gold_vein', difficulty: 0, ...over }, rid);

  it('정상: 비슷한 전투력 둘이 대기하다 60초가 지나면 한 파티(source=match)가 된다', async () => {
    const a = await newHero(app);
    const b = await newHero(app);
    const q = await queue(a);
    expect(q.status).toBe(200);
    expect(q.body.data.queue).toMatchObject({ dungeon_id: 'gold_vein', difficulty: 0, humans_waiting: 1, fill_ai_available: true });
    await queue(b);
    expect((await get(app, a, '/party')).body.data.queue.humans_waiting).toBe(2);
    advance(30);
    await get(app, a, '/party');
    await get(app, b, '/party');
    await runMatchTick();
    expect((await get(app, a, '/party')).body.data.party).toBeNull();
    advance(31);
    await get(app, a, '/party');
    await get(app, b, '/party');
    await runMatchTick();
    const va = await get(app, a, '/party');
    expect(va.body.data.queue).toBeNull();
    expect(va.body.data.party).toMatchObject({ source: 'match', listed: false });
    expect(va.body.data.party.members).toHaveLength(2);
    expect(va.body.data.party.members.every((m: { ready: boolean }) => m.ready)).toBe(true);
    expect(va.body.data.party.start_by).toBeTruthy();
    // 방장이 곧바로 출발할 수 있다(빈자리는 AI)
    const leader = va.body.data.party.members.find((m: { is_leader: boolean }) => m.is_leader).character_id === a.id ? a : b;
    expect((await post(app, leader, '/party/start', { ai_count: 2 })).status).toBe(201);
  });

  it('4명이 모이면 즉시 파티가 되고, 전투력이 30% 넘게 다르면 묶이지 않는다', async () => {
    const heroes: Hero[] = [];
    for (let i = 0; i < 4; i++) heroes.push(await newHero(app));
    for (const h of heroes) await queue(h);
    const v = await get(app, heroes[0] as Hero, '/party');
    expect(v.body.data.party.members).toHaveLength(4);

    const x = await newHero(app);
    const y = await newHero(app);
    await seedWorn(y, 0, 'eq_sword_dragon');
    await queue(x);
    await queue(y);
    advance(61);
    await get(app, x, '/party');
    await get(app, y, '/party');
    await runMatchTick();
    const px = (await get(app, x, '/party')).body.data.party;
    const py = (await get(app, y, '/party')).body.data.party;
    expect(px.members).toHaveLength(1);
    expect(py.members).toHaveLength(1);
    expect(px.id).not.toBe(py.id);
  });

  it('AI로 채워 출발: 대기자만으로 즉시 파티를 만들고, 재전송은 같은 파티', async () => {
    const a = await newHero(app);
    await queue(a);
    const rid = randomUUID();
    const f1 = await post(app, a, '/match/fill-ai', {}, rid);
    expect(f1.status).toBe(201);
    expect(f1.body.data.party).toMatchObject({ source: 'match' });
    const f2 = await post(app, a, '/match/fill-ai', {}, rid);
    expect(f2.body.data.party.id).toBe(f1.body.data.party.id);
    const none = await newHero(app);
    expect((await post(app, none, '/match/fill-ai', {})).body.errors.code).toBe('NOT_QUEUED');
  });

  it('규칙: 이미 파티가 있으면 대기 불가, 대기 중에는 파티를 만들 수 없고 취소는 멱등, 입력 오류', async () => {
    const a = await newHero(app);
    const b = await newHero(app);
    await createParty(app, b);
    expect((await queue(b)).body.errors.code).toBe('ALREADY_IN_PARTY');
    expect((await queue(a, { difficulty: 4 })).status).toBe(400);
    expect((await queue(a, { dungeon_id: 'smelter' })).body.errors.code).toBe('DUNGEON_CLOSED_TODAY');
    expect((await queue(a)).status).toBe(200);
    expect((await createParty(app, a)).body.errors.code).toBe('IN_QUEUE');
    expect((await request(app).delete(`/characters/${a.id}/match/queue`).set(auth(a.s))).body.data.cancelled).toBe(true);
    expect((await request(app).delete(`/characters/${a.id}/match/queue`).set(auth(a.s))).body.data.cancelled).toBe(true);
    expect((await get(app, a, '/party')).body.data.queue).toBeNull();
  });

  it('동시 요청: 같은 캐릭터의 대기 요청 두 개는 티켓 하나, 폴링이 끊긴 티켓은 버려진다', async () => {
    const a = await newHero(app);
    const [r1, r2] = await Promise.all([queue(a), queue(a)]);
    expect([r1.status, r2.status]).toEqual([200, 200]);
    expect(getQueueStore().listByKey('gold_vein', 0)).toHaveLength(1);
    advance(20);
    await runMatchTick();
    expect(getQueueStore().listByKey('gold_vein', 0)).toHaveLength(0);
  });
});
