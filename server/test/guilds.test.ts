import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { GUILD_CREATE_GOLD, GUILD_MAX_MEMBERS } from '../src/domains/guilds/guildRules';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { withTransaction } from '../src/db/pool';
import { detachCharacter } from '../src/domains/guilds/guildService';
import { expectLedgerConsistent, get, goldOf, newHero, post, seedGold, seedLevel, type Hero } from './economyHelpers';

const app = buildApp();
const START = '2026-10-20T03:00:00Z';
let now = new Date(START);
const advance = (seconds: number) => { now = new Date(now.getTime() + seconds * 1000); };
beforeAll(resetDb);
beforeEach(() => { now = new Date(START); setClockOverride(() => now); });
afterAll(async () => { setClockOverride(null); await shutdown(); });

async function hero(level = 20, gold = GUILD_CREATE_GOLD): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, level);
  if (gold > 0) await seedGold(h, gold);
  return h;
}
let seq = 0;
const uniq = () => `길드${++seq}${randomUUID().slice(0, 4)}`.replace(/-/g, '').slice(0, 12);
const create = (h: Hero, name = uniq(), joinMode = 'open') => post(app, h, '/guild', { name, join_mode: joinMode });
const status = (h: Hero) => get(app, h, '/guild');
const apply = (h: Hero, guildId: string) => post(app, h, '/guild/apply', { guild_id: guildId });
async function guildOf(leader: Hero, joinMode = 'open') {
  const r = await create(leader, uniq(), joinMode);
  expect(r.status).toBe(201);
  return r.body.data.guild as { id: string; name: string };
}

describe('guild create', () => {
  test('costs gold, needs Lv20, unique name', async () => {
    const low = await hero(19);
    const r1 = await create(low);
    expect(r1.status).toBe(422);
    expect(r1.body.errors.code).toBe('LEVEL_TOO_LOW');

    const poor = await hero(20, 0);
    const r2 = await create(poor);
    expect(r2.status).toBe(422);

    const a = await hero();
    const name = uniq();
    const before = await goldOf(a);
    const ok = await create(a, name);
    expect(ok.status).toBe(201);
    expect(ok.body.data.guild).toMatchObject({ name, my_role: 'leader', member_count: 1, max_members: GUILD_MAX_MEMBERS });
    expect(await goldOf(a)).toBe(before - GUILD_CREATE_GOLD);
    await expectLedgerConsistent(a);

    const b = await hero();
    const bBefore = await goldOf(b);
    const dup = await create(b, name);
    expect(dup.status).toBe(409);
    expect(await goldOf(b)).toBe(bBefore);
    // 이미 길드가 있으면 또 만들 수 없다
    await seedGold(a, GUILD_CREATE_GOLD);
    expect((await create(a)).status).toBe(409);
  });

  test('same request_id replays without charging twice', async () => {
    const a = await hero(20, GUILD_CREATE_GOLD * 2);
    const before = await goldOf(a);
    const id = randomUUID();
    const name = uniq();
    const r1 = await post(app, a, '/guild', { name, join_mode: 'open' }, id);
    const r2 = await post(app, a, '/guild', { name, join_mode: 'open' }, id);
    expect(r1.status).toBe(201);
    expect(r2.status).toBe(201);
    expect(await goldOf(a)).toBe(before - GUILD_CREATE_GOLD);
  });
});

describe('guild membership', () => {
  test('open guild joins at once, approval guild waits for an officer', async () => {
    const leader = await hero();
    const open = await guildOf(leader, 'open');
    const m = await hero(1, 0);
    const j = await apply(m, open.id);
    expect(j.status).toBe(200);
    expect(j.body.data.joined).toBe(true);
    expect(j.body.data.guild.my_role).toBe('member');

    const leader2 = await hero();
    const closed = await guildOf(leader2, 'approval');
    const c = await hero(1, 0);
    const a = await apply(c, closed.id);
    expect(a.body.data.joined).toBe(false);
    expect(a.body.data.my_applications).toHaveLength(1);
    // 길드원은 신청 목록을 보지 못하고, 길드장은 본다
    const s = await status(leader2);
    expect(s.body.data.guild.applications).toHaveLength(1);
    const appId = s.body.data.guild.applications[0].id;
    const acc = await post(app, leader2, `/guild/applications/${appId}/accept`, {});
    expect(acc.status).toBe(200);
    expect(acc.body.data.guild.member_count).toBe(2);
    expect((await status(c)).body.data.guild.name).toBe(closed.name);
  });

  test('application limit is 3 at once', async () => {
    const guilds = [];
    for (let i = 0; i < 4; i++) guilds.push(await guildOf(await hero(), 'approval'));
    const c = await hero(1, 0);
    for (let i = 0; i < 3; i++) expect((await apply(c, guilds[i]!.id)).status).toBe(200);
    const r = await apply(c, guilds[3]!.id);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('GUILD_APPLY_LIMIT');
  });

  test('roles: officer kicks members only, leader promotes up to 3 officers, transfer', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    const ms: Hero[] = [];
    for (let i = 0; i < 5; i++) { const h = await hero(1, 0); await apply(h, g.id); ms.push(h); }
    const ids = (await status(leader)).body.data.guild.members.filter((x: { role: string }) => x.role === 'member').map((x: { character_id: string }) => x.character_id);
    for (let i = 0; i < 3; i++) expect((await post(app, leader, `/guild/members/${ids[i]}/promote`, {})).status).toBe(200);
    const fourth = await post(app, leader, `/guild/members/${ids[3]}/promote`, {});
    expect(fourth.status).toBe(409);
    // 부길드장(ms[0])은 길드원을 내보낼 수 있지만 다른 부길드장은 못 한다
    expect((await post(app, ms[0]!, `/guild/members/${ids[1]}/kick`, {})).status).toBe(403);
    expect((await post(app, ms[0]!, `/guild/members/${ids[4]}/kick`, {})).status).toBe(200);
    // 길드원은 승급 못 한다
    expect((await post(app, ms[3]!, `/guild/members/${ids[3]}/promote`, {})).status).toBe(409);
    // 위임: 받은 사람이 길드장, 이전 길드장은 부길드장 자리가 꽉 차서 길드원
    const t = await post(app, leader, `/guild/members/${ids[3]}/transfer`, {});
    expect(t.status).toBe(200);
    expect(t.body.data.guild.my_role).toBe('member');
    expect((await status(ms[3]!)).body.data.guild.my_role).toBe('leader');
  });

  test('leaving puts a 24 hour cooldown, kicked too', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    const other = await guildOf(await hero());
    const m = await hero(1, 0);
    await apply(m, g.id);
    expect((await post(app, leader, '/guild/leave', {})).status).toBe(409); // 길드장은 위임 또는 해체
    const l = await post(app, m, '/guild/leave', {});
    expect(l.status).toBe(200);
    expect(l.body.data.guild).toBeNull();
    expect(l.body.data.cooldown_until).not.toBeNull();
    const again = await apply(m, other.id);
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('GUILD_COOLDOWN');
    advance(24 * 3600 + 1);
    expect((await apply(m, other.id)).body.data.joined).toBe(true);
  });

  test('full guild rejects, disband only when alone', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    await getPool().query('UPDATE guilds SET member_count = $2 WHERE uuid = $1', [g.id, GUILD_MAX_MEMBERS]);
    const late = await hero(1, 0);
    const r = await apply(late, g.id);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('GUILD_FULL');
    await getPool().query('UPDATE guilds SET member_count = 2 WHERE uuid = $1', [g.id]);
    expect((await post(app, leader, '/guild/disband', {})).status).toBe(409);
    await getPool().query('UPDATE guilds SET member_count = 1 WHERE uuid = $1', [g.id]);
    const d = await post(app, leader, '/guild/disband', {});
    expect(d.status).toBe(200);
    expect(d.body.data.guild).toBeNull();
    // 해체한 이름은 다시 쓸 수 있다
    const again = await hero();
    expect((await create(again, g.name)).status).toBe(201);
  });

  test('search lists alive guilds by name', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    const s = await request(app).get(`/guilds?query=${encodeURIComponent(g.name)}`).set(auth(leader.s));
    expect(s.status).toBe(200);
    expect(s.body.data.guilds.map((x: { id: string }) => x.id)).toContain(g.id);
    expect(s.body.data.guilds[0]).toMatchObject({ member_count: 1, max_members: GUILD_MAX_MEMBERS });
  });

  test('notice is masked and only managers change settings', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    const m = await hero(1, 0);
    await apply(m, g.id);
    expect((await request(app).patch(`/characters/${m.id}/guild`).set(auth(m.s)).send({ request_id: randomUUID(), notice: 'hi' })).status).toBe(403);
    const p = await request(app).patch(`/characters/${leader.id}/guild`).set(auth(leader.s)).send({ request_id: randomUUID(), notice: '매주 목요일 길드보스', join_mode: 'approval' });
    expect(p.status).toBe(200);
    expect(p.body.data.guild).toMatchObject({ notice: '매주 목요일 길드보스', join_mode: 'approval' });
  });
});

describe('guild and character removal', () => {
  const del = (h: Hero) => request(app).delete(`/characters/${h.id}`).set(auth(h.s));
  const countOf = async (guildId: string) => Number((await getPool().query<{ n: string }>('SELECT member_count AS n FROM guilds WHERE uuid = $1', [guildId])).rows[0]?.n);

  test('a leader cannot delete the character before transferring; a member leaves on delete', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    const m = await hero(1, 0);
    await apply(m, g.id);
    const other = await guildOf(await hero(), 'approval');
    await apply(m, other.id); // 대기 신청도 같이 지워진다
    expect(await countOf(g.id)).toBe(2);
    const refused = await del(leader);
    expect(refused.status).toBe(409);
    expect(refused.body.errors.code).toBe('GUILD_LEADER_MUST_TRANSFER');
    expect((await status(leader)).body.data.guild.my_role).toBe('leader');
    const ok = await del(m);
    expect(ok.status).toBe(200);
    expect(await countOf(g.id)).toBe(1);
    const left = await getPool().query('SELECT 1 FROM guild_members WHERE character_id = $1', [m.dbId]);
    expect(left.rows).toEqual([]);
    const apps = await getPool().query('SELECT 1 FROM guild_applications WHERE character_id = $1', [m.dbId]);
    expect(apps.rows).toEqual([]);
    // 이제 혼자라 해체할 수 있고, 그 뒤 삭제된다
    expect((await post(app, leader, '/guild/disband', {})).status).toBe(200);
    expect((await del(leader)).status).toBe(200);
  });

  test('withdrawal mode hands the guild to the oldest officer, then member, and disbands when alone', async () => {
    const leader = await hero();
    const g = await guildOf(leader);
    const officer = await hero(1, 0);
    const member = await hero(1, 0);
    await apply(officer, g.id);
    advance(10);
    await apply(member, g.id);
    expect((await post(app, leader, `/guild/members/${officer.id}/promote`, {})).status).toBe(200);
    await withTransaction((c) => detachCharacter(c, leader.dbId, now, 'auto'));
    expect(await countOf(g.id)).toBe(2);
    expect((await status(officer)).body.data.guild.my_role).toBe('leader');
    await withTransaction((c) => detachCharacter(c, officer.dbId, now, 'auto'));
    expect((await status(member)).body.data.guild.my_role).toBe('leader');
    expect(await countOf(g.id)).toBe(1);
    await withTransaction((c) => detachCharacter(c, member.dbId, now, 'auto'));
    const row = await getPool().query<{ disbanded_at: Date | null; member_count: number }>('SELECT disbanded_at, member_count FROM guilds WHERE uuid = $1', [g.id]);
    expect(row.rows[0]?.disbanded_at).not.toBeNull();
    expect((await status(member)).body.data.guild).toBeNull();
  });
});
