// 회원 탈퇴 요청 W1~W3, 로그인 거절 E1·E2, 철회 (Docs/server/phase12_withdrawal.md 12절 T-W1 ~ T-W17)
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { startSessionListener, stopSessionListener } from '../src/domains/antiabuse/sessionListener';
import { relayHub } from '../src/domains/relay/relayHub';
import { requestWithdrawal as requestService } from '../src/domains/withdrawal/withdrawalService';
import { activeCampaign, deliverTo, twoOwners } from './campaignHelpers';
import { adminApp } from './opsHelpers';
import { setClockOverride } from '../src/utils/clock';
import { sweepStale } from '../src/ops/jobs/staleRuns';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import { auth, pinClockToMondayFlowing, resetDb, shutdown, ver } from './helpers';
import { expectLedgerConsistent, get as getH, newHero, post as postH, seedGold, type Hero } from './economyHelpers';
import { buyoutReq, listIron, mk } from './auctionHelpers';
import { formParty } from './partyHelpers';
import { approveAndSync, buy, mock, payApp, purchase, resetPay, seedStars } from './payHelpers';
import { paymentWatchJob } from '../src/ops/jobs/paymentJobs';
import { connect, startServer, until, type TestServer } from './wsHelpers';
import {
  PHRASE, accountRow, asSession, authU, cancelBody, cancelWithdrawalApi, charNames, devLoginApi, devUser, makeDue, openWithdrawal, requestWithdrawal,
  steamLoginApi, steamUser, ticketFor, withdrawBody, withdrawalInfo, withdrawalRows, type WUser,
} from './withdrawalHelpers';

const app = payApp({ CAMPAIGN_DELIVERY_ENABLED: 'true', CAMPAIGN_MAX_GOLD_TOTAL: '100000000' });
beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

const fromHero = async (h: Hero): Promise<WUser> => {
  const a = await getPool().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [h.s.accountId]);
  return {
    access: h.s.access,
    accountUuid: h.s.accountId,
    accountId: Number((a.rows[0] as { id: string }).id),
    steamId: null,
    loginId: h.s.loginId,
    password: h.s.password,
    charUuid: h.id,
    charId: h.dbId,
    name: '',
  };
};
const me = (u: Pick<WUser, 'access'>) => request(app).get('/me').set(authU(u));
const pending = (res: request.Response): { due_at: string; cancel_allowed: boolean } => res.body.errors;

describe('W2 요청: 재인증, 확인 문구, 확인 체크, 결제 (T-W1 ~ T-W4)', () => {
  it('T-W1 재인증 없음·틀림은 403 REAUTH_FAILED이고 상태가 바뀌지 않는다. 남의 Steam 티켓도 통과하지 못한다', async () => {
    const steam = await steamUser(app);
    const dev = await devUser(app);
    const noReauth = await request(app).post('/me/withdrawal').set(authU(steam)).send({ request_id: randomUUID(), confirm: PHRASE, ack_progress_loss: true });
    expect(noReauth.status).toBe(403);
    expect(noReauth.body.errors.code).toBe('REAUTH_FAILED');
    const other = await requestWithdrawal(app, steam, { reauth: { provider: 'steam', ticket: ticketFor('76561198000000042') } });
    expect(other.status).toBe(403);
    expect(other.body.errors.code).toBe('REAUTH_FAILED');
    const badTicket = await requestWithdrawal(app, steam, { reauth: { provider: 'steam', ticket: 'abcdef0123456789abcdef' } });
    expect(badTicket.status).toBe(403);
    expect(badTicket.body.errors.code).toBe('REAUTH_FAILED');
    const wrongPw = await requestWithdrawal(app, dev, { reauth: { provider: 'dev', password: 'not-the-password' } });
    expect(wrongPw.status).toBe(403);
    expect(wrongPw.body.errors.code).toBe('REAUTH_FAILED');
    for (const u of [steam, dev]) {
      expect((await accountRow(u.accountId)).deleted_at).toBeNull();
      expect(await withdrawalRows(u.accountId)).toHaveLength(0);
      expect((await me(u)).status).toBe(200);
    }
    // 정상 자격이면 통과(재인증 실패가 이후 요청을 막지 않는다)
    expect((await requestWithdrawal(app, steam)).status).toBe(201);
    // 같은 티켓을 다시 쓰면 재사용으로 거절(401이 아니라 403 REAUTH_FAILED)
    const dev2 = await devUser(app);
    const t = ticketFor('76561198000000043');
    expect((await requestWithdrawal(app, dev2, { reauth: { provider: 'steam', ticket: t } })).body.errors.code).toBe('REAUTH_FAILED');
  });

  it('T-W2 확인 문구 불일치는 422 CONFIRM_MISMATCH, 공백과 유니코드 정규화(NFD) 차이는 통과한다. 허용되지 않은 필드는 400', async () => {
    const u = await devUser(app);
    const bad = await requestWithdrawal(app, u, { confirm: '탈퇴함' });
    expect(bad.status).toBe(422);
    expect(bad.body.errors.code).toBe('CONFIRM_MISMATCH');
    expect((await requestWithdrawal(app, u, { ack_progress_loss: false })).status).toBe(400);
    for (const extra of [{ grace_days: 0 }, { due_at: '2020-01-01T00:00:00Z' }, { reason: 'x' }, { paid_stars: 0 }]) {
      expect((await requestWithdrawal(app, u, extra)).status).toBe(400);
    }
    expect((await request(app).post('/me/withdrawal').send(withdrawBody(u))).status).toBe(400); // 버전 헤더 없음
    expect((await accountRow(u.accountId)).deleted_at).toBeNull();
    const ok = await requestWithdrawal(app, u, { confirm: `  ${PHRASE.normalize('NFD')} ` });
    expect(ok.status).toBe(201);
  });

  it('T-W3 유료 별조각이 있는데 확인(ack_paid_loss)이 없으면 409 ACK_REQUIRED. 무료 잔액만이면 필요 없다', async () => {
    const paid = await steamUser(app);
    await purchase(app, { access: paid.access, accountUuid: paid.accountUuid, accountId: paid.accountId, steamId: paid.steamId as string });
    const no = await requestWithdrawal(app, paid);
    expect(no.status).toBe(409);
    expect(no.body.errors).toMatchObject({ code: 'ACK_REQUIRED', need: ['paid_loss'] });
    expect((await requestWithdrawal(app, paid, { ack_paid_loss: false })).body.errors.code).toBe('ACK_REQUIRED');
    expect((await accountRow(paid.accountId)).deleted_at).toBeNull();
    const yes = await requestWithdrawal(app, paid, { ack_paid_loss: true });
    expect(yes.status).toBe(201);
    const w = await openWithdrawal(paid.accountId);
    expect(w).toMatchObject({ ack_paid_loss: true, source: 'self', cancel_allowed: true });
    expect(w.loss_snapshot).toMatchObject({ paid_stars: 300, characters: 1 });
    // 지갑은 그대로(소멸시키지 않는다)
    const wallet = await getPool().query('SELECT balance, paid_balance FROM star_wallets WHERE account_id = $1', [paid.accountId]);
    expect(wallet.rows[0]).toMatchObject({ balance: '300', paid_balance: '300' });

    const free = await steamUser(app);
    await seedStars(free.accountId, 50);
    expect((await requestWithdrawal(app, free)).status).toBe(201);
  });

  it('T-W4 열린 결제 주문이 있으면 409 WITHDRAW_BLOCKED, 주문이 끝난 뒤에는 통과한다', async () => {
    const u = await steamUser(app);
    const payer = { access: u.access, accountUuid: u.accountUuid, accountId: u.accountId, steamId: u.steamId as string };
    const b = await buy(app, payer, 'stars_300');
    expect(b.status).toBe(201);
    const info = await withdrawalInfo(app, u);
    expect(info.body.data).toMatchObject({ can_request: false, blockers: ['payment_open'] });
    const blocked = await requestWithdrawal(app, u, { ack_paid_loss: true });
    expect(blocked.status).toBe(409);
    expect(blocked.body.errors).toMatchObject({ code: 'WITHDRAW_BLOCKED', blockers: ['payment_open'] });
    expect((await accountRow(u.accountId)).deleted_at).toBeNull();
    expect((await approveAndSync(app, payer, b.body.data.order)).body.data.order.state).toBe('granted');
    expect((await requestWithdrawal(app, u, { ack_paid_loss: true })).status).toBe(201);
  });
});

describe('W2 효과: 즉시 차단, 토큰 폐기, 소켓 종료 (T-W5, T-W6)', () => {
  let srv: TestServer;
  beforeAll(async () => {
    srv = await startServer(app);
    await startSessionListener();
  });
  afterAll(async () => {
    await stopSessionListener();
    await srv.stop();
  });

  it('T-W5 요청 직후 같은 액세스 토큰은 모두 401이고 /ws가 4012로 닫히며 중계 연결도 끊긴다', async () => {
    const h = await newHero(app);
    const u = await fromHero(h);
    const kick = jest.spyOn(relayHub(), 'kickAccount');
    const { c } = await connect(srv.port, h);
    expect((await me(u)).status).toBe(200);
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    expect(await c.waitClose()).toBe(4012);
    await until(() => kick.mock.calls.some((x) => x[0] === u.accountId && x[1] === 4012));
    kick.mockRestore();
    for (const path of ['/me', '/characters', '/me/withdrawal']) {
      const res = await request(app).get(path).set(authU(u));
      expect(res.status).toBe(401);
      expect(res.body.errors.code).toBe('TOKEN_INVALID');
    }
    expect((await getH(app, h, '')).status).toBe(401);
    expect((await postH(app, h, '/shop/buy', { item_id: 'potion_hp', count: 1 })).status).toBe(401);
  });

  it('T-W6 모든 refresh 토큰 family가 revoke_reason=withdrawal 로 폐기되고 /auth/refresh 가 거절한다', async () => {
    const u = await devUser(app);
    const second = await devLoginApi(app, u.loginId as string, u.password as string); // 첫 세션은 replaced 로 폐기된다
    const live = { ...u, access: second.body.data.access_token as string };
    const refreshToken = second.body.data.refresh_token as string;
    expect((await requestWithdrawal(app, live)).status).toBe(201);
    const rows = await getPool().query<{ revoke_reason: string; revoked_at: Date | null }>('SELECT revoke_reason, revoked_at FROM refresh_tokens WHERE account_id = $1', [u.accountId]);
    expect(rows.rows.length).toBeGreaterThan(0);
    expect(rows.rows.every((r) => r.revoked_at !== null)).toBe(true);
    expect(rows.rows.filter((r) => r.revoke_reason === 'withdrawal').length).toBeGreaterThan(0);
    const res = await request(app).post('/auth/refresh').set(ver()).send({ refresh_token: refreshToken });
    expect(res.status).toBe(401);
    expect(['REFRESH_REUSED', 'REFRESH_INVALID']).toContain(res.body.errors.code);
    const acc = await accountRow(u.accountId);
    expect(acc).toMatchObject({ active_family_id: null });
  });
});

describe('W2 정리: 파티, 초대, 던전, 친구, 이름 (T-W7, T-W8)', () => {
  beforeAll(() => {
    pinClockToMondayFlowing();
  });
  afterAll(() => setClockOverride(null));

  it('T-W7 방장 탈퇴는 인계, 혼자면 해산. 신청·초대가 취소되고 솔로 던전 판이 abandoned 로 닫힌다', async () => {
    const [a, b, c, d] = [await newHero(app), await newHero(app), await newHero(app), await newHero(app)];
    const partyId = await formParty(app, a, [b]);
    const ua = await fromHero(a);
    // 대기 중 초대(받은 것·보낸 것)를 직접 넣는다
    const party = await getPool().query<{ id: string }>('SELECT id FROM parties WHERE uuid = $1', [partyId]);
    const pid = Number((party.rows[0] as { id: string }).id);
    await getPool().query(
      `INSERT INTO party_invites (party_id, inviter_character_id, invitee_character_id, invitee_account_id, expires_at)
       VALUES ($1, $2, $3, (SELECT account_id FROM characters WHERE id = $3), now() + interval '1 hour')`,
      [pid, a.dbId, c.dbId],
    );
    expect((await requestWithdrawal(app, ua)).status).toBe(201);
    const p = (await getPool().query('SELECT leader_character_id, state FROM parties WHERE id = $1', [pid])).rows[0] as { leader_character_id: string; state: string };
    expect(Number(p.leader_character_id)).toBe(b.dbId);
    expect(p.state).not.toBe('closed');
    const mem = await getPool().query("SELECT left_reason FROM party_members WHERE party_id = $1 AND character_id = $2", [pid, a.dbId]);
    expect(mem.rows[0]).toMatchObject({ left_reason: 'left' });
    const inv = await getPool().query('SELECT state, silent FROM party_invites WHERE party_id = $1', [pid]);
    expect(inv.rows).toEqual([{ state: 'cancelled', silent: true }]);
    // 혼자 남은 b 가 탈퇴하면 파티가 해산된다
    expect((await requestWithdrawal(app, await fromHero(b))).status).toBe(201);
    expect((await getPool().query('SELECT state, close_reason FROM parties WHERE id = $1', [pid])).rows[0]).toMatchObject({ state: 'closed', close_reason: 'disbanded' });
    // 솔로 던전 판
    const enter = await postH(app, c, '/dungeon-runs', { dungeon_id: 'gold_vein', difficulty: 0 });
    expect(enter.status).toBe(201);
    const runId = enter.body.data.run.id as string;
    // 대기 중 파티 신청
    const hostD = await postH(app, d, '/parties', { dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, listed: true });
    expect(hostD.status).toBe(201);
    const ap = await postH(app, c, `/parties/${hostD.body.data.party.id}/apply`, {});
    expect(ap.status).toBe(201);
    expect((await requestWithdrawal(app, await fromHero(c))).status).toBe(201);
    expect((await getPool().query('SELECT state FROM dungeon_runs WHERE uuid = $1', [runId])).rows[0]).toMatchObject({ state: 'abandoned' });
    expect((await getPool().query('SELECT state FROM party_applications WHERE character_id = $1', [c.dbId])).rows[0]).toMatchObject({ state: 'cancelled' });
    // 정리 작업이 방치 판으로 다시 건드리지 않는다
    await sweepStale();
    expect((await getPool().query('SELECT state FROM dungeon_runs WHERE uuid = $1', [runId])).rows[0]).toMatchObject({ state: 'abandoned' });
  });
});

describe('W2 정리: 친구와 이름 스냅샷 (T-W8)', () => {
  it('T-W8 친구가 removed 가 되고, 다른 사람이 보는 경매 판매자 이름은 자리표시다', async () => {
    const [a, f, buyer] = [await mk(app, 100_000), await mk(app, 100_000), await mk(app, 100_000)];
    const fr = await request(app).post('/friends/requests').set(auth(a.s)).send({ request_id: randomUUID(), character_id: a.id, target: f.id });
    expect(fr.status).toBe(201);
    const acc = await request(app).post(`/friends/requests/${fr.body.data.request.id}/respond`).set(auth(f.s)).send({ request_id: randomUUID(), accept: true });
    expect(acc.status).toBe(200);
    const nameBefore = (await getPool().query<{ name: string }>('SELECT name FROM characters WHERE id = $1', [a.dbId])).rows[0]?.name as string;
    const listing = await listIron(app, a);
    const seen = async (): Promise<string[]> => {
      const res = await getH(app, buyer, '/auction/search');
      expect(res.status).toBe(200);
      return (res.body.data.listings as { id: string; seller_name: string }[]).filter((l) => l.id === listing).map((l) => l.seller_name);
    };
    expect(await seen()).toEqual([nameBefore]);
    expect((await request(app).get('/friends').set(auth(f.s))).body.data.friends).toHaveLength(1);

    expect((await requestWithdrawal(app, await fromHero(a))).status).toBe(201);

    const friendsAfter = await request(app).get('/friends').set(auth(f.s));
    expect(friendsAfter.body.data.friends).toHaveLength(0);
    const fs = await getPool().query("SELECT state, silent FROM friendships WHERE requester_account_id = (SELECT account_id FROM characters WHERE id = $1)", [a.dbId]);
    expect(fs.rows).toEqual([{ state: 'removed', silent: true }]);
    const names = await seen();
    expect(names).toHaveLength(1);
    expect(names[0]).toMatch(/^탈퇴[0-9a-f]{6}$/);
    expect(names[0]).not.toBe(nameBefore);
  });
});
describe('멱등, 한도, 동시성, 손실 요약 (T-W9 ~ T-W12)', () => {
  it('T-W9 같은 request_id 재전송은 첫 응답, 다른 본문은 422, 성공 뒤 HTTP 재전송은 401(문서화된 동작)', async () => {
    const u = await devUser(app);
    const rid = randomUUID();
    const first = await requestWithdrawal(app, u, { request_id: rid });
    expect(first.status).toBe(201);
    expect(first.body.data).toMatchObject({ logged_out: true, withdrawal: { cancel_allowed: true } });
    // 토큰이 무효가 된 뒤의 재전송
    const again = await requestWithdrawal(app, u, { request_id: rid });
    expect(again.status).toBe(401);
    expect(again.body.errors.code).toBe('TOKEN_INVALID');
    // 서비스 직접 호출(토큰 검사 이전의 경쟁 상황): 같은 본문은 첫 응답, 다른 본문은 IDEMPOTENCY_MISMATCH
    const body = withdrawBody(u, { request_id: rid }) as Parameters<typeof requestService>[2];
    const replay = await requestService(u.accountId, '127.0.0.1', body);
    expect(replay.replay).toBe(true);
    expect(replay.body).toEqual(first.body);
    await expect(requestService(u.accountId, '127.0.0.1', { ...body, ack_paid_loss: true })).rejects.toMatchObject({ status: 422, code: 'IDEMPOTENCY_MISMATCH' });
    // 다른 request_id 는 이미 열린 요청 -> 409
    await expect(requestService(u.accountId, '127.0.0.1', { ...body, request_id: randomUUID() })).rejects.toMatchObject({ status: 409, code: 'WITHDRAWAL_ALREADY_REQUESTED' });
    expect(await withdrawalRows(u.accountId)).toHaveLength(1);
  });

  it('T-W10 최근 30일 4번째 요청은 429 WITHDRAW_LIMIT', async () => {
    const u = await devUser(app);
    let cur = u;
    for (let i = 0; i < 3; i++) {
      expect((await requestWithdrawal(app, cur)).status).toBe(201);
      expect((await cancelWithdrawalApi(app, u)).status).toBe(200);
      const login = await devLoginApi(app, u.loginId as string, u.password as string);
      expect(login.status).toBe(200);
      cur = { ...u, access: login.body.data.access_token as string };
    }
    const fourth = await requestWithdrawal(app, cur);
    expect(fourth.status).toBe(429);
    expect(fourth.body.errors.code).toBe('WITHDRAW_LIMIT');
    expect((await accountRow(u.accountId)).deleted_at).toBeNull();
    expect(await withdrawalRows(u.accountId)).toHaveLength(3);
  });

  it('T-W11 같은 계정에 W2 두 개를 동시에 보내면 요청 행은 1개. 상점 구매와 동시에 보내도 원장·잔액이 일관된다', async () => {
    const u = await devUser(app);
    const results = await Promise.all([requestWithdrawal(app, u), requestWithdrawal(app, u)]);
    const codes = results.map((r) => r.status).sort();
    expect(codes[0]).toBe(201);
    expect([401, 409]).toContain(codes[1]);
    expect(await withdrawalRows(u.accountId)).toHaveLength(1);
    expect((await getPool().query('SELECT count(*) AS n FROM request_log WHERE account_id = $1 AND endpoint = $2', [u.accountId, 'POST /me/withdrawal'])).rows[0]).toMatchObject({ n: '1' });

    const h = await newHero(app);
    await seedGold(h, 10_000);
    const hu = await fromHero(h);
    const [buyRes, wd] = await Promise.all([postH(app, h, '/shop/buy', { item_id: 'potion_hp', count: 3 }), requestWithdrawal(app, hu)]);
    expect(wd.status).toBe(201);
    expect([200, 401]).toContain(buyRes.status);
    await expectLedgerConsistent(h);
    const gold = await getPool().query('SELECT gold FROM characters WHERE id = $1', [h.dbId]);
    const last = await getPool().query('SELECT balance_after FROM gold_ledger WHERE character_id = $1 ORDER BY id DESC LIMIT 1', [h.dbId]);
    expect(gold.rows[0]).toMatchObject({ gold: (last.rows[0] as { balance_after: string }).balance_after });
  });

  it('T-W12 GET /me/withdrawal 의 손실 요약이 실제 잔액·건수와 같고 보류 사유를 노출하지 않는다', async () => {
    const u = await steamUser(app);
    const payer = { access: u.access, accountUuid: u.accountUuid, accountId: u.accountId, steamId: u.steamId as string };
    await purchase(app, payer); // 유료 300
    await seedStars(u.accountId, 40); // 무료 40
    await getPool().query('UPDATE characters SET gold = 777 WHERE id = $1', [u.charId]);
    await getPool().query(
      "INSERT INTO mails (character_id, kind, gold, title, system_code, expires_at) VALUES ($1, 'system', 500, '보상', 'event', now() + interval '10 days')",
      [u.charId],
    );
    await getPool().query(
      "INSERT INTO mails (character_id, kind, gold, title, system_code, expires_at, claimed_at) VALUES ($1, 'system', 500, '받음', 'event', now() + interval '10 days', now())",
      [u.charId],
    );
    // 활성 제재·경제 정지·열린 신고는 응답에 드러나지 않는다
    await getPool().query("INSERT INTO economy_holds (account_id, kind, state, evidence) VALUES ($1, 'manual', 'active', '{}'::jsonb)", [u.accountId]);
    const res = await withdrawalInfo(app, u);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({
      enabled: true, confirm_phrase: PHRASE, grace_days: 30, reauth: 'steam', can_request: true, blockers: [],
      losses: { gold_total: 777, paid_stars: 300, free_stars: 40, star_debt: 0, unclaimed_mails: 1, active_listings: 0, top_bids: 0 },
    });
    expect(res.body.data.losses.characters).toEqual([{ id: u.charUuid, name: u.name, class: 'warrior', level: 1 }]);
    expect(res.body.data.notices).toEqual(['paid_stars_forfeited', 'refund_follows_steam_policy', 'auction_and_mail_lost_after_grace', 'friends_party_not_restored']);
    expect(new Date(res.body.data.due_at_if_now).getTime()).toBeGreaterThan(Date.now() + 29 * 86_400_000);
    expect(JSON.stringify(res.body)).not.toMatch(/defer|economy_hold|sanction|report/i);
    const dev = await devUser(app);
    expect((await withdrawalInfo(app, dev)).body.data).toMatchObject({ reauth: 'dev' });
    expect((await request(app).get('/me/withdrawal')).status).toBe(400);
  });
});

describe('로그인 거절과 철회 (T-W13 ~ T-W17)', () => {
  it('T-W13 유예 중 Steam 로그인은 403 ACCOUNT_WITHDRAWAL_PENDING(새 계정이 생기지 않는다). 개발용은 비밀번호가 맞을 때만 알린다', async () => {
    const s = await steamUser(app);
    expect((await requestWithdrawal(app, s)).status).toBe(201);
    const login = await steamLoginApi(app, s.steamId as string);
    expect(login.status).toBe(403);
    expect(login.body.errors.code).toBe('ACCOUNT_WITHDRAWAL_PENDING');
    expect(pending(login)).toMatchObject({ cancel_allowed: true });
    expect(new Date(pending(login).due_at).getTime()).toBeGreaterThan(Date.now() + 29 * 86_400_000);
    const n = await getPool().query("SELECT count(*) AS n FROM auth_identities WHERE provider = 'steam' AND subject = $1", [s.steamId]);
    expect(n.rows[0]).toMatchObject({ n: '1' });

    const d = await devUser(app);
    expect((await requestWithdrawal(app, d)).status).toBe(201);
    const wrong = await devLoginApi(app, d.loginId as string, 'wrong-password-1');
    expect(wrong.status).toBe(401);
    expect(wrong.body.errors.code).toBe('INVALID_CREDENTIALS');
    const right = await devLoginApi(app, d.loginId as string, d.password as string);
    expect(right.status).toBe(403);
    expect(right.body.errors.code).toBe('ACCOUNT_WITHDRAWAL_PENDING');
    // 계정이 아예 없는 아이디와 틀린 비밀번호의 응답은 같다(존재 비노출)
    const ghost = await devLoginApi(app, 'nobody_here_42', 'wrong-password-1');
    expect(ghost.body.errors.code).toBe(wrong.body.errors.code);
    // 갱신 토큰은 이미 폐기됐다
    expect((await accountRow(d.accountId)).deleted_at).not.toBeNull();
  });

  it('T-W14 W3 정상: 로그인 복구, 이름 복원, 재화·친구 관계는 그대로(친구는 복구 안 됨)', async () => {
    const f = await mk(app, 50_000);
    const h = await mk(app, 50_000);
    const fr = await request(app).post('/friends/requests').set(auth(h.s)).send({ request_id: randomUUID(), character_id: h.id, target: f.id });
    await request(app).post(`/friends/requests/${fr.body.data.request.id}/respond`).set(auth(f.s)).send({ request_id: randomUUID(), accept: true });
    const u = await fromHero(h);
    const before = await charNames(u.accountId);
    const goldBefore = (await getPool().query('SELECT gold FROM characters WHERE id = $1', [h.dbId])).rows[0] as { gold: string };
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    expect((await charNames(u.accountId))[0]).toMatch(/^탈퇴[0-9a-f]{6}$/);
    const cancel = await cancelWithdrawalApi(app, u);
    expect(cancel.status).toBe(200);
    expect(cancel.body.data).toEqual({ cancelled: true, renamed_characters: 0 });
    expect(JSON.stringify(cancel.body)).not.toMatch(/token/);
    expect((await accountRow(u.accountId)).deleted_at).toBeNull();
    expect(await charNames(u.accountId)).toEqual(before);
    expect((await getPool().query('SELECT gold FROM characters WHERE id = $1', [h.dbId])).rows[0]).toEqual(goldBefore);
    const w = (await withdrawalRows(u.accountId))[0] as Record<string, unknown>;
    expect(w).toMatchObject({ state: 'cancelled', cancelled_via: 'self', saved_character_names: null });
    const login = await devLoginApi(app, u.loginId as string, u.password as string);
    expect(login.status).toBe(200);
    const live = { access: login.body.data.access_token as string };
    expect((await me(live)).status).toBe(200);
    expect((await me(live)).body.data.withdrawal).toBeNull(); // E4
    expect((await request(app).get('/friends').set(authU(live))).body.data.friends).toHaveLength(0);
    // 같은 request_id 로 다시 부르면 같은 200(그 사이 재로그인했어도)
    const rid = (w as { cancel_request_id: string }).cancel_request_id;
    const replay = await cancelWithdrawalApi(app, u, { request_id: rid });
    expect(replay.status).toBe(200);
    expect(replay.body.data).toEqual({ cancelled: true, renamed_characters: 0 });
    // Steam 철회: 티켓은 1회용이라 철회 뒤 로그인은 새 티켓으로
    const s = await steamUser(app);
    expect((await requestWithdrawal(app, s)).status).toBe(201);
    expect((await cancelWithdrawalApi(app, s)).status).toBe(200);
    expect((await steamLoginApi(app, s.steamId as string)).status).toBe(200);
  });

  it('T-W15 W3 거절: 기한 경과(410), 운영자 강제(403), 요청 없음·신원 없음(404 동일), 남의 자격으로는 철회할 수 없다', async () => {
    const a = await devUser(app);
    const b = await devUser(app);
    expect((await requestWithdrawal(app, a)).status).toBe(201);
    expect((await requestWithdrawal(app, b)).status).toBe(201);
    // 남의 자격(b)으로는 a 의 요청에 닿지 않는다: b 의 요청만 철회된다
    expect((await cancelWithdrawalApi(app, b)).status).toBe(200);
    expect((await accountRow(a.accountId)).deleted_at).not.toBeNull();
    expect((await openWithdrawal(a.accountId)).state).toBe('requested');
    // 틀린 비밀번호 / 존재하지 않는 아이디
    const wrongPw = await cancelWithdrawalApi(app, a, { reauth: { provider: 'dev', login_id: a.loginId, password: 'wrong-password-1' } });
    expect(wrongPw.status).toBe(403);
    expect(wrongPw.body.errors.code).toBe('REAUTH_FAILED');
    // 열린 요청이 없음(철회 뒤 다른 request_id) / 신원 없음 -> 같은 응답
    const none = await cancelWithdrawalApi(app, b);
    expect(none.status).toBe(404);
    expect(none.body.errors.code).toBe('NO_PENDING_WITHDRAWAL');
    const ghostSteam = await cancelWithdrawalApi(app, { ...a, steamId: '76561198123456789', loginId: null } as WUser);
    expect(ghostSteam.status).toBe(404);
    expect(ghostSteam.body.errors.code).toBe('NO_PENDING_WITHDRAWAL');
    expect(ghostSteam.body.message).toBe(none.body.message);
    // 기한 경과
    await makeDue(a.accountId);
    const due = await cancelWithdrawalApi(app, a);
    expect(due.status).toBe(410);
    expect(due.body.errors.code).toBe('WITHDRAWAL_DUE');
    // 운영자 강제 처리(cancel_allowed=false)
    const c = await devUser(app);
    expect((await requestWithdrawal(app, c)).status).toBe(201);
    await getPool().query('UPDATE account_withdrawals SET cancel_allowed = false WHERE account_id = $1', [c.accountId]);
    const forced = await cancelWithdrawalApi(app, c);
    expect(forced.status).toBe(403);
    expect(forced.body.errors.code).toBe('CANCEL_NOT_ALLOWED');
    expect((await openWithdrawal(c.accountId)).cancel_allowed).toBe(false);
    // 입력 오류
    expect((await request(app).post('/auth/withdrawal/cancel').set(ver()).send({ request_id: randomUUID() })).status).toBe(400);
    expect((await cancelWithdrawalApi(app, c, { account_id: 'x' })).status).toBe(400);
  });

  it('T-W16 유예 중에도 결제 대사·환불 회수·경매 정산·우편 기한 폐기가 계속 동작하고, 새 캠페인 우편은 배달되지 않는다', async () => {
    const u = await steamUser(app);
    const payer = { access: u.access, accountUuid: u.accountUuid, accountId: u.accountId, steamId: u.steamId as string };
    const bought = await buy(app, payer, 'stars_300');
    expect(bought.status).toBe(201);
    // 열린 주문이 있으면 W2 가 막히므로, 다른 계정으로 환불 회수를 확인한다
    const r = await steamUser(app);
    const rp = { access: r.access, accountUuid: r.accountUuid, accountId: r.accountId, steamId: r.steamId as string };
    const o = await purchase(app, rp);
    expect((await requestWithdrawal(app, r, { ack_paid_loss: true })).status).toBe(201);
    mock.setStatus(o.steam_order_id, 'Refunded');
    await getPool().query("UPDATE star_orders SET next_check_at = now() - interval '1 minute' WHERE uuid = $1", [o.id]);
    await paymentWatchJob({ opts: {}, shouldStop: () => false });
    const state = await getPool().query('SELECT state FROM star_orders WHERE uuid = $1', [o.id]);
    expect(state.rows[0]).toMatchObject({ state: 'refunded' });
    const w = await getPool().query('SELECT balance, paid_balance FROM star_wallets WHERE account_id = $1', [r.accountId]);
    expect(w.rows[0]).toMatchObject({ balance: '0', paid_balance: '0' });
    // 경매: 판매자가 탈퇴해도 마감 정산 틱이 대금을 우편으로 쌓는다
    const seller = await mk(app, 100_000);
    const buyer = await mk(app, 100_000);
    const listing = await listIron(app, seller);
    expect((await requestWithdrawal(app, await fromHero(seller))).status).toBe(201);
    expect((await buyoutReq(app, buyer, listing)).status).toBe(200);
    const mails = await getPool().query("SELECT kind, gold FROM mails WHERE character_id = $1 AND kind = 'sold'", [seller.dbId]);
    expect(mails.rows).toHaveLength(1);
    // 새 캠페인 우편은 탈퇴 요청 계정에 배달되지 않는다(활동 계정에는 배달된다)
    const [o1, o2] = await twoOwners();
    await activeCampaign(adminApp(), o1, o2);
    const control = await mk(app);
    expect(await deliverTo(control)).toBe(1);
    expect(await deliverTo(seller)).toBe(0);
    // 우편 기한 폐기와 경매 정산 틱은 로그인 없이 돈다
    await getPool().query("INSERT INTO mails (character_id, kind, gold, title, system_code, expires_at, created_at) VALUES ($1, 'system', 10, '만료', 'event', now() - interval '1 minute', now() - interval '2 days')", [seller.dbId]);
    const tick = await runAuctionTick();
    expect(tick.mailsExpired).toBeGreaterThanOrEqual(1);
    expect((await getPool().query("SELECT expired_at FROM mails WHERE character_id = $1 AND title = '만료'", [seller.dbId])).rows[0]?.expired_at).not.toBeNull();
  });

  it('T-W17 철회 때 이름이 이미 다른 사람에게 넘어갔으면 접미사로 복원하고 renamed_characters 가 오른다', async () => {
    const h = await newHero(app);
    const u = await fromHero(h);
    const original = (await getPool().query<{ name: string }>('SELECT name FROM characters WHERE id = $1', [h.dbId])).rows[0]?.name as string;
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    // 유예 중 다른 사람이 같은 이름으로 새 캐릭터를 만들었다
    const other = await devUser(app);
    await getPool().query('UPDATE characters SET name = $2 WHERE id = $1', [other.charId, original]);
    const cancel = await cancelWithdrawalApi(app, u);
    expect(cancel.status).toBe(200);
    expect(cancel.body.data).toEqual({ cancelled: true, renamed_characters: 1 });
    const restored = (await charNames(u.accountId))[0] as string;
    expect(restored).not.toBe(original);
    expect(restored).toMatch(/^[가-힣A-Za-z0-9]{2,8}$/);
    expect(restored.startsWith(Array.from(original).slice(0, 6).join(''))).toBe(true);
    expect((await accountRow(u.accountId)).deleted_at).toBeNull();
  });
});
