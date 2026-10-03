import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { integrityJob } from '../src/ops/jobs/integrity';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { expectConserved } from './auctionHelpers';
import { newHero, post, get, type Hero } from './economyHelpers';
import { auth, buildApp, resetDb, shutdown } from './helpers';
import { adminApp, adminGet, adminPost, auditRows, makeAdmin, rid, type TestAdmin } from './opsHelpers';
import { connect, startServer, until, type TestServer } from './wsHelpers';

const pub = buildApp();
const app = adminApp();
let owner: TestAdmin;
let operator: TestAdmin;
let viewer: TestAdmin;
let srv: TestServer;
const sockets: { close(): void }[] = [];

beforeAll(async () => {
  srv = await startServer(pub);
});
beforeEach(async () => {
  await resetDb();
  getRateLimitStore().clear();
  [owner, operator, viewer] = [await makeAdmin('owner'), await makeAdmin('operator'), await makeAdmin('viewer')];
});
afterEach(async () => {
  for (const s of sockets.splice(0)) s.close();
  await until(() => registry.size() === 0);
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

const ITEM = 'potion_hp';
const accountUuid = async (h: Hero): Promise<string> => h.s.accountId;
const dbOne = async <T = Record<string, unknown>>(sql: string, p: unknown[] = []): Promise<T> => (await getPool().query(sql, p)).rows[0] as T;

describe('계정 조회와 감사', () => {
  it('찾기(uuid, name:, dev:), 상세, 캐릭터, 원장: 민감 조회는 감사에 남는다', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    const byUuid = await adminGet(app, viewer, `/admin/accounts?q=${acct}`);
    expect(byUuid.body.data.accounts[0].id).toBe(acct);
    const name = (await dbOne<{ name: string }>('SELECT name FROM characters WHERE uuid = $1', [h.id])).name;
    const byName = await adminGet(app, viewer, `/admin/accounts?q=${encodeURIComponent(`name:${name.toUpperCase()}`)}`);
    expect(byName.body.data.accounts).toHaveLength(1);
    const byDev = await adminGet(app, viewer, `/admin/accounts?q=dev:${h.s.loginId}`);
    expect(byDev.body.data.accounts[0].identities[0]).toMatchObject({ provider: 'dev' });
    expect((await adminGet(app, viewer, '/admin/accounts?q=whatever')).status).toBe(422);

    const detail = await adminGet(app, viewer, `/admin/accounts/${acct}`);
    expect(detail.status).toBe(200);
    expect(detail.body.data).toMatchObject({ account: { id: acct, banned: false }, held_runs: 0, watch_score: 0 });
    expect(detail.body.data.account.characters[0].id).toBe(h.id);
    const ch = await adminGet(app, viewer, `/admin/characters/${h.id}`);
    expect(ch.body.data.character.id).toBe(h.id);
    expect(ch.body.data.items.length).toBeGreaterThan(0);

    const led = await adminGet(app, viewer, `/admin/characters/${h.id}/ledger?kind=item&limit=2`);
    expect(led.status).toBe(200);
    expect(led.body.data.lines).toHaveLength(2);
    expect(led.body.meta.next_before).toEqual(expect.any(String));
    const page2 = await adminGet(app, viewer, `/admin/characters/${h.id}/ledger?kind=item&limit=50&before=${led.body.meta.next_before}`);
    expect(page2.body.data.lines.length).toBeGreaterThan(0);
    expect((await adminGet(app, viewer, `/admin/characters/${h.id}/ledger?before=!!`)).status).toBe(400);
    for (const kind of ['gold', 'xp']) expect((await adminGet(app, viewer, `/admin/characters/${h.id}/ledger?kind=${kind}`)).status).toBe(200);

    const views = (await auditRows("result = 'ok'")).map((r) => r.action);
    expect(views).toEqual(expect.arrayContaining(['account.view', 'character.view', 'ledger.view']));
    expect((await adminGet(app, viewer, `/admin/accounts/${randomUUID()}`)).status).toBe(404);
    // 실패(404)도 감사에 남는다
    expect((await auditRows("action = 'account.view' AND result = 'invalid'")).length).toBe(1);
  });

  it('메모와 검토 확인, 접속 끊기(4011), 개발용 계정 발급·재설정', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    expect((await adminPost(app, viewer, `/admin/accounts/${acct}/notes`, { note: 'x' })).status).toBe(403);
    const note = await adminPost(app, operator, `/admin/accounts/${acct}/notes`, { note: '의심스러운 거래 확인' });
    expect(note.status).toBe(201);
    expect((await adminGet(app, viewer, `/admin/accounts/${acct}`)).body.data.notes[0].note).toBe('의심스러운 거래 확인');

    const { c } = await connect(srv.port, h);
    sockets.push(c);
    const kick = await adminPost(app, operator, `/admin/accounts/${acct}/kick`);
    expect(kick.body.data.kicked).toBe(true);
    const bye = await c.waitT('bye');
    expect(bye).toMatchObject({ code: 4011, reason: 'KICKED', reconnect: true });

    // 개발용 계정: 임시 비밀번호로 실제 로그인되고, 재설정하면 옛 비밀번호는 막힌다
    const created = await adminPost(app, owner, '/admin/accounts/dev', { login_id: 'cbt_tester1' });
    expect(created.status).toBe(201);
    const temp = created.body.data.temp_password as string;
    const ok = await request(pub).post('/auth/dev/login').set({ 'X-Client-Version': '0.2.0' }).send({ login_id: 'cbt_tester1', password: temp });
    expect(ok.status).toBe(200);
    expect((await adminPost(app, operator, '/admin/accounts/dev', { login_id: 'cbt_tester2' })).status).toBe(403);
    const reset = await adminPost(app, owner, `/admin/accounts/${created.body.data.account.id}/dev-password`);
    const next = reset.body.data.temp_password as string;
    expect(next).not.toBe(temp);
    expect((await request(pub).post('/auth/dev/login').set({ 'X-Client-Version': '0.2.0' }).send({ login_id: 'cbt_tester1', password: temp })).status).toBe(401);
    expect(JSON.stringify(await auditRows())).not.toContain(next);
  });
});

describe('제재(SA1, SA2)', () => {
  it('정지: banned_until을 정하고 게임 API가 막힌다. 영구 정지는 9999-12-31이라 인증이 500이 되지 않는다(F9)', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    const body = { kind: 'ban', reason_code: 'cheat', duration: '1d', note: '부정 행위' };
    const res = await adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, body);
    expect(res.status).toBe(201);
    expect(Date.parse(res.body.data.banned_until)).toBeGreaterThan(Date.now() + 23 * 3_600_000);
    const blocked = await request(pub).get('/characters').set(auth(h.s));
    expect(blocked.status).toBe(403);
    expect(blocked.body.errors.code).toBe('ACCOUNT_BANNED');
    // 같은 종류가 더 긴 기간으로 활성이면 409
    expect((await adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, { ...body, duration: '1h' })).body.errors.code).toBe('SANCTION_EXISTS');
    // operator의 영구 정지는 403, owner는 가능
    const perm = { kind: 'ban', reason_code: 'cheat', duration: 'permanent', note: '영구' };
    const denied = await adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, perm);
    expect(denied.status).toBe(403);
    expect((await auditRows("action = 'sanction.create' AND result = 'denied'")).length).toBe(1);
    const granted = await adminPost(app, owner, `/admin/accounts/${acct}/sanctions`, perm);
    expect(granted.status).toBe(201);
    expect(granted.body.data.banned_until).toBe('9999-12-31T00:00:00.000Z');
    const still = await request(pub).get('/characters').set(auth(h.s));
    expect(still.status).toBe(403);
    expect(still.body.errors.code).toBe('ACCOUNT_BANNED');
    // 해제하면 남은 활성 정지 중 가장 늦은 종료로 다시 계산된다
    const first = granted.body.data.sanction.id as string;
    const rev = await adminPost(app, owner, `/admin/sanctions/${first}/revoke`, { note: '오탐' });
    expect(rev.status).toBe(200);
    expect(Date.parse(rev.body.data.banned_until)).toBeLessThan(Date.now() + 25 * 3_600_000);
    const firstId = res.body.data.sanction.id as string;
    expect((await adminPost(app, owner, `/admin/sanctions/${firstId}/revoke`, { note: '해제' })).body.data.banned_until).toBeNull();
    expect((await adminPost(app, owner, `/admin/sanctions/${firstId}/revoke`, { note: '또' })).body.errors.code).toBe('SANCTION_REVOKED');
    expect((await request(pub).get('/characters').set(auth(h.s))).status).toBe(200);
  });

  it('입력 오류: 기간 규칙(경고에 기간, 채팅 금지 영구), 알 수 없는 사유, request_id 없음', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    const post1 = (b: Record<string, unknown>) => adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, b);
    expect((await post1({ kind: 'warning', reason_code: 'spam', duration: '1d', note: 'x' })).body.errors.code).toBe('BAD_DURATION');
    expect((await post1({ kind: 'chat_mute', reason_code: 'spam', duration: 'permanent', note: 'x' })).body.errors.code).toBe('BAD_DURATION');
    expect((await post1({ kind: 'ban', reason_code: 'spam', note: 'x' })).body.errors.code).toBe('BAD_DURATION');
    expect((await post1({ kind: 'ban', reason_code: 'nope', duration: '1d', note: 'x' })).status).toBe(400);
    const noId = await request(app).post(`/admin/accounts/${acct}/sanctions`).set({ Authorization: `Bearer ${operator.token}` }).send({ kind: 'warning', reason_code: 'spam', note: 'x' });
    expect(noId.status).toBe(400);
  });

  it('재전송(같은 request_id)은 처음 결과 그대로, 다른 본문은 422, 동시 두 번은 한 건만 만든다', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    const r = rid();
    const b = { kind: 'chat_mute', reason_code: 'spam', duration: '1h', note: '도배' };
    const [x, y] = await Promise.all([
      adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, b, r),
      adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, b, r),
    ]);
    expect([x.status, y.status]).toEqual([201, 201]);
    expect(x.body.data.sanction.id).toBe(y.body.data.sanction.id);
    expect([x.headers['idempotent-replay'], y.headers['idempotent-replay']].filter(Boolean)).toHaveLength(1);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM account_sanctions WHERE source = 'admin'")).n)).toBe(1);
    expect((await adminPost(app, operator, `/admin/accounts/${acct}/sanctions`, { ...b, duration: '1d' }, r)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    // 감사 행은 한 줄(ok)
    expect((await auditRows("action = 'sanction.create' AND result = 'ok'")).length).toBe(1);
  });

  it('제재가 접속 중인 플레이어에게 닿는다: sanction 프레임 후 4003', async () => {
    const h = await newHero(pub);
    const { c } = await connect(srv.port, h);
    sockets.push(c);
    await adminPost(app, operator, `/admin/accounts/${await accountUuid(h)}/sanctions`, { kind: 'ban', reason_code: 'abuse', duration: '1h', note: '욕설' });
    const f = await c.waitT('sanction');
    expect(f).toMatchObject({ kind: 'ban' });
    expect(await c.waitClose()).toBe(4003);
  });
});

describe('신고 처리(RP1~RP4)', () => {
  async function makeReport(reporter: Hero, target: Hero, reason = 'spam'): Promise<string> {
    const r = await getPool().query<{ uuid: string; id: string }>(
      `INSERT INTO reports (reporter_account_id, reporter_character_id, target_account_id, target_character_id, target_name, reason, line_count)
       SELECT (SELECT account_id FROM characters WHERE id = $1), $1, (SELECT account_id FROM characters WHERE id = $2), $2,
              (SELECT name FROM characters WHERE id = $2), $3, 1 RETURNING uuid, id`,
      [reporter.dbId, target.dbId, reason],
    );
    const row = r.rows[0] as { uuid: string; id: string };
    await getPool().query(
      `INSERT INTO report_lines (report_id, seq, channel, sender_account_id, sender_character_id, sender_name, text, sent_at, is_target)
       SELECT $1, 1, 'general', account_id, id, name, '광고 문구입니다', now(), true FROM characters WHERE id = $2`,
      [row.id, target.dbId],
    );
    return row.uuid;
  }

  it('대기열은 증거 없이, 상세는 operator가 증거 포함으로(감사). 검토 시작 -> 제재와 함께 처리, 비슷한 신고는 병합', async () => {
    const [a, b, t] = [await newHero(pub), await newHero(pub), await newHero(pub)];
    const r1 = await makeReport(a, t);
    const r2 = await makeReport(b, t);
    const list = await adminGet(app, viewer, '/admin/reports');
    expect(list.body.data.reports).toHaveLength(2);
    expect(JSON.stringify(list.body)).not.toContain('광고 문구입니다');
    expect((await adminGet(app, viewer, `/admin/reports/${r1}`)).status).toBe(403);
    const detail = await adminGet(app, operator, `/admin/reports/${r1}`);
    expect(detail.body.data.lines[0]).toMatchObject({ text: '광고 문구입니다', is_target: true });
    expect((await auditRows("action = 'report.view' AND result = 'ok'")).length).toBe(1);
    expect((await adminPost(app, operator, `/admin/reports/${r1}/take`)).body.data.report.state).toBe('reviewing');
    expect((await adminPost(app, operator, `/admin/reports/${r1}/take`)).body.errors.code).toBe('REPORT_CLOSED');

    const res = await adminPost(app, operator, `/admin/reports/${r1}/resolve`, { decision: 'sanction', note: '광고 확인', sanction: { kind: 'chat_mute', reason_code: 'spam', duration: '1d' } });
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ report: { state: 'actioned' }, merged: 1 });
    const states = await getPool().query('SELECT state, handled_by FROM reports ORDER BY id');
    expect(states.rows.map((x) => x.state)).toEqual(['actioned', 'actioned']);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM account_sanctions WHERE report_id IS NOT NULL")).n)).toBe(1);
    // 이미 닫힌 신고
    expect((await adminPost(app, operator, `/admin/reports/${r2}/resolve`, { decision: 'dismiss', note: 'x' })).body.errors.code).toBe('REPORT_CLOSED');
    // 기각
    const r3 = await makeReport(a, b, 'abuse');
    const dis = await adminPost(app, operator, `/admin/reports/${r3}/resolve`, { decision: 'dismiss', note: '증거 부족' });
    expect(dis.body.data.report.state).toBe('dismissed');
    // decision과 sanction 필드 불일치는 400
    expect((await adminPost(app, operator, `/admin/reports/${r3}/resolve`, { decision: 'dismiss', note: 'x', sanction: { kind: 'warning', reason_code: 'spam' } })).status).toBe(400);
  });
});

describe('보류 판 검토(HR1~HR4)', () => {
  async function heldRun(h: Hero, reason = 'TOO_FAST'): Promise<string> {
    const r = await getPool().query<{ uuid: string }>(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, party_size, state, reset_day, started_at, ended_at, room_index, room_kills, stats, hold_reason, humans, ai_count, counts_entry)
       VALUES ($1, 'gold_vein', 0, 1, 'held', date_trunc('day', now()), now() - interval '2 minutes', now() - interval '1 minute', 3,
               '{"0:skel_gold":3,"3:boss_gold_foreman":1}'::jsonb, '{"elapsed_ms": 9000, "hits_taken": 1, "max_combo": 900, "revives_used": 9}'::jsonb, $2, 1, 0, true)
       RETURNING uuid`,
      [h.dbId, reason],
    );
    return (r.rows[0] as { uuid: string }).uuid;
  }

  it('대기 목록, 상세(사유 설명), 해제: finalizeCleared 재사용으로 경험치·카드가 확정되고 접속 때 카드 미선택 판으로 보인다', async () => {
    const h = await newHero(pub);
    const run = await heldRun(h);
    const list = await adminGet(app, viewer, '/admin/held-runs');
    expect(list.body.data.runs[0]).toMatchObject({ id: run, hold_reason: ['TOO_FAST'], character_name: expect.any(String) });
    expect((await adminGet(app, viewer, `/admin/held-runs/${run}`)).status).toBe(403);
    const d = await adminGet(app, operator, `/admin/held-runs/${run}`);
    expect(d.body.data.hold[0]).toMatchObject({ code: 'TOO_FAST', min_clear_seconds: expect.any(Number) });
    expect((await auditRows("action = 'held_run.view' AND result = 'ok'")).length).toBe(1);

    const r = rid();
    const rel = await adminPost(app, operator, `/admin/held-runs/${run}/release`, { note: '수동 확인 후 해제' }, r);
    expect(rel.status).toBe(200);
    expect(rel.body.data).toMatchObject({ result: 'cleared', card_count: expect.any(Number) });
    const row = await dbOne<{ state: string; rank: number; cards: unknown[] }>('SELECT state, rank, cards FROM dungeon_runs WHERE uuid = $1', [run]);
    expect(row.state).toBe('cleared');
    expect(row.cards.length).toBeGreaterThan(0);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM xp_ledger WHERE reason = 'dungeon_clear' AND ref = $1", [run])).n)).toBe(1);
    const review = await dbOne<{ decision: string }>('SELECT decision FROM held_run_reviews');
    expect(review.decision).toBe('released');
    // 재전송은 같은 결과, 다른 request_id로 다시 해제하면 409(한 판에 한 번)
    const again = await adminPost(app, operator, `/admin/held-runs/${run}/release`, { note: '수동 확인 후 해제' }, r);
    expect(again.headers['idempotent-replay']).toBe('true');
    expect((await adminPost(app, operator, `/admin/held-runs/${run}/release`, { note: '또' })).body.errors.code).toBe('RUN_NOT_HELD');
    expect((await adminGet(app, viewer, '/admin/held-runs')).body.data.runs).toHaveLength(0);
    // 접속 때 카드 창을 띄울 판 목록(D5)
    const dungeons = await get(pub, h, '/dungeons');
    expect(dungeons.body.data.unpicked_runs.map((u: { run_id: string }) => u.run_id)).toContain(run);
  });

  it('동시 두 번 해제하면 정확히 한 번만 확정된다', async () => {
    const h = await newHero(pub);
    const run = await heldRun(h);
    const [x, y] = await Promise.all([
      adminPost(app, operator, `/admin/held-runs/${run}/release`, { note: 'a' }),
      adminPost(app, owner, `/admin/held-runs/${run}/release`, { note: 'b' }),
    ]);
    expect([x.status, y.status].sort()).toEqual([200, 409]);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM xp_ledger WHERE reason = 'dungeon_clear'")).n)).toBe(1);
    expect(Number((await dbOne<{ n: string }>('SELECT count(*) AS n FROM held_run_reviews')).n)).toBe(1);
  });

  it('거절은 기록만 남기고 판은 held로 남는다. 없는 판·보류가 아닌 판은 오류', async () => {
    const h = await newHero(pub);
    const run = await heldRun(h);
    const rej = await adminPost(app, operator, `/admin/held-runs/${run}/reject`, { note: '고의 의심' });
    expect(rej.status).toBe(200);
    expect((await dbOne<{ state: string }>('SELECT state FROM dungeon_runs WHERE uuid = $1', [run])).state).toBe('held');
    expect((await adminPost(app, operator, `/admin/held-runs/${run}/release`, { note: '번복' })).body.errors.code).toBe('RUN_NOT_HELD');
    expect((await adminPost(app, operator, `/admin/held-runs/${randomUUID()}/release`, { note: 'x' })).body.errors.code).toBe('RUN_NOT_FOUND');
    expect((await adminPost(app, operator, `/admin/held-runs/${run}/reject`, {})).status).toBe(400);
  });
});

describe('운영 지급(EC2)과 경제 요약', () => {
  it('골드와 아이템을 system 우편으로 지급: 수령하면 원장이 남고 보존식·정합성 점검이 맞는다', async () => {
    const h = await newHero(pub);
    const body = { character_id: h.id, system_code: 'compensation', gold: 5000, item: { item_key: ITEM, count: 3 }, memo: '문의 #123 보상' };
    expect((await adminPost(app, operator, '/admin/grants', body)).status).toBe(403);
    const res = await adminPost(app, owner, '/admin/grants', body);
    expect(res.status).toBe(201);
    expect(res.body.data.grant).toMatchObject({ gold: 5000, system_code: 'compensation' });
    const mailId = res.body.data.mail_id as string;
    const claim = await post(pub, h, `/mail/${mailId}/claim`, {});
    expect(claim.status).toBe(200);
    const gold = await dbOne<{ gold: string }>('SELECT gold FROM characters WHERE id = $1', [h.dbId]);
    expect(Number(gold.gold)).toBeGreaterThanOrEqual(5000);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM gold_ledger WHERE reason = 'mail_claim'")).n)).toBe(1);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM item_ledger WHERE reason = 'admin_grant'")).n)).toBe(1);
    expect(Number((await dbOne<{ c: string }>("SELECT coalesce(sum(count), 0) AS c FROM character_items WHERE character_id = $1 AND item_key = $2 AND location = $3", [h.dbId, ITEM, "bag"])).c)).toBeGreaterThanOrEqual(3);
    await expectConserved();
    const job = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    expect(job.detail.mismatches).toBe(0);
    const grants = await adminGet(app, viewer, '/admin/grants');
    expect(grants.body.data.items[0]).toMatchObject({ memo: '문의 #123 보상', mail_state: 'claimed' });
  });

  it('한도: 1회 골드 한도, 일일 한도, 없는 아이템, 없는 캐릭터. 재전송과 동시 두 번은 우편 한 통', async () => {
    const h = await newHero(pub);
    const base = { character_id: h.id, system_code: 'event', memo: '이벤트' };
    expect((await adminPost(app, owner, '/admin/grants', { ...base, gold: 1_000_001 })).body.errors.code).toBe('GRANT_LIMIT');
    expect((await adminPost(app, owner, '/admin/grants', { ...base, item: { item_key: 'no_such_item', count: 1 } })).body.errors.code).toBe('ITEM_NOT_FOUND');
    expect((await adminPost(app, owner, '/admin/grants', { ...base, character_id: randomUUID(), gold: 10 })).body.errors.code).toBe('CHARACTER_NOT_FOUND');
    expect((await adminPost(app, owner, '/admin/grants', { ...base })).status).toBe(400);
    const r = rid();
    const [x, y] = await Promise.all([
      adminPost(app, owner, '/admin/grants', { ...base, gold: 100 }, r),
      adminPost(app, owner, '/admin/grants', { ...base, gold: 100 }, r),
    ]);
    expect([x.status, y.status]).toEqual([201, 201]);
    expect(x.body.data.mail_id).toBe(y.body.data.mail_id);
    expect(Number((await dbOne<{ n: string }>("SELECT count(*) AS n FROM mails WHERE kind = 'system'")).n)).toBe(1);
    // 일일 한도(5,000,000): 지금까지 100 + 1,000,000 x 4 = 4,000,100, 다음 1,000,000은 한도 초과
    for (let i = 0; i < 4; i++) expect((await adminPost(app, owner, '/admin/grants', { ...base, gold: 1_000_000 })).status).toBe(201);
    const over = await adminPost(app, owner, '/admin/grants', { ...base, gold: 1_000_000 });
    expect(over.body.errors.code).toBe('GRANT_LIMIT');
    await expectConserved();
  });

  it('일일 경제 요약: 골드 유입·유출, 경험치, 경매, 신규 계정', async () => {
    await newHero(pub);
    const today = new Date(Date.now() + 9 * 3_600_000).toISOString().slice(0, 10);
    const res = await adminGet(app, viewer, `/admin/economy/daily?day=${today}`);
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ gold: { inflow: expect.any(Number) }, accounts: { new: expect.any(Number) }, auction: { trades: 0 } });
    expect((await adminGet(app, viewer, '/admin/economy/daily')).status).toBe(200);
    expect((await adminGet(app, viewer, '/admin/economy/daily?day=bad')).status).toBe(400);
  });
});

describe('감시 목록과 이상 기록', () => {
  it('점수 임계값 이상만 올라오고, 검토 확인(review_ack) 이후에는 다시 올라오지 않는다', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    const aid = (await dbOne<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [acct])).id;
    for (let i = 0; i < 3; i++) await getPool().query("INSERT INTO anomaly_log (account_id, character_id, kind, severity) VALUES ($1, $2, 'kill_rate', 1)", [aid, h.dbId]);
    expect((await adminGet(app, viewer, '/admin/watchlist')).body.data.items).toHaveLength(0);
    await getPool().query("INSERT INTO anomaly_log (account_id, character_id, kind, severity) VALUES ($1, $2, 'kill_power', 3)", [aid, h.dbId]);
    const w = await adminGet(app, viewer, '/admin/watchlist');
    expect(w.body.data.items[0]).toMatchObject({ account: acct, score: 13 });
    const an = await adminGet(app, viewer, `/admin/anomalies?account=${acct}&min_severity=3`);
    expect(an.body.data.items).toHaveLength(1);
    expect((await adminGet(app, viewer, '/admin/auction-flags')).status).toBe(200);
    await new Promise((r) => setTimeout(r, 20));
    expect((await adminPost(app, operator, `/admin/accounts/${acct}/notes`, { kind: 'review_ack', note: '검토 끝' })).status).toBe(201);
    expect((await adminGet(app, viewer, '/admin/watchlist')).body.data.items).toHaveLength(0);
  });
});

describe('권한 표와 속도 제한', () => {
  it('viewer는 조회만: 변경 POST는 모두 403이고, 토큰 없이는 401', async () => {
    const posts = ['/admin/accounts/dev', `/admin/accounts/${randomUUID()}/kick`, '/admin/maintenance', '/admin/broadcast', '/admin/grants', `/admin/reports/${randomUUID()}/take`];
    for (const p of posts) expect((await adminPost(app, viewer, p, {})).status).toBe(403);
    for (const p of ['/admin/ops/status', '/admin/ops/jobs', '/admin/maintenance', '/admin/maintenance/drain', '/admin/watchlist', '/admin/reports', '/admin/held-runs', '/admin/grants']) {
      expect((await adminGet(app, viewer, p)).status).toBe(200);
    }
    expect((await request(app).get('/admin/ops/status')).status).toBe(401);
    expect((await adminPost(app, operator, '/admin/ops/jobs/purge-hourly/run', {})).status).toBe(403);
  });

  it('변경 요청은 관리자별 분당 30회를 넘으면 429', async () => {
    const h = await newHero(pub);
    const acct = await accountUuid(h);
    let last = 0;
    for (let i = 0; i < 31; i++) last = (await adminPost(app, operator, `/admin/accounts/${acct}/notes`, { note: `n${i}` })).status;
    expect(last).toBe(429);
  });

  it('공개 서버에는 관리자 경로가 없다(404)', async () => {
    expect((await request(pub).get('/admin/me')).status).toBe(404);
  });
});
