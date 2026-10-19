import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import {
  GUILD_STAGE1_HP, WORLD_DEFEAT_GOLD, WORLD_JOIN_GOLD, applyGuildDamage, attemptDamageCap, stageHp,
} from '../src/domains/bossrush/bossRushRules';
import { settleDueSeasons } from '../src/domains/bossrush/bossRushSettle';
import { clearRankingCache } from '../src/domains/rankings/rankingService';
import { GUILD_CREATE_GOLD } from '../src/domains/guilds/guildRules';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { anomalyKinds, get, newHero, post, seedGold, seedLevel, type Hero } from './economyHelpers';

const app = buildApp();
// 화요일 KST 12:00 (주는 목요일 06:00 KST에 바뀐다)
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
const start = (h: Hero, kind = 'world', requestId?: string) => post(app, h, `/bosses/${kind}/attempts`, {}, requestId);
const finish = (h: Hero, id: string, damage: number, kind = 'world', requestId?: string) =>
  post(app, h, `/bosses/${kind}/attempts/${id}/finish`, { damage_dealt: damage, hits_landed: damage > 0 ? 50 : 0, duration_s: 180 }, requestId);
/** 3분 싸운 한 번: 시작 -> 3분 -> 끝 */
async function fight(h: Hero, damage: number, kind = 'world') {
  const s = await start(h, kind);
  expect(s.status).toBe(201);
  advance(180);
  const f = await finish(h, s.body.data.attempt_id, damage, kind);
  expect(f.status).toBe(200);
  return f.body.data;
}

describe('boss rules', () => {
  test('guild damage carries over into the next stage and stops at stage 10', () => {
    expect(applyGuildDamage(1, 100, 50)).toMatchObject({ stage: 1, hpLeft: 50, advanced: false });
    const over = applyGuildDamage(1, 100, 100 + 10);
    expect(over).toMatchObject({ stage: 2, hpLeft: stageHp(2) - 10, advanced: true });
    expect(applyGuildDamage(10, 5, 1_000_000)).toMatchObject({ stage: 10, hpLeft: 0 });
    expect(stageHp(1)).toBe(GUILD_STAGE1_HP);
    expect(attemptDamageCap(100, 500)).toBe(100 * 180 * 1.5);
  });
});

describe('world boss attempts', () => {
  test('status creates the week and shows 3 attempts', async () => {
    const h = await hero();
    const r = await get(app, h, '/bosses');
    expect(r.status).toBe(200);
    expect(r.body.data.world).toMatchObject({ boss_id: 'boss_abyss_titan', max_hp: 16_000_000, hp_left: 16_000_000, attempts_left: 3, my_best: 0, my_rank: null });
    expect(r.body.data.guild).toBeNull();
    expect(r.body.data.week_ends_at).toBe('2026-10-21T21:00:00.000Z');
  });

  test('below max level cannot start', async () => {
    const h = await hero(39);
    const r = await start(h);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('LEVEL_TOO_LOW');
  });

  test('one open attempt, single finish, idempotent replay', async () => {
    const h = await hero();
    const s = await start(h);
    expect(s.body.data).toMatchObject({ boss_id: 'boss_abyss_titan', level: 40, damage_mul: 1 });
    const again = await start(h);
    expect(again.status).toBe(409);
    expect(again.body.errors).toMatchObject({ code: 'BOSS_ATTEMPT_OPEN', attempt_id: s.body.data.attempt_id });
    advance(180);
    const rid = randomUUID();
    const f1 = await finish(h, s.body.data.attempt_id, 12_000, 'world', rid);
    expect(f1.status).toBe(200);
    expect(f1.body.data).toMatchObject({ accepted_damage: 12_000, capped: false, best: 12_000, hp_left: 16_000_000 - 12_000, rank: 1, defeated: false });
    const replay = await finish(h, s.body.data.attempt_id, 12_000, 'world', rid);
    expect(replay.body).toEqual(f1.body);
    const twice = await finish(h, s.body.data.attempt_id, 12_000);
    expect(twice.status).toBe(409);
    expect(twice.body.errors.code).toBe('BOSS_ATTEMPT_CLOSED');
  });

  test('3 attempts a day, resets at 06:00 KST', async () => {
    const h = await hero();
    for (let i = 0; i < 3; i++) await fight(h, 1000);
    const r = await start(h);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('BOSS_NO_ATTEMPTS');
    now = new Date('2026-10-20T21:00:01Z'); // 다음 날 06:00 KST
    expect((await start(h)).status).toBe(201);
  });

  test('damage over the power cap is cut and logged', async () => {
    const h = await hero();
    const d = await fight(h, 900_000_000);
    expect(d.capped).toBe(true);
    expect(d.accepted_damage).toBeGreaterThan(0);
    expect(d.accepted_damage).toBeLessThan(900_000_000);
    expect(await anomalyKinds(h)).toContain('boss_damage');
    // 맞힌 횟수 없이 온 피해는 받지 않는다
    const s = await start(h);
    advance(180);
    const z = await post(app, h, `/bosses/world/attempts/${s.body.data.attempt_id}/finish`, { damage_dealt: 5000, hits_landed: 0, duration_s: 180 });
    expect(z.body.data.accepted_damage).toBe(0);
  });

  test('a late report closes the attempt with no damage', async () => {
    const h = await hero();
    const s = await start(h);
    advance(180 + 61);
    const f = await finish(h, s.body.data.attempt_id, 5000);
    expect(f.status).toBe(200);
    expect(f.body.data).toMatchObject({ accepted_damage: 0, expired: true });
    // 기한이 지난 열린 도전은 다음 시작 때 자동으로 닫힌다
    const s2 = await start(h);
    advance(500);
    expect((await start(h)).status).toBe(201);
    const left = await getPool().query("SELECT state FROM boss_attempts WHERE uuid = $1", [s2.body.data.attempt_id]);
    expect(left.rows[0]?.state).toBe('expired');
  });
});

async function inGuild(): Promise<{ leader: Hero; guildId: string }> {
  const leader = await hero();
  await seedGold(leader, GUILD_CREATE_GOLD);
  const g = await post(app, leader, '/guild', { name: `보스${randomUUID().slice(0, 6)}`.replace(/-/g, ''), join_mode: 'open' });
  expect(g.status).toBe(201);
  return { leader, guildId: g.body.data.guild.id };
}

describe('guild boss attempts', () => {
  test('needs a guild and 24 hours in it, stages advance', async () => {
    const solo = await hero();
    expect((await start(solo, 'guild')).body.errors.code).toBe('NOT_IN_GUILD');
    const { leader } = await inGuild();
    const early = await start(leader, 'guild');
    expect(early.status).toBe(409);
    expect(early.body.errors.code).toBe('GUILD_BOSS_NOT_YET');
    advance(24 * 3600);
    const st = await get(app, leader, '/bosses');
    expect(st.body.data.guild).toMatchObject({ stage: 1, max_hp: GUILD_STAGE1_HP, hp_left: GUILD_STAGE1_HP, attempts_left: 2 });
    await getPool().query('UPDATE guild_boss_progress SET hp_left = 1000');
    const d = await fight(leader, 3000, 'guild');
    expect(d).toMatchObject({ accepted_damage: 3000, stage: 2, hp_left: stageHp(2) - 2000, defeated: true, rank: 1 });
    const s2 = await start(leader, 'guild');
    expect(s2.body.data).toMatchObject({ stage: 2, damage_mul: 1.15 });
  });
});

describe('weekly settlement', () => {
  async function mails(h: Hero, code: string) {
    const r = await getPool().query<{ gold: string; item_key: string | null; count: number | null }>(
      'SELECT gold, item_key, count FROM mails WHERE character_id = $1 AND system_code = $2 ORDER BY id', [h.dbId, code]);
    return r.rows.map((x) => ({ gold: Number(x.gold), item: x.item_key, count: x.count }));
  }

  test('world: join rewards, defeat bonus, top titles, only once', async () => {
    const a = await hero();
    const b = await hero();
    await fight(a, 20_000);
    await getPool().query("UPDATE boss_seasons SET hp_left = 5000 WHERE kind = 'world'");
    await fight(b, 10_000);
    now = new Date('2026-10-21T21:05:00Z'); // 목요일 06:05 KST
    const r1 = await settleDueSeasons();
    expect(r1.seasons).toBe(1);
    expect(await mails(a, 'world_boss')).toEqual([
      { gold: WORLD_JOIN_GOLD + WORLD_DEFEAT_GOLD, item: null, count: null },
      { gold: 0, item: 'mat_ore', count: 5 },
    ]);
    const titles = await getPool().query<{ character_id: string; title_id: string; name_color: string | null }>(
      'SELECT character_id, title_id, name_color FROM rank_titles ORDER BY character_id');
    expect(titles.rows.map((t) => [Number(t.character_id), t.title_id, t.name_color])).toEqual([
      [a.dbId, 'rank_world_1', 'gold'],
      [b.dbId, 'rank_world_10', 'orange'],
    ].sort((x, y) => (x[0] as number) - (y[0] as number)));
    const r2 = await settleDueSeasons();
    expect(r2.seasons).toBe(0);
    expect(await mails(a, 'world_boss')).toHaveLength(2);

    // 랭킹 칭호를 칭호 자리에 단다
    const eq = await request(app).post(`/characters/${a.id}/title`).set(auth(a.s)).send({ achievement_id: 'rank_world_1' });
    expect(eq.status).toBe(200);
    expect(eq.body.data.title).toEqual({ id: 'rank_world_1', name: '세계를 꺾은 자' });
    expect((await request(app).post(`/characters/${b.id}/title`).set(auth(b.s)).send({ achievement_id: 'rank_world_1' })).status).toBe(409);
    // 다음 정산 뒤 만료되면 떼어 낸다
    now = new Date('2026-10-28T23:00:00Z');
    await getPool().query("INSERT INTO boss_seasons (kind, week_start, week_end, boss_id, max_hp, hp_left) VALUES ('world', '2026-10-21T21:00:00Z', '2026-10-28T21:00:00Z', 'boss_abyss_titan', 10, 10)");
    await settleDueSeasons();
    const t = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(t.rows[0]?.title_achievement).toBeNull();
  });

  test('guild: stage gold for participants, top guild title and gold name', async () => {
    const { leader, guildId } = await inGuild();
    const m = await hero();
    await post(app, m, '/guild/apply', { guild_id: guildId });
    advance(24 * 3600);
    await get(app, leader, '/bosses'); // 이번 주 길드 진행 행을 만든다
    await getPool().query('UPDATE guild_boss_progress SET hp_left = 100');
    await fight(leader, 500, 'guild');
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    expect(await mails(leader, 'guild_boss')).toEqual([{ gold: 2 * 10_000, item: null, count: null }]);
    expect(await mails(m, 'guild_boss')).toEqual([]); // 도전하지 않은 길드원
    const g = await getPool().query("SELECT honor_color FROM guilds WHERE uuid = $1", [guildId]);
    expect(g.rows[0]?.honor_color).toBe('gold');
    const t = await getPool().query("SELECT count(*)::int AS n FROM rank_titles WHERE title_id = 'rank_guild_1'");
    expect(t.rows[0]?.n).toBe(2);
    const view = await get(app, m, '/guild');
    expect(view.body.data.guild.honor_color).toBe('gold');
  });
});
