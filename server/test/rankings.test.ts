import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { setClockOverride } from '../src/utils/clock';
import { clearRankingCache } from '../src/domains/rankings/rankingService';
import { GUILD_CREATE_GOLD } from '../src/domains/guilds/guildRules';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { newHero, post, seedGold, seedLevel, seedWorn, type Hero } from './economyHelpers';

const app = buildApp();
const START = '2026-10-20T03:00:00Z';
let now = new Date(START);
const advance = (seconds: number) => { now = new Date(now.getTime() + seconds * 1000); };
beforeEach(async () => { await resetDb(); clearRankingCache(); now = new Date(START); setClockOverride(() => now); });
afterAll(async () => { setClockOverride(null); await shutdown(); });

async function hero(level = 40): Promise<Hero> {
  const h = await newHero(app);
  await seedLevel(h, level);
  return h;
}
async function fight(h: Hero, damage: number, kind = 'world') {
  const s = await post(app, h, `/bosses/${kind}/attempts`, {});
  expect(s.status).toBe(201);
  advance(180);
  const f = await post(app, h, `/bosses/${kind}/attempts/${s.body.data.attempt_id}/finish`, { damage_dealt: damage, hits_landed: 10, duration_s: 180 });
  expect(f.status).toBe(200);
}
const board = (h: Hero, name: string, extra = '') => request(app).get(`/rankings/${name}?character_id=${h.id}${extra}`).set(auth(h.s));

describe('rankings', () => {
  test('world boss: best single attempt, ties go to whoever got there first', async () => {
    const a = await hero();
    const b = await hero();
    const c = await hero();
    await fight(a, 5000);
    await fight(a, 9000); // a의 최고 9000
    await fight(b, 9000); // 같은 값, 나중
    await fight(c, 12_000);
    const r = await board(b, 'world_boss');
    expect(r.status).toBe(200);
    expect(r.body.data.entries.map((e: { value: number; rank: number }) => [e.rank, e.value])).toEqual([[1, 12_000], [2, 9000], [3, 9000]]);
    expect(r.body.data.me).toEqual({ rank: 3, value: 9000 });
    expect(r.body.data.entries[0]).toMatchObject({ level: 40, guild_name: null, name_color: null });
    // 지난주는 비어 있다
    const prev = await board(b, 'world_boss', '&week=previous');
    expect(prev.body.data.entries).toEqual([]);
  });

  test('guild boss: score first, then who reached it first', async () => {
    const guilds: { leader: Hero; id: string; name: string }[] = [];
    for (let i = 0; i < 3; i++) {
      const leader = await hero();
      await seedGold(leader, GUILD_CREATE_GOLD);
      const name = `랭킹${i}${randomUUID().slice(0, 4)}`;
      const g = await post(app, leader, '/guild', { name, join_mode: 'open' });
      expect(g.status).toBe(201);
      guilds.push({ leader, id: g.body.data.guild.id, name });
    }
    advance(24 * 3600);
    await fight(guilds[0]!.leader, 1000, 'guild');
    await fight(guilds[1]!.leader, 2000, 'guild');
    await fight(guilds[2]!.leader, 500, 'guild');
    await fight(guilds[2]!.leader, 500, 'guild'); // 1000점에 0번 길드보다 늦게 도달
    clearRankingCache();
    const r = await board(guilds[0]!.leader, 'guild_boss');
    expect(r.body.data.entries.map((e: { guild_name: string; value: number }) => [e.guild_name, e.value])).toEqual([
      [guilds[1]!.name, 2000], [guilds[0]!.name, 1000], [guilds[2]!.name, 1000],
    ]);
    expect(r.body.data.me).toEqual({ rank: 2, value: 1000 });
  });

  test('power board is computed from worn gear, cached for a minute', async () => {
    const a = await hero(30);
    const b = await hero(30);
    await seedWorn(b, 0, 'eq_sword_wood+12');
    const r = await board(a, 'power');
    expect(r.status).toBe(200);
    const values = r.body.data.entries.map((e: { value: number }) => e.value);
    expect(values[0]).toBeGreaterThan(values[1]);
    expect(r.body.data.me.rank).toBe(2);
  });

  test('cannot ask for someone else\'s character', async () => {
    const a = await hero();
    const b = await hero();
    const r = await request(app).get(`/rankings/power?character_id=${b.id}`).set(auth(a.s));
    expect(r.status).toBe(404);
  });
});
