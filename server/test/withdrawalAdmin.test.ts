// 관리자 탈퇴 도구 WD1~WD8, CLI, 경제 정지 연동 (Docs/server/phase12_withdrawal.md 12절 T-W31 ~ T-W33)
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { COMMANDS, type CmdCtx } from '../src/admin/cli/cliCommands';
import { identityHash } from '../src/domains/withdrawal/withdrawalIdentity';
import { withdrawalAnonymizeJob } from '../src/ops/jobs/withdrawalJobs';
import { MANUAL_JOBS } from '../src/ops/jobs';
import { resetDb, shutdown } from './helpers';
import { adminApp, adminGet, adminPost, auditRows, bearer, makeAdmin, rid, type TestAdmin } from './opsHelpers';
import { newSteamId, payApp, resetPay } from './payHelpers';
import {
  accountRow, charNames, devUser, jobCtx, makeDue, openWithdrawal, requestWithdrawal, steamLoginApi, steamUser, withdrawalRows,
} from './withdrawalHelpers';

let app: Express = payApp();
let adm: Express;
let viewer: TestAdmin;
let operator: TestAdmin;
let owner: TestAdmin;
const db = () => getPool();
const count = async (sql: string, params: unknown[] = []): Promise<number> => Number(((await db().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);

beforeAll(async () => {
  await resetDb();
  adm = adminApp();
  viewer = await makeAdmin('viewer');
  operator = await makeAdmin('operator');
  owner = await makeAdmin('owner');
});
beforeEach(() => resetPay());
afterAll(shutdown);

const start = (a: TestAdmin, accountUuid: string, body: Record<string, unknown> = {}, requestId?: string) =>
  adminPost(adm, a, `/admin/accounts/${accountUuid}/withdrawal`, { note: '지원 채널 요청', ...body }, requestId);
const wuuid = async (accountId: number): Promise<string> => ((await db().query<{ uuid: string }>('SELECT uuid FROM account_withdrawals WHERE account_id = $1 ORDER BY id DESC LIMIT 1', [accountId])).rows[0] as { uuid: string }).uuid;
const ban = (accountId: number, ends: string | null = "now() + interval '10 days'") =>
  db().query(`INSERT INTO account_sanctions (account_id, kind, source, reason_code, starts_at, ends_at, created_by) VALUES ($1, 'ban', 'admin', 'cheat', now() - interval '1 hour', ${ends ?? 'NULL'}, 't')`, [accountId]);

describe('WD3 운영자 대행 요청 (T-W31)', () => {
  it('T-W31 source=admin, 정지 계정도 가능, 감사 행이 같은 트랜잭션, 메모 원문은 감사 params 에 없다. 같은 request_id 는 멱등', async () => {
    const u = await steamUser(app);
    await ban(u.accountId);
    await db().query("UPDATE accounts SET banned_until = now() + interval '10 days' WHERE id = $1", [u.accountId]);
    // 정지 계정은 본인이 W2를 부를 수 없다(403 ACCOUNT_BANNED)
    const self = await requestWithdrawal(app, u);
    expect(self.status).toBe(403);
    expect(self.body.errors.code).toBe('ACCOUNT_BANNED');

    const requestId = rid();
    const res = await start(operator, u.accountUuid, { note: '고객지원 접수 12345' }, requestId);
    expect(res.status).toBe(201);
    expect(res.body.data).toMatchObject({ logged_out: true, withdrawal: { cancel_allowed: true } });
    const w = await openWithdrawal(u.accountId);
    expect(w).toMatchObject({ source: 'admin', cancel_allowed: true, state: 'requested' });
    expect(Number(w.requested_by_admin_id)).toBe(operator.id);
    expect((await accountRow(u.accountId)).deleted_at).not.toBeNull();
    expect((await charNames(u.accountId))[0]).toMatch(/^탈퇴[0-9a-f]{6}$/);
    const audits = await auditRows("action = 'withdrawal.start' AND request_id = $1", [requestId]);
    expect(audits).toHaveLength(1);
    expect(audits[0]).toMatchObject({ result: 'ok', target_type: 'account', target_uuid: u.accountUuid });
    expect(audits[0]?.params).toEqual({ note_len: '고객지원 접수 12345'.length, cancel_allowed: true });
    expect(JSON.stringify(audits[0])).not.toContain('고객지원 접수');
    // 메모는 계정 메모에만 남는다
    const notes = await db().query('SELECT note FROM admin_account_notes WHERE account_id = $1', [u.accountId]);
    expect(notes.rows[0]?.note).toContain('고객지원 접수 12345');
    // 요청 때문에 정지 계정의 토큰도 끊긴다. 정지는 유지된다
    expect(await count("SELECT count(*) AS n FROM account_sanctions WHERE account_id = $1 AND kind = 'ban'", [u.accountId])).toBe(1);

    // 멱등: 같은 request_id 는 저장된 응답, 다른 본문은 422
    const replay = await start(operator, u.accountUuid, { note: '고객지원 접수 12345' }, requestId);
    expect(replay.status).toBe(201);
    expect(replay.headers['idempotent-replay']).toBe('true');
    expect(replay.body.data).toEqual(res.body.data);
    const mismatch = await start(operator, u.accountUuid, { note: '다른 메모입니다.' }, requestId);
    expect(mismatch.status).toBe(422);
    expect(mismatch.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    expect(await withdrawalRows(u.accountId)).toHaveLength(1);
    // 이미 요청된 계정은 409
    const dup = await start(operator, u.accountUuid);
    expect(dup.status).toBe(409);
    expect(dup.body.errors.code).toBe('WITHDRAWAL_ALREADY_REQUESTED');
  });

  it('권한: viewer 는 403, cancel_allowed=false 는 owner 만, 없는 계정 404, 입력 오류 400. 동시에 같은 request_id 를 보내도 요청 행은 1개', async () => {
    const a = await devUser(app);
    expect((await start(viewer, a.accountUuid)).status).toBe(403);
    const op = await start(operator, a.accountUuid, { cancel_allowed: false });
    expect(op.status).toBe(403);
    expect(op.body.errors.code).toBe('FORBIDDEN_ROLE');
    expect((await accountRow(a.accountId)).deleted_at).toBeNull();
    expect((await start(operator, randomUUID())).status).toBe(404);
    expect((await adminPost(adm, operator, `/admin/accounts/${a.accountUuid}/withdrawal`, {})).status).toBe(400);
    expect((await adminPost(adm, operator, `/admin/accounts/${a.accountUuid}/withdrawal`, { note: 'x', extra: 1 })).status).toBe(400);
    expect((await request(adm).post(`/admin/accounts/${a.accountUuid}/withdrawal`).send({ request_id: rid(), note: 'x' })).status).toBe(401);
    const forced = await start(owner, a.accountUuid, { cancel_allowed: false });
    expect(forced.status).toBe(201);
    expect(forced.body.data.withdrawal.cancel_allowed).toBe(false);
    expect((await openWithdrawal(a.accountId)).cancel_allowed).toBe(false);

    const b = await devUser(app);
    const same = rid();
    const [x, y] = await Promise.all([start(operator, b.accountUuid, {}, same), start(operator, b.accountUuid, {}, same)]);
    expect([x.status, y.status]).toEqual([201, 201]);
    expect(await withdrawalRows(b.accountId)).toHaveLength(1);
    expect(await auditRows("action = 'withdrawal.start' AND request_id = $1 AND result = 'ok'", [same])).toHaveLength(1);
  });
});

describe('WD1 ~ WD8 (T-W32)', () => {
  it('WD1 목록·필터·커서, WD2 상세와 보류 근거 건수(감사 withdrawal.view), PL2 요약', async () => {
    const [a, b, c] = [await devUser(app), await devUser(app), await devUser(app)];
    for (const u of [a, b, c]) expect((await requestWithdrawal(app, u)).status).toBe(201);
    await ban(b.accountId);
    await makeDue(b.accountId);
    await withdrawalAnonymizeJob(jobCtx); // b: 보류
    await makeDue(c.accountId);
    await withdrawalAnonymizeJob(jobCtx); // c: 완료(a 도 같은 실행에서 기한 전)
    expect((await adminGet(adm, viewer, '/admin/withdrawals')).status).toBe(200);
    const all = await adminGet(adm, viewer, '/admin/withdrawals?limit=100');
    const ids = all.body.data.items.map((x: { account_id: string }) => x.account_id);
    expect(ids).toEqual(expect.arrayContaining([a.accountUuid, b.accountUuid, c.accountUuid]));
    expect(JSON.stringify(all.body)).not.toMatch(/"account_id":\d+/);
    const deferred = await adminGet(adm, viewer, '/admin/withdrawals?deferred=true&limit=100');
    const dItem = deferred.body.data.items.find((x: { account_id: string }) => x.account_id === b.accountUuid);
    expect(dItem).toMatchObject({ state: 'requested', defer_reasons: ['sanction'], manual_hold: false });
    expect(dItem.remaining_days).toBeGreaterThanOrEqual(0);
    expect((await adminGet(adm, viewer, '/admin/withdrawals?state=completed&limit=100')).body.data.items.map((x: { account_id: string }) => x.account_id)).toContain(c.accountUuid);
    expect((await adminGet(adm, viewer, '/admin/withdrawals?state=bogus')).status).toBe(400);
    const page = await adminGet(adm, viewer, '/admin/withdrawals?limit=1');
    expect(page.body.data.items).toHaveLength(1);
    expect(typeof page.body.meta.next_cursor).toBe('string');
    const page2 = await adminGet(adm, viewer, `/admin/withdrawals?limit=1&cursor=${page.body.meta.next_cursor}`);
    expect(page2.body.data.items[0].id).not.toBe(page.body.data.items[0].id);
    expect(page.body.meta.next_cursor).toBe(page.body.data.items[0].id); // 내부 숫자 id 가 아니라 uuid
    expect((await adminGet(adm, viewer, '/admin/withdrawals?cursor=12')).status).toBe(400);

    const detail = await adminGet(adm, viewer, `/admin/accounts/${b.accountUuid}/withdrawal`);
    expect(detail.status).toBe(200);
    expect(detail.body.data.current).toMatchObject({ state: 'requested', defer_reasons: ['sanction'], source: 'self' });
    expect(detail.body.data.defer_evidence).toMatchObject({ active_sanctions: 1, active_economy_holds: 0, open_reports_against: 0, open_orders: 0 });
    expect(detail.body.data.history).toHaveLength(1);
    expect(await auditRows("action = 'withdrawal.view' AND target_uuid = $1", [b.accountUuid])).toHaveLength(1);
    expect((await adminGet(adm, viewer, `/admin/accounts/${randomUUID()}/withdrawal`)).status).toBe(404);
    // PL2 계정 상세에 요약이 붙는다(E6)
    const pl2 = await adminGet(adm, viewer, `/admin/accounts/${b.accountUuid}`);
    expect(pl2.status).toBe(200);
    expect(pl2.body.data.withdrawal).toMatchObject({ state: 'requested', defer_reasons: ['sanction'], history_count: 1, anonymized: false });
    const pl2c = await adminGet(adm, viewer, `/admin/accounts/${c.accountUuid}`);
    expect(pl2c.body.data.withdrawal).toMatchObject({ state: 'completed', anonymized: true });
  });

  it('WD4 유예 중 운영자 취소(기한이 지나면 409 WITHDRAWAL_DUE), WD6 보류 켜기·끄기, 작업은 보류를 건너뛴다', async () => {
    const u = await steamUser(app);
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    const id = await wuuid(u.accountId);
    expect((await adminPost(adm, viewer, `/admin/withdrawals/${id}/hold`, { on: true, note: '조사' })).status).toBe(403);
    const on = await adminPost(adm, operator, `/admin/withdrawals/${id}/hold`, { on: true, note: '조사 중' });
    expect(on.status).toBe(200);
    expect(on.body.data).toEqual({ manual_hold: true });
    expect(await openWithdrawal(u.accountId)).toMatchObject({ manual_hold: true, manual_hold_note: '조사 중', defer_reasons: ['manual'] });
    await makeDue(u.accountId);
    expect((await withdrawalAnonymizeJob(jobCtx)).detail).toMatchObject({ completed: 0 });
    expect((await openWithdrawal(u.accountId)).state).toBe('requested');
    expect((await auditRows("action = 'withdrawal.hold' AND target_uuid = $1", [u.accountUuid]))[0]?.params).toEqual({ note_len: 4, on: true });
    const off = await adminPost(adm, operator, `/admin/withdrawals/${id}/hold`, { on: false, note: '조사 끝' });
    expect(off.body.data).toEqual({ manual_hold: false });
    expect(await openWithdrawal(u.accountId)).toMatchObject({ manual_hold: false, manual_hold_note: null, defer_reasons: [] });

    // 기한이 지난 요청은 취소할 수 없다
    const due = await adminPost(adm, operator, `/admin/withdrawals/${id}/cancel`, { note: '늦음' });
    expect(due.status).toBe(409);
    expect(due.body.errors.code).toBe('WITHDRAWAL_DUE');
    // 새 요청으로 취소 성공
    const v = await devUser(app);
    expect((await requestWithdrawal(app, v)).status).toBe(201);
    const vid = await wuuid(v.accountId);
    expect((await adminPost(adm, viewer, `/admin/withdrawals/${vid}/cancel`, { note: 'x' })).status).toBe(403);
    const cancelled = await adminPost(adm, operator, `/admin/withdrawals/${vid}/cancel`, { note: '고객 요청 취소' });
    expect(cancelled.status).toBe(200);
    expect(cancelled.body.data).toMatchObject({ cancelled: true, renamed_characters: 0 });
    expect((await accountRow(v.accountId)).deleted_at).toBeNull();
    expect((await withdrawalRows(v.accountId))[0]).toMatchObject({ state: 'cancelled', cancelled_via: 'admin' });
    expect((await adminPost(adm, operator, `/admin/withdrawals/${vid}/cancel`, { note: '또' })).status).toBe(404);
    expect((await adminPost(adm, operator, `/admin/withdrawals/${randomUUID()}/cancel`, { note: 'x' })).status).toBe(404);
    // 본인은 다시 로그인할 수 있다
    expect((await request(app).post('/auth/dev/login').set({ 'X-Client-Version': '0.2.0' }).send({ login_id: v.loginId, password: v.password })).status).toBe(200);
  });

  it('WD5 즉시 익명화(owner): 보류 사유가 있으면 409 DEFERRED, override_deferral 이면 진행하고 이월 표시를 남긴다. 이미 익명화됐으면 409', async () => {
    const u = await steamUser(app);
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    await ban(u.accountId, null);
    const id = await wuuid(u.accountId);
    expect((await adminPost(adm, operator, `/admin/withdrawals/${id}/anonymize-now`, { note: '즉시 파기 요청' })).status).toBe(403);
    const deferred = await adminPost(adm, owner, `/admin/withdrawals/${id}/anonymize-now`, { note: '즉시 파기 요청' });
    expect(deferred.status).toBe(409);
    expect(deferred.body.errors).toMatchObject({ code: 'DEFERRED', defer_reasons: ['sanction'] });
    expect((await accountRow(u.accountId)).anonymized_at).toBeNull();
    expect(await count('SELECT count(*) AS n FROM auth_identities WHERE account_id = $1', [u.accountId])).toBe(1);
    const forced = await adminPost(adm, owner, `/admin/withdrawals/${id}/anonymize-now`, { note: '즉시 파기 요청', override_deferral: true });
    expect(forced.status).toBe(200);
    expect(forced.body.data).toEqual({ anonymized: true, forced: true, tombstone: true });
    expect((await accountRow(u.accountId)).anonymized_at).not.toBeNull();
    expect(await count('SELECT count(*) AS n FROM auth_identities WHERE account_id = $1', [u.accountId])).toBe(0);
    expect(await count('SELECT count(*) AS n FROM withdrawn_identities WHERE source_account_id = $1', [u.accountId])).toBe(1);
    expect((await auditRows("action = 'withdrawal.anonymize_now' AND result = 'ok' AND target_uuid = $1", [u.accountUuid]))[0]?.params).toEqual({ note_len: '즉시 파기 요청'.length, override_deferral: true });
    const again = await adminPost(adm, owner, `/admin/withdrawals/${id}/anonymize-now`, { note: '또 시도합니다' });
    expect(again.status).toBe(409);
    expect(again.body.errors.code).toBe('ALREADY_ANONYMIZED');
    // 기한 전이어도 보류 사유가 없으면 바로 익명화(유예 건너뛰기)
    const clean = await devUser(app);
    expect((await requestWithdrawal(app, clean)).status).toBe(201);
    const cid = await wuuid(clean.accountId);
    const done = await adminPost(adm, owner, `/admin/withdrawals/${cid}/anonymize-now`, { note: '정보주체 즉시 파기 요구' });
    expect(done.status).toBe(200);
    expect(done.body.data).toEqual({ anonymized: true, forced: false, tombstone: false });
    // 운영자 보류(manual)는 override 없이는 막는다
    const held = await devUser(app);
    expect((await requestWithdrawal(app, held)).status).toBe(201);
    const hid = await wuuid(held.accountId);
    await adminPost(adm, operator, `/admin/withdrawals/${hid}/hold`, { on: true, note: '보류합니다' });
    const heldTry = await adminPost(adm, owner, `/admin/withdrawals/${hid}/anonymize-now`, { note: '시도 중입니다' });
    expect(heldTry.status).toBe(409);
    expect(heldTry.body.errors).toMatchObject({ code: 'DEFERRED', defer_reasons: ['manual'] });
  });

  it('WD7 Steam ID 로 이월 표시 조회(서버가 HMAC 계산, 원문은 응답·감사에 남기지 않는다), WD8 해제(owner)', async () => {
    const u = await steamUser(app);
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    await ban(u.accountId, null);
    const id = await wuuid(u.accountId);
    // WD5 는 관리자당 분당 5회라 이 시험은 새 owner 로 부른다
    const owner2 = await makeAdmin('owner');
    expect((await adminPost(adm, owner2, `/admin/withdrawals/${id}/anonymize-now`, { note: '이월 테스트 계정', override_deferral: true })).status).toBe(200);
    const steam = u.steamId as string;
    const found = await adminGet(adm, viewer, `/admin/tombstones?steam=${steam}`);
    expect(found.status).toBe(200);
    expect(found.body.data).toMatchObject({ found: true, hash_prefix: identityHash(steam).slice(0, 8), tombstone: { released: false, expired: false } });
    expect(found.body.data.tombstone.carry_ban_until).toMatch(/^9999-/);
    expect(JSON.stringify(found.body)).not.toContain(steam);
    expect(JSON.stringify(await auditRows("action = 'tombstone.view'"))).not.toContain(steam);
    expect((await adminGet(adm, viewer, `/admin/tombstones?steam=${newSteamId()}`)).body.data).toMatchObject({ found: false, tombstone: null });
    expect((await adminGet(adm, viewer, '/admin/tombstones?steam=12345')).status).toBe(400);
    expect((await adminGet(adm, viewer, '/admin/tombstones')).status).toBe(400);
    // 재가입은 정지 이월로 막힌다 -> 해제하면 통과
    expect((await steamLoginApi(app, steam)).status).toBe(403);
    const tid = found.body.data.tombstone.id as string;
    expect(tid).toMatch(/^[0-9a-f-]{36}$/);
    expect((await adminPost(adm, operator, `/admin/tombstones/${tid}/release`, { note: '오탐 정정' })).status).toBe(403);
    const rel = await adminPost(adm, owner, `/admin/tombstones/${tid}/release`, { note: '오탐 정정입니다' });
    expect(rel.status).toBe(200);
    expect(rel.body.data).toEqual({ released: true, already: false });
    const rel2 = await adminPost(adm, owner, `/admin/tombstones/${tid}/release`, { note: '다시 해제합니다' });
    expect(rel2.body.data).toEqual({ released: true, already: true });
    expect((await adminGet(adm, viewer, `/admin/tombstones?steam=${steam}`)).body.data.tombstone).toMatchObject({ released: true, released_by: owner.loginId });
    expect((await steamLoginApi(app, steam)).status).toBe(201);
    expect((await adminPost(adm, owner, '/admin/tombstones/00000000-0000-4000-8000-000000000000/release', { note: '없는 표시' })).status).toBe(404);
  });

  it('CLI 명령은 관리자 API 한 개씩에 대응하고 ops run 허용 목록에 두 작업이 있다', async () => {
    const calls: { m: string; route: string; body?: unknown; query?: unknown }[] = [];
    const mk = (args: string[], flags: CmdCtx['flags'] = {}): CmdCtx => ({
      args,
      flags,
      get: async (route, query) => void calls.push({ m: 'GET', route, query }),
      post: async (route, body) => void calls.push({ m: 'POST', route, body }),
      prompt: async () => '',
    });
    const run = (path: string[], args: string[], flags: CmdCtx['flags'] = {}) => (COMMANDS.find((c) => c.path.join(' ') === path.join(' ')) as (typeof COMMANDS)[number]).run(mk(args, flags));
    await run(['withdraw', 'list'], [], { state: 'requested', deferred: true });
    await run(['withdraw', 'show'], ['acc-1']);
    await run(['withdraw', 'start'], ['acc-1'], { note: '사유', 'no-cancel': true });
    await run(['withdraw', 'cancel'], ['w-1'], { note: '사유' });
    await run(['withdraw', 'anonymize-now'], ['w-1'], { note: '사유', 'override-deferral': true });
    await run(['withdraw', 'hold'], ['w-1'], { on: true, note: '사유' });
    await run(['tombstone', 'find'], [], { steam: '76561198000000001' });
    await run(['tombstone', 'release'], ['t-7'], { note: '사유' });
    expect(calls.map((c) => `${c.m} ${c.route}`)).toEqual([
      'GET /admin/withdrawals', 'GET /admin/accounts/acc-1/withdrawal', 'POST /admin/accounts/acc-1/withdrawal', 'POST /admin/withdrawals/w-1/cancel',
      'POST /admin/withdrawals/w-1/anonymize-now', 'POST /admin/withdrawals/w-1/hold', 'GET /admin/tombstones', 'POST /admin/tombstones/t-7/release',
    ]);
    expect(calls[0]?.query).toMatchObject({ state: 'requested', deferred: 'true' });
    expect(calls[2]?.body).toEqual({ note: '사유', cancel_allowed: false });
    expect(calls[4]?.body).toEqual({ note: '사유', override_deferral: true });
    expect(calls[5]?.body).toEqual({ on: true, note: '사유' });
    expect(() => run(['withdraw', 'hold'], ['w-1'], { note: '사유' })).toThrow(/--on/);
    expect(MANUAL_JOBS).toEqual(expect.arrayContaining(['withdrawal-anonymize', 'withdrawal-destroy']));
    // 수동 실행 경로(OP3)도 두 작업을 받아들인다
    const run1 = await adminPost(adm, owner, '/admin/ops/jobs/withdrawal-anonymize/run', {});
    expect(run1.status).toBeLessThan(300);
    const run2 = await adminPost(adm, owner, '/admin/ops/jobs/withdrawal-destroy/run', {});
    expect(run2.status).toBeLessThan(300);
    expect(JSON.stringify(run2.body)).toMatch(/dry_run/);
    void bearer;
  });
});

describe('경제 정지 연동 (T-W33)', () => {
  it('T-W33 유예 계정에는 경제 정지를 걸 수 있고, 익명화된 계정에는 거절한다', async () => {
    const pending = await devUser(app);
    expect((await requestWithdrawal(app, pending)).status).toBe(201);
    const res = await adminPost(adm, operator, '/admin/economy/holds', { account_id: pending.accountUuid, note: '탈퇴 요청 중 조사' });
    expect(res.status).toBe(201);
    expect(res.body.data).toMatchObject({ state: 'active', created: true });
    // 보류 사유(economy_hold)로 잡혀 익명화가 미뤄진다
    await makeDue(pending.accountId);
    await withdrawalAnonymizeJob(jobCtx);
    expect(await openWithdrawal(pending.accountId)).toMatchObject({ state: 'requested', defer_reasons: ['economy_hold'] });
    expect((await accountRow(pending.accountId)).anonymized_at).toBeNull();

    const gone = await devUser(app);
    expect((await requestWithdrawal(app, gone)).status).toBe(201);
    await makeDue(gone.accountId);
    await withdrawalAnonymizeJob(jobCtx);
    expect((await accountRow(gone.accountId)).anonymized_at).not.toBeNull();
    const refused = await adminPost(adm, operator, '/admin/economy/holds', { account_id: gone.accountUuid, note: '이미 익명화됨' });
    expect(refused.status).toBe(404);
    expect(refused.body.errors.code).toBe('ACCOUNT_NOT_FOUND');
    // 유예 계정에도 제재와 조회가 된다(accounts.deleted_at 필터가 없다). 제재도 보류 사유가 된다
    const sanction = await adminPost(adm, operator, `/admin/accounts/${pending.accountUuid}/sanctions`, { kind: 'ban', reason_code: 'cheat', duration: '7d', note: '유예 중 제재' });
    expect(sanction.status).toBe(201);
    expect((await adminGet(adm, viewer, `/admin/accounts/${pending.accountUuid}`)).status).toBe(200);
    await withdrawalAnonymizeJob(jobCtx);
    expect((await openWithdrawal(pending.accountId)).defer_reasons).toEqual(expect.arrayContaining(['economy_hold', 'sanction']));
    await db().query("UPDATE account_sanctions SET revoked_at = now(), revoked_by = 't' WHERE account_id = $1", [pending.accountId]);
    await db().query('UPDATE accounts SET banned_until = NULL WHERE id = $1', [pending.accountId]);
    // 사유가 풀리면 다음 실행에서 처리된다
    await db().query("UPDATE economy_holds SET state = 'released', reviewed_at = now(), released_at = now() WHERE account_id = $1", [pending.accountId]);
    await withdrawalAnonymizeJob(jobCtx);
    expect((await withdrawalRows(pending.accountId))[0]).toMatchObject({ state: 'completed' });
  });
});

