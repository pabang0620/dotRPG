// 환경 파일과 운영 서버 진입점을 사용하지 않는 UI 검수 전용 워커.
import '../test/setupEnv';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { migrateUp } from '../src/db/migrate';
import { buildApp, auth } from '../test/helpers';
import { newHero, seedLevel, seedGold, seedItem, seedWorn, seedClaims, post, get } from '../test/economyHelpers';
import { getDailyQuests } from '../src/gamedata/dailyQuests';
import { closePool, getPool } from '../src/db/pool';
import { attachRealtime } from '../src/domains/chat/wsServer';
import { attachRelay } from '../src/domains/relay/relayServer';
import { setClockOverride } from '../src/utils/clock';

async function main() {
  const output = process.env.UI_QA_OUTPUT;
  const dbUrl = new URL(process.env.TEST_DATABASE_URL ?? 'invalid:');
  if (!output || dbUrl.hostname !== '127.0.0.1' || !/^\/uiqa_[a-f0-9]{32}$/.test(dbUrl.pathname)) throw new Error('격리 DB 확인 실패');
  await migrateUp(dbUrl.toString());
  const clientVersion = process.env.UI_QA_CLIENT_VERSION;
  if (!clientVersion || !/^\d+\.\d+\.\d+$/.test(clientVersion)) throw new Error('QA 클라이언트 버전 확인 실패');
  const app = buildApp({ CHAPTER3_OPEN: 'true', AUTH_DEV_REGISTER_ENABLED: 'true', POLICY_CONSENT_ENFORCE: 'false', MIN_CLIENT_VERSION: clientVersion });
  const fixtures: { name: string; status: string }[] = [];
  async function attempt(name: string, action: () => Promise<unknown>) {
    try { await action(); fixtures.push({ name, status: 'seeded' }); }
    catch (e) { fixtures.push({ name, status: `FAILED: ${e instanceof Error ? e.message : 'unknown'}` }); }
  }
  function check(r: request.Response) { if (r.status >= 300) throw new Error(`HTTP ${r.status}: ${r.body?.error?.code ?? r.body?.errors?.code ?? 'request failed'}`); return r; }
  const hero = await newHero(app, 'mage');
  const friend = await newHero(app, 'warrior');
  await getPool().query('UPDATE characters SET name = $2 WHERE id = $1', [hero.dbId, '새벽별빛마법소녀']);
  await getPool().query('UPDATE characters SET name = $2 WHERE id = $1', [friend.dbId, '은빛달빛검의수호']);
  for (const h of [hero, friend]) {
    await seedLevel(h, 50); await seedGold(h, 1000000);
    const policies = check(await request(app).get('/me/consents').set(auth(h.s)));
    check(await request(app).post('/me/consents').set(auth(h.s)).send({ agree: policies.body.data.required.map((d: { id: string; version: string }) => ({ id: d.id, version: d.version })) }));
  }
  await seedWorn(hero, 0, 'eq_staff_10_u');
  await seedItem(hero, 'mat_ore', 40); await seedItem(hero, 'eq_robe_1_u', 1);
  fixtures.push({ name: 'accounts-level50-inventory', status: 'seeded' });
  await attempt('friends', async () => {
    const r = check(await request(app).post('/friends/requests').set(auth(hero.s)).send({ request_id: randomUUID(), character_id: hero.id, target: friend.id }));
    check(await request(app).post(`/friends/requests/${r.body.data.request.id}/respond`).set(auth(friend.s)).send({ request_id: randomUUID(), accept: true }));
  });
  await attempt('guild', async () => {
    const r = check(await post(app, hero, '/guild', { name: '별빛달빛새벽하늘모험길드', join_mode: 'open' }));
    check(await post(app, friend, '/guild/apply', { guild_id: r.body.data.guild.id }));
  });
  await attempt('mail', async () => {
    for (let i = 0; i < 25; i++)
      await getPool().query("INSERT INTO mails (character_id, kind, system_code, title, gold, expires_at) VALUES ($1, 'system', 'notice', $2, 1000, now() + interval '7 days')", [hero.dbId, '긴제목글자잘림확인용우편안내'.repeat(4).slice(0, 38) + String(i + 1).padStart(2, '0')]);
  });
  await attempt('auction', async () => {
    await seedItem(friend, 'eq_sword_10_u', 2);
    check(await post(app, friend, '/auction/listings', { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, hours: 12 }));
  });
  await attempt('auction-pagination-render-only', async () => {
    // UI 페이지 수를 확보하는 DB fixture다. 등록 한도/거래 원장 무결성 검증으로 해석하지 않는다.
    const columns = (await getPool().query<{ column_name: string }>("SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='auction_listings' AND column_name NOT IN ('id','uuid') ORDER BY ordinal_position")).rows.map(r => r.column_name);
    if (!columns.length || columns.some(c => !/^[a-z_]+$/.test(c))) throw new Error('경매 fixture 열 검증 실패');
    const list = columns.map(c => `"${c}"`).join(',');
    const existing = Number((await getPool().query("SELECT count(*) FROM auction_listings WHERE status='active'")).rows[0].count);
    if (existing < 1) throw new Error('경매 기본 fixture 없음');
    for (let i = existing; i < 25; i++) await getPool().query(`INSERT INTO auction_listings(${list}) SELECT ${list} FROM auction_listings ORDER BY id LIMIT 1`);
  });
  await attempt('dailies', async () => {
    await seedClaims(hero, [getDailyQuests().unlockQuest]);
    const board = check(await get(app, hero, '/dailies')).body.data;
    if (!board.unlocked || !board.slots.length || !board.weekly.slots.length) throw new Error('일일·주간 의뢰 fixture가 비어 있음');
    for (const [period, slots] of [['day', board.slots], ['week', board.weekly.slots]] as const) {
      const slot = slots[slots.length - 1];
      check(await post(app, hero, '/dailies/accept', { period, day: slot.day, slot: slot.slot }));
    }
  });
  await attempt('world-boss-ranking', async () => {
    const now = Date.now(); setClockOverride(() => new Date(now - 181000));
    try {
      const r = check(await post(app, hero, '/bosses/world/attempts', {}));
      setClockOverride(() => new Date(now));
      check(await post(app, hero, `/bosses/world/attempts/${r.body.data.attempt_id}/finish`, { damage_dealt: 5000, hits_landed: 10, duration_s: 180 }));
    } finally { setClockOverride(null); }
  });
  const server = app.listen(Number(process.env.UI_QA_HTTP_PORT ?? 0), '127.0.0.1');
  await new Promise<void>((resolve, reject) => { server.once('listening', resolve); server.once('error', reject); });
  const realtime = await attachRealtime(server), relay = await attachRelay(server);
  const address = server.address(); if (!address || typeof address === 'string') throw new Error('서버 포트 확인 실패');
  const url = `http://127.0.0.1:${address.port}`;
  fs.writeFileSync(path.join(output, 'qa-credentials.json'), JSON.stringify({ server: url, loginId: hero.s.loginId, password: hero.s.password, characterId: hero.id }), { mode: 0o600 });
  const paginationCounts = {
    mail: Number((await getPool().query('SELECT count(*) FROM mails WHERE character_id=$1', [hero.dbId])).rows[0].count),
    auction: Number((await getPool().query("SELECT count(*) FROM auction_listings WHERE status='active'")).rows[0].count),
  };
  fs.writeFileSync(path.join(output, 'ready.json'), JSON.stringify({ server: url, clientVersion, fixtures, paginationCounts,
    stressLengths: { characters: [8, 8], guild: [12], mail: [40] }, onlineGameplayVerified: false,
    fixtureScope: 'Rendering fixture only. Auction rows are cloned directly for pagination, not an economy-integrity test.' }, null, 2));
  const timer = setInterval(async () => {
    if (!fs.existsSync(path.join(output, 'STOP'))) return;
    clearInterval(timer); await relay.close(); await realtime.close();
    await new Promise<void>(resolve => { server.close(() => resolve()); server.closeAllConnections(); });
    await closePool();
  }, 500);
}
main().catch(async e => { console.error(e instanceof Error ? e.message : 'QA worker failed'); await closePool(); process.exitCode = 1; });
