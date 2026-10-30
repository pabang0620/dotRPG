import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import {
  GUILD_JOIN_GOLD, WORLD_DEFEAT_GOLD, WORLD_JOIN_GOLD, WORLD_JOIN_ORE, WORLD_SEASON_ANCHOR_MS, attemptDamageCap, DAMAGE_SLACK, guildTierReward,
  worldSeasonBounds,
} from '../src/domains/bossrush/bossRushRules';
import { settleDueSeasons } from '../src/domains/bossrush/bossRushSettle';
import { clearRankingCache } from '../src/domains/rankings/rankingService';
import { GUILD_CREATE_GOLD } from '../src/domains/guilds/guildRules';
import { auth, buildApp, createChar, openDungeonToday, randomName, resetDb, shutdown } from './helpers';
import { formParty } from './partyHelpers';
import { unequipExpiredRankTitles } from '../src/domains/bossrush/bossRushSettle';
import { anomalyKinds, get, newHero, post, seedGold, seedLevel, type Hero } from './economyHelpers';
import { CHAPTER2_LEVEL_CAP } from '../src/gamedata/chapterGate';

const app = buildApp();
// 화요일 KST 12:00 (주는 목요일 06:00 KST에 바뀐다). 월드보스 2주 시즌은 2026-10-15 06:00 KST ~ 2026-10-29 06:00 KST(기준점 10-29에서 2주 앞)
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

  test('world season is two weeks from the fixed anchor (Thursday 06:00 KST every second week)', () => {
    const W = 7 * 86_400_000;
    const at = (weeks: number) => worldSeasonBounds(WORLD_SEASON_ANCHOR_MS + weeks * W);
    expect(new Date(WORLD_SEASON_ANCHOR_MS).toISOString()).toBe('2026-10-28T21:00:00.000Z');
    expect(at(0)).toEqual({ start: new Date(WORLD_SEASON_ANCHOR_MS), end: new Date(WORLD_SEASON_ANCHOR_MS + 2 * W) });
    expect(at(1)).toEqual(at(0)); // 둘째 주도 같은 시즌
    expect(at(2).start).toEqual(new Date(WORLD_SEASON_ANCHOR_MS + 2 * W));
    expect(at(-1).start).toEqual(new Date(WORLD_SEASON_ANCHOR_MS - 2 * W)); // 기준점 앞도 같은 격주
    expect(at(-2)).toEqual(at(-1));
    expect(at(-3).start).toEqual(new Date(WORLD_SEASON_ANCHOR_MS - 4 * W));
  });
});

describe('world boss attempts', () => {
  test('status creates the week and shows 3 attempts', async () => {
    const h = await hero();
    const r = await get(app, h, '/bosses');
    expect(r.status).toBe(200);
    expect(r.body.data.world).toMatchObject({ boss_id: 'boss_abyss_titan', max_hp: 80_000_000, hp_left: 80_000_000, attempts_left: 3, my_best: 0, my_rank: null,
      season_ends_at: '2026-10-28T21:00:00.000Z', season_weeks: 2 });
    expect(r.body.data.guild).toBeNull();
    expect(r.body.data.week_ends_at).toBe('2026-10-21T21:00:00.000Z'); // 주간 경계(길드보스·일정)는 그대로
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
    expect(f1.body.data).toMatchObject({ accepted_damage: 12_000, capped: false, best: 12_000, hp_left: 80_000_000 - 12_000, rank: 1, defeated: false });
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

/** 길드보스 순위 보상 우편(골드 한 통 + 아이템마다 한 통) */
function tierMails(rank: number) {
  const t = guildTierReward(rank);
  if (!t) return [];
  return [{ gold: t.gold, item: null, count: null }, ...t.items.map((i) => ({ gold: 0, item: i.itemKey, count: i.count }))];
}

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
    // 1주가 끝나도 월드보스는 정산하지 않는다(2주 시즌)
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    expect(await mails(a, 'world_boss')).toEqual([]);
    now = new Date('2026-10-28T21:05:00Z'); // 2주 뒤 목요일 06:05 KST
    const r1 = await settleDueSeasons();
    expect(r1.seasons).toBe(1);
    expect(await mails(a, 'world_boss')).toEqual([
      { gold: WORLD_JOIN_GOLD + WORLD_DEFEAT_GOLD, item: null, count: null },
      { gold: 0, item: 'mat_ore', count: WORLD_JOIN_ORE },
    ]);
    expect([WORLD_JOIN_GOLD, WORLD_DEFEAT_GOLD, WORLD_JOIN_ORE]).toEqual([60_000, 60_000, 10]); // 시즌(2주)당 = 예전 주당 x 2
    const titles = await getPool().query<{ character_id: string; title_id: string; name_color: string | null; expires_at: Date | null }>(
      'SELECT character_id, title_id, name_color, expires_at FROM rank_titles ORDER BY character_id');
    expect(titles.rows.every((t) => t.expires_at === null)).toBe(true); // 영구
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
    // 월드보스 칭호는 영구: 다음 시즌 정산·매일 만료 작업 뒤에도 그대로 달려 있다
    now = new Date('2027-03-01T00:00:00Z');
    await getPool().query("INSERT INTO boss_seasons (kind, week_start, week_end, boss_id, max_hp, hp_left) VALUES ('world', '2026-10-28T21:00:00Z', '2026-11-11T21:00:00Z', 'boss_abyss_titan', 10, 10)");
    await settleDueSeasons();
    expect(await unequipExpiredRankTitles()).toBe(0);
    const t = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(t.rows[0]?.title_achievement).toBe('rank_world_1');
    const list = await request(app).get(`/characters/${a.id}/achievements`).set(auth(a.s));
    expect(list.body.data.rank_titles).toEqual([{ id: 'rank_world_1', title: '세계를 꺾은 자', name_color: 'gold', expires_at: null, permanent: true }]);
    // 이름 색은 장착한 칭호를 따른다: 떼면 색도 없다
    const chat = await import('../src/domains/rankings/nameplate');
    expect((await chat.nameplateOf(getPool(), a.dbId, now)).name_color).toBe('gold');
    expect((await chat.nameplateOf(getPool(), b.dbId, now)).name_color).toBeNull(); // b는 칭호를 가졌지만 장착 안 함
    await request(app).post(`/characters/${a.id}/title`).set(auth(a.s)).send({ achievement_id: null });
    expect((await chat.nameplateOf(getPool(), a.dbId, now))).toMatchObject({ name_color: null, title_id: null });
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
    // 참가 골드(도전한 사람) + 1위 길드원 보상(그 길드로 도전한 길드원만)
    expect(await mails(leader, 'guild_boss')).toEqual([{ gold: GUILD_JOIN_GOLD, item: null, count: null }, ...tierMails(1)]);
    expect(await mails(m, 'guild_boss')).toEqual([]); // 도전하지 않은 길드원은 순위 보상도 없다
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

  test('an expired guild rank title is unequipped by the daily job and hidden by the achievements view', async () => {
    const { leader: a } = await inGuild();
    advance(24 * 3600);
    await fight(a, 20_000, 'guild');
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    expect((await request(app).post(`/characters/${a.id}/title`).set(auth(a.s)).send({ achievement_id: 'rank_guild_1' })).status).toBe(200);
    // 만료(week_end + 7일 + 1시간) 하루 뒤. 길드보스 칭호는 주간 그대로
    now = new Date('2026-10-29T23:00:00Z');
    const before = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(before.rows[0]?.title_achievement).toBe('rank_guild_1');
    expect(await unequipExpiredRankTitles()).toBe(1);
    const after = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(after.rows[0]?.title_achievement).toBeNull();
    // 작업 전이라도 업적 조회는 만료 칭호를 비워서 보여 주고 뗀다
    await getPool().query("UPDATE characters SET title_achievement = 'rank_guild_1' WHERE id = $1", [a.dbId]);
    const list = await request(app).get(`/characters/${a.id}/achievements`).set(auth(a.s));
    expect(list.status).toBe(200);
    expect(list.body.data.title).toBeNull();
    expect(list.body.data.rank_titles).toEqual([]);
    const again = await getPool().query('SELECT title_achievement FROM characters WHERE id = $1', [a.dbId]);
    expect(again.rows[0]?.title_achievement).toBeNull();
  });
});

// 챕터 3 5단계(Docs/ch3/LIVE_CONTENT.md 2절): 입장 Lv40 고정, 보스 레벨 = 시즌 생성 때의 levelCap, Lv50 시즌 월드보스 체력 x2.0
describe('chapter 3 boss level (0053 boss_level)', () => {
  afterEach(() => { buildApp(); });

  test('closed: the week is a Lv40 boss with the base HP', async () => {
    const h = await hero();
    const r = await get(app, h, '/bosses');
    expect(r.body.data.world).toMatchObject({ boss_level: 40, max_hp: 80_000_000 });
    expect((await start(h)).body.data.level).toBe(40);
  });

  test('open: a new week is a Lv50 boss with x2.0 HP, and Lv40 heroes still take part', async () => {
    buildApp({ CHAPTER3_OPEN: 'true' });
    const lv40 = await hero(40);
    const r = await get(app, lv40, '/bosses');
    expect(r.body.data.world).toMatchObject({ boss_level: 50, max_hp: 160_000_000, hp_left: 160_000_000 });
    const s = await start(lv40);
    expect(s.status).toBe(201);
    expect(s.body.data.level).toBe(50);
    expect((await start(await hero(39))).body.errors.code).toBe('LEVEL_TOO_LOW');
    const lv50 = await hero(50);
    expect((await start(lv50)).status).toBe(201);
    const row = await getPool().query('SELECT boss_level FROM boss_seasons WHERE kind = $1', ['world']);
    expect(row.rows[0]?.boss_level).toBe(50);
  });

  test('the season keeps the level it was created with until the week ends', async () => {
    const h = await hero();
    expect((await get(app, h, '/bosses')).body.data.world.boss_level).toBe(40);
    buildApp({ CHAPTER3_OPEN: 'true' });
    const r = await get(app, h, '/bosses');
    expect(r.body.data.world).toMatchObject({ boss_level: 40, max_hp: 80_000_000 });
    expect((await start(h)).body.data.level).toBe(40);
    advance(3 * 86_400); // 목요일 06:00 KST를 넘겨도 2주 시즌의 둘째 주라 그대로
    expect((await get(app, h, '/bosses')).body.data.world).toMatchObject({ boss_level: 40, max_hp: 80_000_000 });
    advance(7 * 86_400); // 2주 시즌 경계를 넘긴다
    expect((await get(app, h, '/bosses')).body.data.world).toMatchObject({ boss_level: 50, max_hp: 160_000_000 });
  });
});

// 2026-10-30: 월드보스 시즌 2주(기준점 2026-10-29 06:00 KST부터 격주), 하루 3회는 그대로
describe('world boss two-week season', () => {
  test('the season changes only at the biweekly anchor, daily attempts still reset every day', async () => {
    const h = await hero();
    now = new Date('2026-10-28T20:50:00Z'); // 2주 시즌 마지막 10분
    const a = (await get(app, h, '/bosses')).body.data.world;
    expect(a.season_ends_at).toBe('2026-10-28T21:00:00.000Z');
    await fight(h, 3000);
    now = new Date('2026-10-28T21:00:01Z'); // 기준점(목요일 06:00 KST): 새 시즌
    const b = (await get(app, h, '/bosses')).body.data.world;
    expect(b.season_id).not.toBe(a.season_id);
    expect(b).toMatchObject({ season_ends_at: '2026-11-11T21:00:00.000Z', max_hp: 80_000_000, hp_left: 80_000_000, my_best: 0, attempts_left: 3 });
    now = new Date('2026-11-04T21:00:01Z'); // 한 주 뒤 목요일 06:00 KST: 길드보스 주는 바뀌어도 월드보스 시즌은 그대로
    const c = (await get(app, h, '/bosses')).body.data;
    expect(c.world.season_id).toBe(b.season_id);
    expect(c.week_ends_at).toBe('2026-11-11T21:00:00.000Z');
  });

  test('a one-week world season made before the switch runs until its own end', async () => {
    now = new Date('2026-10-22T03:00:00Z');
    await getPool().query("INSERT INTO boss_seasons (kind, week_start, week_end, boss_id, max_hp, hp_left) VALUES ('world', '2026-10-21T21:00:00Z', '2026-10-28T21:00:00Z', 'boss_abyss_titan', 40000000, 40000000)");
    const h = await hero();
    const w = (await get(app, h, '/bosses')).body.data.world;
    expect(w).toMatchObject({ max_hp: 40_000_000, season_ends_at: '2026-10-28T21:00:00.000Z', season_weeks: 1 });
    const n = await getPool().query("SELECT count(*)::int AS n FROM boss_seasons WHERE kind = 'world'");
    expect(n.rows[0]?.n).toBe(1);
  });
});

// 2026-10-30: 길드보스 1~10위 길드원 순위 보상(정산 시각 길드원 전원, 한 사람 한 번)
describe('guild boss top 10 member rewards', () => {
  async function mailsOf(h: Hero) {
    const r = await getPool().query<{ gold: string; item_key: string | null; count: number | null }>(
      "SELECT gold, item_key, count FROM mails WHERE character_id = $1 AND system_code = 'guild_boss' ORDER BY id", [h.dbId]);
    return r.rows.map((x) => ({ gold: Number(x.gold), item: x.item_key, count: x.count }));
  }
  const join = { gold: GUILD_JOIN_GOLD, item: null, count: null };

  test('top 10 guilds only, only members who attempted, once even if settled again', async () => {
    const guilds: { leader: Hero; guildId: string }[] = [];
    for (let i = 0; i < 11; i++) guilds.push(await inGuild());
    const member = await hero(); // 1위 길드의 도전 안 한 길드원(보상 없음)
    await post(app, member, '/guild/apply', { guild_id: guilds[0]!.guildId });
    advance(24 * 3600);
    for (let i = 0; i < 11; i++) await fight(guilds[i]!.leader, 20_000 - i * 1000, 'guild'); // 0번이 1위 ... 10번이 11위
    now = new Date('2026-10-21T21:02:00Z'); // 주가 끝난 뒤 정산 전에 들어온 사람은 받지 않는다
    const late = await hero();
    await post(app, late, '/guild/apply', { guild_id: guilds[0]!.guildId });
    await settleDueSeasons();
    for (let i = 0; i < 11; i++) expect(await mailsOf(guilds[i]!.leader)).toEqual([join, ...tierMails(i + 1)]);
    expect(tierMails(11)).toEqual([]);
    expect(await mailsOf(member)).toEqual([]);
    expect(await mailsOf(late)).toEqual([]);
    expect(guildTierReward(2)).toEqual(guildTierReward(3));
    expect(guildTierReward(4)).toEqual(guildTierReward(10));
    // 다시 정산해도 더 오지 않는다
    expect((await settleDueSeasons()).seasons).toBe(0);
    expect(await mailsOf(guilds[0]!.leader)).toHaveLength(1 + tierMails(1).length);
  });

  test('someone who switched guilds during the week gets no tier reward (attempted for a guild they left)', async () => {
    const a = await inGuild();
    const b = await inGuild();
    const mover = await hero();
    await post(app, mover, '/guild/apply', { guild_id: a.guildId });
    advance(24 * 3600);
    await fight(mover, 30_000, 'guild'); // A가 1위
    await fight(b.leader, 10_000, 'guild'); // B가 2위
    expect((await post(app, mover, '/guild/leave', {})).status).toBe(200);
    await getPool().query('DELETE FROM guild_cooldowns WHERE character_id = $1', [mover.dbId]); // 24시간 대기를 건너뛴다
    expect((await post(app, mover, '/guild/apply', { guild_id: b.guildId })).body.data.joined).toBe(true);
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    // 참가 골드 한 번. 지금 길드(B)로는 도전하지 않았고 떠난 길드(A)에는 없으니 순위 보상은 없다(1위 칭호는 기여로 받는다)
    expect(await mailsOf(mover)).toEqual([join]);
    const t = await getPool().query("SELECT count(*)::int AS n FROM rank_titles WHERE character_id = $1 AND title_id = 'rank_guild_1'", [mover.dbId]);
    expect(t.rows[0]?.n).toBe(1);
  });

  test('participant member gets the tier reward, non-participant none, alt characters only if each attempted', async () => {
    const g = await inGuild();
    const main = await hero();
    const alt1 = await hero();
    const sameAcc = await createChar(app, main.s, randomName(), 'warrior');
    expect(sameAcc.status).toBe(201);
    const altId = Number((await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [sameAcc.body.data.character.id])).rows[0]!.id);
    const alt2: Hero = { s: main.s, id: sameAcc.body.data.character.id as string, dbId: altId, cls: 'warrior' };
    await seedLevel(alt2, CHAPTER2_LEVEL_CAP);
    for (const h of [main, alt1, alt2]) expect((await post(app, h, '/guild/apply', { guild_id: g.guildId })).status).toBe(200);
    advance(24 * 3600);
    await fight(g.leader, 20_000, 'guild');
    await fight(main, 10_000, 'guild'); // main 만 도전, 같은 계정의 alt2 와 다른 계정의 alt1 은 도전 안 함
    now = new Date('2026-10-21T21:05:00Z');
    await settleDueSeasons();
    expect(await mailsOf(main)).toEqual([join, ...tierMails(1)]);
    expect(await mailsOf(alt1)).toEqual([]);
    expect(await mailsOf(alt2)).toEqual([]);
    expect(await mailsOf(g.leader)).toEqual([join, ...tierMails(1)]);
    expect((await settleDueSeasons()).seasons).toBe(0);
    expect(await mailsOf(main)).toHaveLength(1 + tierMails(1).length);
  });

  test('zero-damage attempts are not participation (guild top 10 reward and join gold; world join reward)', async () => {
    const g = await inGuild();
    const alt = await hero();
    await post(app, alt, '/guild/apply', { guild_id: g.guildId });
    advance(24 * 3600);
    await fight(g.leader, 20_000, 'guild');
    await fight(alt, 0, 'guild');
    await fight(alt, 0, 'world');
    now = new Date('2026-10-28T21:05:00Z');
    await settleDueSeasons();
    expect(await mailsOf(alt)).toEqual([]);
    const w = await getPool().query("SELECT count(*)::int AS n FROM mails WHERE character_id = $1", [alt.dbId]);
    expect(w.rows[0]?.n).toBe(0);
    expect(await mailsOf(g.leader)).toEqual([join, ...tierMails(1)]);
  });
});
