// 9단계 12.9: 관리자 경제 정지 API(H1~H9)와 회수 규칙
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { HOUR_MS, hourStart } from '../src/domains/antiabuse/incomeMeter';
import { auth, buildApp, createChar, randomLoginId, randomName, resetDb, shutdown, ver } from './helpers';
import { expectLedgerConsistent, get, newHero, post, seedGold, seedItem, type Hero } from './economyHelpers';
import { buyoutReq, listIron } from './auctionHelpers';
import { adminApp, adminGet, adminPost, auditRows, makeAdmin, type TestAdmin } from './opsHelpers';

let pub: Express = buildApp();
let adm: Express;
let viewer: TestAdmin;
let operator: TestAdmin;
let owner: TestAdmin;

beforeEach(async () => {
  await resetDb();
  pub = buildApp();
  adm = adminApp();
  viewer = await makeAdmin('viewer');
  operator = await makeAdmin('operator');
  owner = await makeAdmin('owner');
});
afterAll(shutdown);

const accountOf = async (h: Hero): Promise<number> =>
  Number(((await getPool().query('SELECT account_id FROM characters WHERE id = $1', [h.dbId])).rows[0] as { account_id: string }).account_id);
const accountUuid = async (h: Hero): Promise<string> =>
  ((await getPool().query('SELECT uuid FROM accounts WHERE id = $1', [await accountOf(h)])).rows[0] as { uuid: string }).uuid;

async function insertHold(h: Hero, state = 'active', over: { scoped?: boolean; kind?: string; evidence?: object } = {}): Promise<string> {
  const r = await getPool().query<{ uuid: string }>(
    `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence, reviewed_at)
     VALUES ($1, $2, $3, $4, '24h', now() - interval '24 hours', now() + interval '1 hour', $5::jsonb, CASE WHEN $4 IN ('shadow', 'active') THEN NULL ELSE now() END) RETURNING uuid`,
    [await accountOf(h), over.scoped === false ? null : h.dbId, over.kind ?? 'velocity', state, JSON.stringify(over.evidence ?? { metric: 'xp', value: 500, cap: 100 })],
  );
  return (r.rows[0] as { uuid: string }).uuid;
}
const holdState = async (uuid: string): Promise<Record<string, unknown>> => (await getPool().query('SELECT * FROM economy_holds WHERE uuid = $1', [uuid])).rows[0];

describe('H1 목록, H2 상세', () => {
  it('목록: 상태·종류·이름 필터, 커서 페이지, 수치 요약. 인증 없음은 401', async () => {
    const a = await newHero(pub);
    const b = await newHero(pub);
    const shadow = await insertHold(a, 'shadow');
    const active = await insertHold(b, 'active', { kind: 'manual' });
    expect((await request(adm).get('/admin/economy/holds')).status).toBe(401);
    const all = await adminGet(adm, viewer, '/admin/economy/holds');
    expect(all.status).toBe(200);
    expect(all.body.data.items.map((x: { id: string }) => x.id)).toEqual([active, shadow]);
    expect(all.body.data.items[1]).toMatchObject({ state: 'shadow', kind: 'velocity', window_kind: '24h', evidence_summary: { metric: 'xp', value: 500, cap: 100 }, linked_count: 0 });
    expect(all.body.data.items[1].character).toMatchObject({ level: 1 });
    expect(JSON.stringify(all.body)).not.toMatch(/"account_id":\d+/);
    expect((await adminGet(adm, viewer, '/admin/economy/holds?state=active')).body.data.items.map((x: { id: string }) => x.id)).toEqual([active]);
    expect((await adminGet(adm, viewer, '/admin/economy/holds?kind=velocity')).body.data.items.map((x: { id: string }) => x.id)).toEqual([shadow]);
    const acct = await accountUuid(a);
    expect((await adminGet(adm, viewer, `/admin/economy/holds?q=${acct}`)).body.data.items.map((x: { id: string }) => x.id)).toEqual([shadow]);
    const page = await adminGet(adm, viewer, '/admin/economy/holds?limit=1');
    expect(page.body.data.items).toHaveLength(1);
    expect(page.body.meta.next_cursor).toEqual(expect.any(String));
    const next = await adminGet(adm, viewer, `/admin/economy/holds?limit=1&cursor=${page.body.meta.next_cursor}`);
    expect(next.body.data.items.map((x: { id: string }) => x.id)).toEqual([shadow]);
    expect((await adminGet(adm, viewer, '/admin/economy/holds?limit=500')).status).toBe(400);
  });

  it('상세: 근거 전체, 창별 수치와 상한, 연결 정지, 원장 사유 요약. 없는 uuid는 404', async () => {
    const a = await newHero(pub);
    const hold = await insertHold(a, 'active');
    await getPool().query(
      `INSERT INTO income_hourly (character_id, hour_start, level_max, xp, gold_acq) VALUES ($1, $2, 18, 100, 50)`,
      [a.dbId, hourStart(new Date())],
    );
    const res = await adminGet(adm, viewer, `/admin/economy/holds/${hold}`);
    expect(res.status).toBe(200);
    expect(res.body.data.hold).toMatchObject({ id: hold, state: 'active', kind: 'velocity' });
    expect(res.body.data.evidence).toEqual({ metric: 'xp', value: 500, cap: 100 });
    expect(res.body.data.windows['1h']).toMatchObject({ xp: 100, active_seconds: 0, over: [] });
    expect(res.body.data.windows['24h'].caps).toHaveProperty('gold_eq');
    expect(res.body.data.linked).toEqual([]);
    expect(Object.keys(res.body.data.ledger_by_reason).sort()).toEqual(['gold', 'item', 'xp']);
    expect((await adminGet(adm, viewer, `/admin/economy/holds/${randomUUID()}`)).status).toBe(404);
    expect((await adminGet(adm, viewer, '/admin/economy/holds/not-a-uuid')).status).toBe(400);
    const audit = await auditRows("action = 'economy.hold.view'");
    expect(audit[0]).toMatchObject({ target_type: 'hold', target_uuid: hold, result: 'ok' });
  });
});

describe('H3 해제, H5 수동 정지', () => {
  it('operator가 해제하면 released_at이 기록되고, 같은 request_id는 재생, 다른 request_id는 released:0(멱등). viewer는 403', async () => {
    const a = await newHero(pub);
    const hold = await insertHold(a, 'active');
    const rid = randomUUID();
    expect((await adminPost(adm, viewer, `/admin/economy/holds/${hold}/release`, { note: '확인' })).status).toBe(403);
    const ok = await adminPost(adm, operator, `/admin/economy/holds/${hold}/release`, { note: '정상 플레이 확인' }, rid);
    expect(ok.status).toBe(200);
    expect(ok.body.data).toEqual({ released: 1 });
    expect(await holdState(hold)).toMatchObject({ state: 'released', note: '정상 플레이 확인', reviewed_by: operator.loginId });
    expect((await holdState(hold)).released_at).not.toBeNull();
    const replay = await adminPost(adm, operator, `/admin/economy/holds/${hold}/release`, { note: '정상 플레이 확인' }, rid);
    expect(replay.headers['idempotent-replay']).toBe('true');
    expect((await adminPost(adm, operator, `/admin/economy/holds/${hold}/release`, { note: '다시' })).body.data).toEqual({ released: 0 });
    expect((await adminPost(adm, operator, `/admin/economy/holds/${randomUUID()}/release`, { note: 'x' })).body.errors.code).toBe('HOLD_NOT_FOUND');
    expect((await adminPost(adm, operator, `/admin/economy/holds/${hold}/release`, {})).status).toBe(400);
    // 정지가 풀려 정지 대상 경로가 다시 열린다
    await seedItem(a, 'potion_hp', 1);
    expect((await post(pub, a, '/shop/sell', { item_key: 'potion_hp', count: 1 })).status).toBe(200);
  });

  it('수동 정지(H5): 캐릭터 또는 계정 중 정확히 하나, 이미 있으면 created:false, 정지 중에는 7개 경로가 막힌다', async () => {
    const a = await newHero(pub);
    const none = await adminPost(adm, operator, '/admin/economy/holds', { note: '조사 중' });
    expect(none.status).toBe(400);
    const both = await adminPost(adm, operator, '/admin/economy/holds', { note: 'x', character_id: a.id, account_id: await accountUuid(a) });
    expect(both.status).toBe(400);
    expect((await adminPost(adm, viewer, '/admin/economy/holds', { note: 'x', character_id: a.id })).status).toBe(403);
    const made = await adminPost(adm, operator, '/admin/economy/holds', { note: '조사 중', character_id: a.id });
    expect(made.status).toBe(201);
    expect(made.body.data).toMatchObject({ state: 'active', created: true });
    const again = await adminPost(adm, operator, '/admin/economy/holds', { note: '조사 중', character_id: a.id });
    expect(again.body.data).toMatchObject({ created: false, id: made.body.data.id });
    await seedItem(a, 'potion_hp', 1);
    const sell = await post(pub, a, '/shop/sell', { item_key: 'potion_hp', count: 1 });
    expect(sell.status).toBe(403);
    expect(sell.body.errors).toMatchObject({ code: 'ECONOMY_HOLD', scope: 'character' });
    const acct = await adminPost(adm, operator, '/admin/economy/holds', { note: '계정 전체', account_id: await accountUuid(a) });
    expect(acct.status).toBe(201);
    expect((await get(pub, a, '/economy-hold')).body.data.on_hold).toBe(true);
    expect((await adminPost(adm, operator, '/admin/economy/holds', { note: 'x', character_id: randomUUID() })).status).toBe(404);
  });
});

describe('H4 회수', () => {
  async function scene() {
    const h = await newHero(pub);
    await seedGold(h, 2900); // 잔액 3000, 구간 안 획득 2900(원장)
    await seedItem(h, 'mat_ore', 5);
    await getPool().query(
      "INSERT INTO mails (character_id, kind, gold, created_at, expires_at) VALUES ($1, 'sold', 500, now(), now() + interval '20 days')",
      [h.dbId],
    );
    return { h, hold: await insertHold(h, 'active') };
  }
  const claw = (hold: string, body: Record<string, unknown> = {}, who: TestAdmin = owner, rid: string = randomUUID()) =>
    adminPost(adm, who, `/admin/economy/holds/${hold}/clawback`, { note: '확정', gold: true, items: true, void_mail: true, ...body }, rid);

  it('골드는 구간 원장 획득분에서 폐기 우편 대금을 뺀 만큼만, 아이템은 획득분만, 미수령 우편은 만료시킨다. 원장 규칙이 지켜진다', async () => {
    const { h, hold } = await scene();
    const res = await claw(hold);
    expect(res.status).toBe(200);
    expect(res.body.data).toEqual({ gold_clawed: 2400, shortfall: 0, items: [{ item_key: 'mat_ore', count: 5 }], mails_voided: 1 });
    const g = await getPool().query("SELECT delta, balance_after, ref FROM gold_ledger WHERE reason = 'admin_clawback' AND character_id = $1", [h.dbId]);
    expect(g.rows).toEqual([{ delta: '-2400', balance_after: '600', ref: hold }]);
    const it = await getPool().query("SELECT delta, balance_after, location FROM item_ledger WHERE reason = 'admin_clawback' AND character_id = $1", [h.dbId]);
    expect(it.rows).toEqual([{ delta: -5, balance_after: 0, location: 'bag' }]);
    await expectLedgerConsistent(h);
    const mail = await getPool().query('SELECT expires_at <= now() AS gone FROM mails WHERE character_id = $1', [h.dbId]);
    expect(mail.rows).toEqual([{ gone: true }]);
    expect(await holdState(hold)).toMatchObject({ state: 'clawed_back', reviewed_by: owner.loginId, clawback: res.body.data });
    // 회수 뒤에도 해제 전까지 막는다
    const sell = await post(pub, h, '/shop/sell', { item_key: 'potion_hp', count: 1 });
    expect(sell.status).toBe(403);
    expect((await auditRows("action = 'economy.hold.clawback'"))[0]).toMatchObject({ result: 'ok', target_type: 'hold' });
  });

  it('재전송은 처음 요약(멱등), 두 번째 회수는 409, 금액을 본문으로 보내면 400, owner만 가능', async () => {
    const { hold } = await scene();
    const rid = randomUUID();
    const first = await claw(hold, {}, owner, rid);
    const replay = await claw(hold, {}, owner, rid);
    expect(replay.headers['idempotent-replay']).toBe('true');
    expect(replay.body.data).toEqual(first.body.data);
    const second = await claw(hold);
    expect(second.status).toBe(409);
    expect(second.body.errors.code).toBe('HOLD_ALREADY_CLAWED');
    expect((await claw(hold, { gold_amount: 99999 })).status).toBe(400);
    expect((await claw(hold, {}, operator)).status).toBe(403);
    expect((await claw(hold, { gold: 'yes' })).status).toBe(400);
  });

  it('회수할 것이 없으면 422 NOTHING_TO_CLAW(상태는 그대로), 해제된 정지는 409', async () => {
    const a = await newHero(pub);
    const hold = await insertHold(a, 'active');
    const none = await claw(hold);
    expect(none.status).toBe(422);
    expect(none.body.errors.code).toBe('NOTHING_TO_CLAW');
    expect((await holdState(hold)).state).toBe('active');
    const released = await insertHold(await newHero(pub), 'released');
    expect((await claw(released)).body.errors.code).toBe('HOLD_NOT_ACTIVE');
    expect((await claw(randomUUID())).status).toBe(404);
  });
});

describe('H6 연결 조회, H7 의심 거래, H8 상한 표, H9 속도', () => {
  const withDevice = async (deviceHash: string): Promise<Hero> => {
    const loginId = randomLoginId();
    const reg = await request(pub).post('/auth/dev/register').set(ver()).send({ login_id: loginId, password: 'password-1234', device: { install_id: randomUUID(), device_hash: deviceHash } });
    const s = { loginId, password: 'password-1234', accountId: reg.body.data.account.id, access: reg.body.data.access_token, refresh: reg.body.data.refresh_token };
    const made = await createChar(pub, s, randomName());
    const id = made.body.data.character.id as string;
    const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
    return { s, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' };
  };

  it('H6: 기기·IP 마스킹, 같은 기기의 다른 계정, 원본 IP는 owner만(감사 기록)', async () => {
    const dev = 'aa'.repeat(32);
    const a = await withDevice(dev);
    const b = await withDevice(dev);
    const uuid = await accountUuid(a);
    const res = await adminGet(adm, viewer, `/admin/accounts/${uuid}/links`);
    expect(res.status).toBe(200);
    expect(res.body.data.devices).toHaveLength(1);
    expect(res.body.data.devices[0]).toMatchObject({ accounts_on_device: 2 });
    expect(res.body.data.devices[0].device_label).toHaveLength(8);
    expect(res.body.data.ips[0]).toMatchObject({ ip_group: '127.0.0.0/24', accounts_on_ip: 2 });
    expect(res.body.data.ips[0].ip).toBeUndefined();
    expect(res.body.data.linked_accounts).toEqual([{ account_id: await accountUuid(b), via: ['device', 'ip'], last_seen_at: expect.any(String) }]);
    expect((await adminGet(adm, viewer, `/admin/accounts/${uuid}/links?full_ip=true`)).status).toBe(403);
    const full = await adminGet(adm, owner, `/admin/accounts/${uuid}/links?full_ip=true`);
    expect(full.body.data.ips[0].ip).toBe('127.0.0.1');
    expect((await auditRows("action = 'account.links_full_ip'")).length).toBe(1);
    expect((await adminGet(adm, viewer, `/admin/accounts/${randomUUID()}/links`)).status).toBe(404);
  });

  it('H7: 같은 기기 쌍의 체결이 SAME_DEVICE·SAME_IP로 표시되고(체결은 성공), 경매 집계가 가중 수입으로 쌓인다', async () => {
    const dev = 'bb'.repeat(32);
    const seller = await withDevice(dev);
    const buyer = await withDevice(dev);
    await seedGold(buyer, 5000);
    const id = await listIron(pub, seller, { buyout: 1000 });
    expect((await buyoutReq(pub, buyer, id)).status).toBe(200);
    const flags = await getPool().query("SELECT flag, weight_pct FROM auction_trade_flags ORDER BY flag");
    expect(flags.rows).toEqual([{ flag: 'SAME_DEVICE', weight_pct: 300 }, { flag: 'SAME_IP', weight_pct: 150 }]);
    const s = await getPool().query('SELECT auction_in, auction_in_w FROM income_hourly WHERE character_id = $1', [seller.dbId]);
    expect(s.rows).toEqual([{ auction_in: '960', auction_in_w: '2880' }]);
    expect((await getPool().query('SELECT auction_out FROM income_hourly WHERE character_id = $1', [buyer.dbId])).rows).toEqual([{ auction_out: '1000' }]);
    const list = await adminGet(adm, viewer, '/admin/auction/trade-flags');
    expect(list.status).toBe(200);
    expect(list.body.data.items).toHaveLength(1);
    expect(list.body.data.items[0]).toMatchObject({ item_key: 'eq_sword_10_u', price: 1000, flags: [{ flag: 'SAME_DEVICE' }, { flag: 'SAME_IP' }] });
    expect(list.body.data.items[0].seller.character.id).toBe(seller.id);
    expect((await adminGet(adm, viewer, '/admin/auction/trade-flags?flag=SAME_DEVICE')).body.data.items).toHaveLength(1);
    expect((await adminGet(adm, viewer, '/admin/auction/trade-flags?flag=NEW_BUYER')).body.data.items).toEqual([]);
    expect((await adminGet(adm, viewer, '/admin/auction/trade-flags?min_price=2000')).body.data.items).toEqual([]);
    expect((await adminGet(adm, viewer, '/admin/auction/trade-flags?flag=BOGUS')).status).toBe(400);
    expect((await request(adm).get('/admin/auction/trade-flags')).status).toBe(401);
  });

  it('H8 상한 표와 H9 캐릭터 속도', async () => {
    const caps = await adminGet(adm, viewer, '/admin/economy/income-caps');
    expect(caps.status).toBe(200);
    expect(caps.body.data).toMatchObject({ mode: 'log_only', mult: 3, floors: { xp: 10_000, gold_eq: 50_000, ore: 200, essence: 60, unique_plus: 3 } });
    expect(caps.body.data.caps.schema).toBe(1);
    expect(caps.body.data.caps.bands).toHaveLength(9);
    const h = await newHero(pub);
    const v = await adminGet(adm, viewer, `/admin/characters/${h.id}/velocity`);
    expect(v.status).toBe(200);
    expect(v.body.data.character).toMatchObject({ id: h.id, level: 1 });
    expect(Object.keys(v.body.data.windows)).toEqual(['1h', '24h', '7d']);
    expect(v.body.data.band).toEqual({ min_level: 1, max_level: 4 });
    expect((await adminGet(adm, viewer, `/admin/characters/${randomUUID()}/velocity`)).status).toBe(404);
    // 정지 목록에 최근 정지가 보인다
    await insertHold(h, 'shadow');
    expect((await adminGet(adm, viewer, `/admin/characters/${h.id}/velocity`)).body.data.recent_holds).toHaveLength(1);
    void auth;
    void HOUR_MS;
  });
});
