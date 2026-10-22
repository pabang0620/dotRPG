import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import {
  GUILD_JOIN_GOLD, WORLD_DEFEAT_GOLD, WORLD_JOIN_GOLD, attemptDamageCap, DAMAGE_SLACK,
} from '../src/domains/bossrush/bossRushRules';
import { settleDueSeasons } from '../src/domains/bossrush/bossRushSettle';
import { clearRankingCache } from '../src/domains/rankings/rankingService';
import { GUILD_CREATE_GOLD } from '../src/domains/guilds/guildRules';
import { auth, buildApp, openDungeonToday, resetDb, shutdown } from './helpers';
import { formParty } from './partyHelpers';
import { unequipExpiredRankTitles } from '../src/domains/bossrush/bossRushSettle';
import { anomalyKinds, get, newHero, post, seedGold, seedLevel, type Hero } from './economyHelpers';
import { CHAPTER2_LEVEL_CAP } from '../src/gamedata/chapterGate';

const app = buildApp();
// 화요일 KST 12:00 (주는 목요일 06:00 KST에 바뀐다)
const START = '2026-10-20T03:00:00Z';
let now = new Date(START);
const advance = (seconds: number) => { now = new Date(now.getTime() + seconds * 1000); };
beforeEach(async () => { await resetDb(); clearRankingCache(); now = new Date(START); setClockOverride(() => now); });
afterAll(async () => { setClockOverride(null); await shutdown(); });

async function hero(level = CHAPTER2_LEVEL_CAP): Promise<Hero> {
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
  test('damage cap is per second x at most 3 minutes x slack', () => {
    expect(attemptDamageCap(100, 500)).toBe(100 * 180 * DAMAGE_SLACK);
  });
});

describe('world boss attempts', () => {
  test('status creates the week and shows 3 attempts', async () => {
    const h = await hero();
    const r = await get(app, h, '/bosses');
    expect(r.status).toBe(200);
    expect(r.body.data.world).toMatchObject({ boss_id: 'boss_abyss_titan', max_hp: 40_000_000, hp_left: 40_000_000, attempts_left: 3, my_best: 0, my_rank: null });
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
    expect(s.body.data).toMatchObject({ boss_id: 'boss_abyss_titan', level: CHAPTER2_LEVEL_CAP, damage_mul: 1 });
    const again = await start(h);
    expect(again.status).toBe(409);
    expect(again.body.errors).toMatchObject({ code: 'BOSS_ATTEMPT_OPEN', attempt_id: s.body.data.attempt_id });
    advance(180);
    const rid = randomUUID();
    const f1 = await finish(h, s.body.data.attempt_id, 12_000, 'world', rid);
    expect(f1.status).toBe(200);
    expect(f1.body.data).toMatchObject({ accepted_damage: 12_000, capped: false, best: 12_000, hp_left: 40_000_000 - 12_000, rank: 1, defeated: false });
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

  test('a human party cannot start (solo + AI only)', async () => {
    const a = await hero();
    const b = await hero();
    await formParty(app, a, [b], { dungeon_id: openDungeonToday() });
    const r = await start(a);
    expect(r.status).toBe(409);
    expect(r.body.errors.code).toBe('BOSS_PARTY_NOT_ALLOWED');
    expect((await start(b)).body.errors.code).toBe('BOSS_PARTY_NOT_ALLOWED');
  });

  test('a report at 80% of the cap is accepted but logged', async () => {
    const h = await hero();
    const s = await start(h);
    advance(180);
    // 상한을 알기 위해 먼저 한 번 넘겨 본다(그 보고는 잘린다)
    const over = (await finish(h, s.body.data.attempt_id, 900_000_000)).body.data;
    const cap = over.accepted_damage as number;
    const s2 = await start(h);
    advance(180);
    const d = (await finish(h, s2.body.data.attempt_id, Math.floor(cap * 0.9))).body.data;
    expect(d.capped).toBe(false);
    expect(d.accepted_damage).toBe(Math.floor(cap * 0.9));
    expect(await anomalyKinds(h)).toEqual(expect.arrayContaining(['boss_damage', 'boss_damage_high']));
  });

  test('a finish after the season was settled keeps hp_left', async () => {
    const h = await hero();
    const s = await start(h);
    await getPool().query("UPDATE boss_seasons SET settled_at = now() WHERE kind = 'world'");
    advance(180);
    const d = (await finish(h, s.body.data.attempt_id, 7000)).body.data;
    expect(d.accepted_damage).toBe(7000);
    const hp = await getPool().query<{ hp_left: string; max_hp: string }>("SELECT hp_left, max_hp FROM boss_seasons WHERE kind = 'world'");
    expect(hp.rows[0]?.hp_left).toBe(hp.rows[0]?.max_hp);
    expect(d.hp_left).toBe(Number(hp.rows[0]?.max_hp));
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
  test('needs a guild and 24 hours in it, accepted damage adds up as the guild score', async () => {
    const solo = await hero();
    expect((await start(solo, 'guild')).body.errors.code).toBe('NOT_IN_GUILD');
    const { leader } = await inGuild();
    const early = await start(leader, 'guild');
    expect(early.status).toBe(409);
    expect(early.body.errors.code).toBe('GUILD_BOSS_NOT_YET');
    advance(24 * 3600);
    const st = await get(app, leader, '/bosses');
    expect(st.body.data.guild).toMatchObject({ score: 0, my_contribution: 0, attempts_left: 2, guild_rank: null });
    expect(st.body.data.guild.stage).toBeUndefined();
    const d = await fight(leader, 3000, 'guild');
    expect(d).toMatchObject({ accepted_damage: 3000, score: 3000, my_contribution: 3000, rank: 1 });
    expect(d.defeated).toBeUndefined();
    const s2 = await start(leader, 'guild');
    expect(s2.body.data).toMatchObject({ damage_mul: 1 });
    expect(s2.body.data.stage).toBeUndefined();
    advance(180);
    const d2 = (await finish(leader, s2.body.data.attempt_id, 2000, 'guild')).body.data;
    expect(d2).toMatchObject({ accepted_damage: 2000, score: 5000, my_contribution: 5000 });
    clearRankingCache(); // 상태의 순위는 1분 캐시(끝 보고 응답의 rank는 바로 계산)
    const st2 = await get(app, leader, '/bosses');
    expect(st2.body.data.guild).toMatchObject({ score: 5000, my_contribution: 5000, attempts_left: 0, guild_rank: 1 });
  });
});

describe('guild boss and leaving the guild', () => {
  test('a finish after leaving the guild adds to no guild score', async () => {
    const { leader, guildId } = await inGuild();
    const m = await hero();
    await post(app, m, '/guild/apply', { guild_id: guildId });
    advance(24 * 3600);
    const s = await start(m, 'guild');
    expect(s.status).toBe(201);
    expect((await post(app, m, '/guild/leave', {})).status).toBe(200);
    advance(180);
    const d = (await finish(m, s.body.data.attempt_id, 4000, 'guild')).body.data;
    expect(d).toMatchObject({ accepted_damage: 4000, score: 0, my_contribution: 0, guild_changed: true, rank: null });
    const sc = await getPool().query<{ score: string }>('SELECT score FROM guild_boss_scores');
    expect(sc.rows).toEqual([]);
    const row = await getPool().query<{ guild_id: string | null }>('SELECT guild_id FROM boss_attempts WHERE uuid = $1', [s.body.data.attempt_id]);
    expect(row.rows[0]?.guild_id).toBeNull();
    // 길드장의 기록은 그대로
    const d2 = await fight(leader, 1000, 'guild');
    expect(d2).toMatchObject({ score: 1000, my_contribution: 1000, rank: 1 });
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

  test('guild: join gold for participants once, top guild title and gold name', async () => {
    const { leader, guildId } = await inGuild();
    const m = await hero();
    await post(app, m, '/guild/apply', { guild_id: guildId });
    advance(24 * 3600);
    await fight(leader, 500, 'guild');
    await fight(leader, 700, 'guild');
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    expect(await mails(leader, 'guild_boss')).toEqual([{ gold: GUILD_JOIN_GOLD, item: null, count: null }]);
    expect(await mails(m, 'guild_boss')).toEqual([]); // 도전하지 않은 길드원
    const g = await getPool().query("SELECT honor_color FROM guilds WHERE uuid = $1", [guildId]);
    expect(g.rows[0]?.honor_color).toBe('gold');
    // 1위 칭호는 그 주에 그 길드로 도전한 사람만: 도전 안 한 길드원 m은 못 받는다
    const t = await getPool().query<{ character_id: string }>("SELECT character_id FROM rank_titles WHERE title_id = 'rank_guild_1'");
    expect(t.rows.map((x) => Number(x.character_id))).toEqual([leader.dbId]);
    const view = await get(app, m, '/guild');
    expect(view.body.data.guild.honor_color).toBe('gold');
  });

  test('guild: top title goes to the week\'s fighters, not to who joined after the week', async () => {
    const { leader, guildId } = await inGuild();
    const fighter = await hero();
    await post(app, fighter, '/guild/apply', { guild_id: guildId });
    advance(24 * 3600);
    await fight(leader, 500, 'guild');
    await fight(fighter, 800, 'guild');
    // 기여한 뒤 떠난 사람도 받는다
    expect((await post(app, fighter, '/guild/leave', {})).status).toBe(200);
    now = new Date('2026-10-21T21:02:00Z'); // 주가 끝난 뒤, 정산 전
    const late = await hero();
    expect((await post(app, late, '/guild/apply', { guild_id: guildId })).body.data.joined).toBe(true);
    await settleDueSeasons();
    const t = await getPool().query<{ character_id: string }>("SELECT character_id FROM rank_titles WHERE title_id = 'rank_guild_1' ORDER BY character_id");
    expect(t.rows.map((x) => Number(x.character_id)).sort((a, b) => a - b)).toEqual([leader.dbId, fighter.dbId].sort((a, b) => a - b));
  });

  test('an expired rank title is unequipped by the daily job and hidden by the achievements view', async () => {
    const a = await hero();
    await fight(a, 20_000);
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    expect((await request(app).post(`/characters/${a.id}/title`).set(auth(a.s)).send({ achievement_id: 'rank_world_1' })).status).toBe(200);
    // 만료(week_end + 7일 + 1시간) 하루 뒤, 다음 정산은 아직
    now = new Date('2026-10-29T23:00:00Z');
    const before = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(before.rows[0]?.title_achievement).toBe('rank_world_1');
    expect(await unequipExpiredRankTitles()).toBe(1);
    const after = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(after.rows[0]?.title_achievement).toBeNull();
    // 작업 전이라도 업적 조회는 만료 칭호를 비워서 보여 주고 뗀다
    await getPool().query("UPDATE characters SET title_achievement = 'rank_world_1' WHERE id = $1", [a.dbId]);
    const list = await request(app).get(`/characters/${a.id}/achievements`).set(auth(a.s));
    expect(list.status).toBe(200);
    expect(list.body.data.title).toBeNull();
    expect(list.body.data.rank_titles).toEqual([]);
    const again = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(again.rows[0]?.title_achievement).toBeNull();
  });
});
