// 5년 파기 통로, 파기 작업, 정책 레지스트리 (Docs/server/phase12_withdrawal.md 12절 T-W28 ~ T-W30)
import { randomUUID } from 'node:crypto';
import { Client } from 'pg';
import { getPool } from '../src/db/pool';
import { runDestroy } from '../src/domains/withdrawal/destroyService';
import { DESTROY_ORDER, DESTROY_STEPS, OWNER_COLUMN_RE, POLICY, PURGE_GRANT_TABLES, T1_STEPS, bindIds, unclassifiedTables, unknownPolicyTables } from '../src/domains/withdrawal/withdrawalPolicy';
import { withdrawalDestroyJob } from '../src/ops/jobs/withdrawalJobs';
import { setClockOverride } from '../src/utils/clock';
import { resetDb, shutdown } from './helpers';
import { buyoutReq, listIron, mk } from './auctionHelpers';
import { expectLedgerConsistent, seedGold, seedItem } from './economyHelpers';
import { payApp, purchase, resetPay, seedStars } from './payHelpers';
import { jobCtx, makeDue, makeRetained, requestWithdrawal, steamUser, withdrawalRows, type WUser } from './withdrawalHelpers';
import { withdrawalAnonymizeJob } from '../src/ops/jobs/withdrawalJobs';

const PURGE_PASSWORD = 'purge-pass-0123';
const db = () => getPool();
const count = async (sql: string, params: unknown[] = []): Promise<number> => Number(((await db().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);

/** 시험 DB 에 전용 역할을 만든다(운영 절차 3.4와 같다: LOGIN + 파기 대상 표의 SELECT, DELETE 와 관리대장 INSERT) */
async function ensurePurgeRole(): Promise<string> {
  const exists = await db().query("SELECT 1 FROM pg_roles WHERE rolname = 'dotrpg_purge'");
  if (exists.rows.length === 0) await db().query(`CREATE ROLE dotrpg_purge LOGIN PASSWORD '${PURGE_PASSWORD}'`);
  // 운영과 같은 최소 권한(ops/README.md 9절): 파기 대상 표만. 감사 로그 등은 주지 않는다
  await db().query(`GRANT SELECT, DELETE ON ${PURGE_GRANT_TABLES.join(', ')} TO dotrpg_purge`);
  await db().query('GRANT INSERT ON account_destruction_log TO dotrpg_purge');
  await db().query('GRANT USAGE ON SEQUENCE account_destruction_log_id_seq TO dotrpg_purge');
  const url = new URL(process.env.DATABASE_URL as string);
  url.username = 'dotrpg_purge';
  url.password = PURGE_PASSWORD;
  return url.toString();
}

let purgeUrl = '';
let app = payApp();
beforeAll(async () => {
  await resetDb();
  purgeUrl = await ensurePurgeRole();
});
beforeEach(() => resetPay());
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

describe('원장 트리거와 전용 역할 (T-W28)', () => {
  it('T-W28 앱 계정의 DELETE 는 거절, UPDATE·TRUNCATE 는 어떤 역할도 거절, dotrpg_purge 의 DELETE 는 허용 표에서만 통과한다', async () => {
    const h = await mk(app, 5000);
    await seedItem(h, 'potion_hp', 2);
    // 앱 계정(풀)은 지울 수 없다
    await expect(db().query('DELETE FROM gold_ledger WHERE character_id = $1', [h.dbId])).rejects.toThrow(/append-only/);
    await expect(db().query('UPDATE gold_ledger SET delta = delta WHERE character_id = $1', [h.dbId])).rejects.toThrow(/append-only/);
    await expect(db().query('TRUNCATE gold_ledger')).rejects.toThrow(/append-only/);
    // 전용 역할: 트리거가 막는지 보려고 권한을 일부러 넓혀 둔다(UPDATE, TRUNCATE 권한이 있어도 트리거가 거절해야 한다)
    await db().query("INSERT INTO admin_audit_log (login_id_tried, action, result) VALUES ('t', 'test.row', 'ok')");
    // 운영 권한에는 감사 로그가 없다
    expect((await db().query("SELECT has_table_privilege('dotrpg_purge', 'admin_audit_log', 'DELETE') AS d, has_table_privilege('dotrpg_purge', 'admin_audit_log', 'SELECT') AS s")).rows[0]).toEqual({ d: false, s: false });
    await db().query('GRANT UPDATE, TRUNCATE ON gold_ledger TO dotrpg_purge');
    await db().query('GRANT SELECT, DELETE ON account_destruction_log TO dotrpg_purge'); // 운영 권한은 INSERT 만. 트리거가 거절하는지 보려고 일부러 연다
    await db().query('GRANT SELECT, DELETE, UPDATE, TRUNCATE ON admin_audit_log TO dotrpg_purge');
    const c = new Client({ connectionString: process.env.DATABASE_URL });
    await c.connect();
    try {
      await c.query('SET SESSION AUTHORIZATION dotrpg_purge');
      await c.query('BEGIN');
      await expect(c.query('UPDATE gold_ledger SET delta = delta WHERE character_id = $1', [h.dbId])).rejects.toThrow(/append-only/);
      await c.query('ROLLBACK');
      await c.query('BEGIN');
      await expect(c.query('TRUNCATE gold_ledger')).rejects.toThrow(/append-only/);
      await c.query('ROLLBACK');
      // 허용 표: 통과(롤백으로 되돌린다)
      await c.query('BEGIN');
      const del = await c.query('DELETE FROM gold_ledger WHERE character_id = $1', [h.dbId]);
      expect(del.rowCount).toBeGreaterThan(0);
      const delItem = await c.query('DELETE FROM item_ledger WHERE character_id = $1', [h.dbId]);
      expect(delItem.rowCount).toBeGreaterThan(0);
      await c.query('ROLLBACK');
      // 허용 표 밖: 거절(감사 로그는 위에서 권한을 일부러 열었어도 트리거가 거절한다)
      await c.query('BEGIN');
      await expect(c.query('DELETE FROM admin_audit_log')).rejects.toThrow(/append-only/);
      await c.query('ROLLBACK');
      // 파기 관리대장은 추가만 한다
      await c.query('BEGIN');
      await c.query("INSERT INTO account_destruction_log (account_uuid, requested_at, anonymized_at, mode, tables) VALUES (gen_random_uuid(), now(), now(), 'admin', '{}'::jsonb)");
      await expect(c.query('DELETE FROM account_destruction_log')).rejects.toThrow(/append-only/);
      await c.query('ROLLBACK');
    } finally {
      await c.query('RESET SESSION AUTHORIZATION').catch(() => undefined);
      await c.end();
      await db().query('REVOKE UPDATE, TRUNCATE ON gold_ledger FROM dotrpg_purge');
      await db().query('REVOKE ALL ON admin_audit_log FROM dotrpg_purge');
      await db().query('REVOKE SELECT, DELETE ON account_destruction_log FROM dotrpg_purge');
    }
    expect(await count('SELECT count(*) AS n FROM gold_ledger WHERE character_id = $1', [h.dbId])).toBeGreaterThan(0);
    await expectLedgerConsistent(h);
  });
});

describe('5년 파기 작업 (T-W29)', () => {
  /** 이 계정 소유 행 수(원장·지갑·재고) */
  interface Owned { gold: number; item: number; inv: number; stars: number; orders: number; wallet: number }
  const owned = async (u: WUser): Promise<Owned> => ({
    gold: await count('SELECT count(*) AS n FROM gold_ledger WHERE character_id = $1', [u.charId]),
    item: await count('SELECT count(*) AS n FROM item_ledger WHERE character_id = $1', [u.charId]),
    inv: await count('SELECT count(*) AS n FROM character_items WHERE character_id = $1', [u.charId]),
    stars: await count('SELECT count(*) AS n FROM star_ledger WHERE account_id = $1', [u.accountId]),
    orders: await count('SELECT count(*) AS n FROM star_orders WHERE account_id = $1', [u.accountId]),
    wallet: await count('SELECT count(*) AS n FROM star_wallets WHERE account_id = $1', [u.accountId]),
  });
  const heroOf = (u: WUser) => ({ s: { access: u.access } as never, id: u.charUuid, dbId: u.charId, cls: 'warrior' as const });

  async function withdrawn(over: Partial<{ paid: boolean; gold: number }> = {}): Promise<WUser> {
    const u = await steamUser(app);
    await seedGold(heroOf(u), over.gold ?? 700);
    await seedItem(heroOf(u), 'potion_hp', 3);
    await seedStars(u.accountId, 15);
    if (over.paid) await purchase(app, { access: u.access, accountUuid: u.accountUuid, accountId: u.accountId, steamId: u.steamId as string });
    expect((await requestWithdrawal(app, u, over.paid ? { ack_paid_loss: true } : {})).status).toBe(201);
    await makeDue(u.accountId);
    await withdrawalAnonymizeJob(jobCtx);
    expect((await withdrawalRows(u.accountId))[0]).toMatchObject({ state: 'completed' });
    return u;
  }
  const setApp = (env: Record<string, string>): void => {
    app = payApp(env);
  };

  it('dry-run 은 아무 행도 지우지 않고 건수만 기록한다. 켜면 기한이 지난 계정만 지우고, 상대가 활동 중이면 공유 기록과 껍데기를 남기며, 관리대장이 한 줄 생긴다', async () => {
    setApp({});
    // A: 활동 중인 친구가 있다(친구 행은 removed 가 되어 남는다 -> 껍데기 유지)
    const friend = await mk(app, 1000);
    const a = await steamUser(app);
    const fr = await (await import('supertest')).default(app).post('/friends/requests').set({ Authorization: `Bearer ${a.access}`, 'X-Client-Version': '0.2.0' }).send({ request_id: randomUUID(), character_id: a.charUuid, target: friend.id });
    expect(fr.status).toBe(201);
    const ac = await (await import('supertest')).default(app).post(`/friends/requests/${fr.body.data.request.id}/respond`).set({ Authorization: `Bearer ${friend.s.access}`, 'X-Client-Version': '0.2.0' }).send({ request_id: randomUUID(), accept: true });
    expect(ac.status).toBe(200);
    await seedGold(heroOf(a), 500);
    await seedStars(a.accountId, 10);
    expect((await requestWithdrawal(app, a)).status).toBe(201);
    await makeDue(a.accountId);
    await withdrawalAnonymizeJob(jobCtx);
    // B, Q: 서로 거래한 두 탈퇴 계정(결제 이력 포함) / C: 보관 기한이 남은 계정
    const b = await withdrawn({ paid: true });
    const seller = await steamUser(app);
    await seedGold(heroOf(seller), 100);
    const listing = await listIron(app, heroOf(seller) as never);
    const buyer = await steamUser(app);
    await seedGold(heroOf(buyer), 9000);
    expect((await buyoutReq(app, heroOf(buyer) as never, listing)).status).toBe(200);
    for (const u of [seller, buyer]) {
      expect((await requestWithdrawal(app, u)).status).toBe(201);
      await makeDue(u.accountId);
    }
    await withdrawalAnonymizeJob(jobCtx);
    const c = await withdrawn();
    for (const u of [a, b, seller, buyer]) await makeRetained(u.accountId);
    // C 는 기한 전(retain_until 이 미래): 건드리지 않는다
    expect((await withdrawalRows(c.accountId))[0]?.retain_until as Date).toBeInstanceOf(Date);
    expect((await withdrawalRows(c.accountId))[0]?.retain_until as Date).toEqual(expect.any(Date));
    await db().query("UPDATE account_withdrawals SET retain_until = now() + interval '1 year' WHERE account_id = $1", [c.accountId]);

    const before = {
      a: await owned(a), b: await owned(b), seller: await owned(seller), buyer: await owned(buyer), c: await owned(c),
      accounts: await count('SELECT count(*) AS n FROM accounts'),
      trades: await count('SELECT count(*) AS n FROM auction_trades'),
      log: await count('SELECT count(*) AS n FROM account_destruction_log'),
    };
    expect(before.b.gold).toBeGreaterThan(0);
    expect(before.trades).toBe(1);

    // ----- dry-run (기본) -----
    const dry = await withdrawalDestroyJob(jobCtx);
    expect(dry.detail).toMatchObject({ mode: 'dry_run', accounts: 4, destroyed: 0 });
    const planned = dry.detail.tables as Record<string, number>;
    expect(planned.gold_ledger).toBe(before.a.gold + before.b.gold + before.seller.gold + before.buyer.gold);
    expect(planned.star_ledger).toBeGreaterThan(0);
    expect(await count('SELECT count(*) AS n FROM accounts')).toBe(before.accounts);
    expect(await owned(b)).toEqual(before.b);
    expect(await count('SELECT count(*) AS n FROM account_destruction_log')).toBe(before.log);

    // ----- 실삭제 (스위치와 전용 역할 접속 문자열이 모두 있을 때만) -----
    setApp({ WITHDRAW_DESTROY_ENABLED: 'true', PURGE_DATABASE_URL: purgeUrl });
    const real = await runDestroy();
    expect(real.mode).toBe('scheduled');
    // B: 완전히 사라졌다(관리대장 한 줄, 탈퇴 행·계정 삭제). 주문까지 포함
    expect(await owned(b)).toEqual({ gold: 0, item: 0, inv: 0, stars: 0, orders: 0, wallet: 0 });
    expect(await count('SELECT count(*) AS n FROM accounts WHERE id = $1', [b.accountId])).toBe(0);
    expect(await count('SELECT count(*) AS n FROM characters WHERE account_id = $1', [b.accountId])).toBe(0);
    expect(await count('SELECT count(*) AS n FROM account_withdrawals WHERE account_id = $1', [b.accountId])).toBe(0);
    const logB = (await db().query('SELECT * FROM account_destruction_log WHERE account_uuid = $1', [b.accountUuid])).rows;
    expect(logB).toHaveLength(1);
    expect(logB[0]).toMatchObject({ mode: 'scheduled' });
    expect(logB[0].tables).toMatchObject({ gold_ledger: before.b.gold, shell_kept: false });
    expect(JSON.stringify(logB[0])).not.toContain(b.steamId as string);
    // C: 기한 전이라 그대로
    expect(await owned(c)).toEqual(before.c);
    expect(await count('SELECT count(*) AS n FROM accounts WHERE id = $1', [c.accountId])).toBe(1);
    // A: 원장 등은 지우지만 활동 중인 친구와의 기록(removed 행)이 남아 껍데기를 유지한다
    expect(await owned(a)).toMatchObject({ gold: 0, item: 0, stars: 0, wallet: 0 });
    expect(await count('SELECT count(*) AS n FROM accounts WHERE id = $1', [a.accountId])).toBe(1);
    expect(await count('SELECT count(*) AS n FROM characters WHERE account_id = $1', [a.accountId])).toBe(1);
    expect(await count('SELECT count(*) AS n FROM friendships WHERE requester_account_id = $1', [a.accountId])).toBe(1);
    expect((await withdrawalRows(a.accountId))[0]).toMatchObject({ state: 'completed' });
    const logA = (await db().query('SELECT tables FROM account_destruction_log WHERE account_uuid = $1', [a.accountUuid])).rows;
    expect(logA).toHaveLength(1);
    expect(logA[0].tables).toMatchObject({ shell_kept: true });
    expect(real.shellKept).toBeGreaterThanOrEqual(1);
    // 활동 중인 친구는 영향이 없다
    expect(await count('SELECT count(*) AS n FROM gold_ledger WHERE character_id = $1', [friend.dbId])).toBeGreaterThan(0);
    await expectLedgerConsistent(friend);

    // 서로 거래한 두 탈퇴 계정: 첫 실행에서 당사자 모두 파기 대상이라 체결 기록이 지워지고, 우편이 서로 묶여 남은 건은 다음 실행에서 끝난다
    const again = await runDestroy();
    expect(again.mode).toBe('scheduled');
    for (const u of [seller, buyer]) {
      expect(await count('SELECT count(*) AS n FROM accounts WHERE id = $1', [u.accountId])).toBe(0);
      expect(await count('SELECT count(*) AS n FROM characters WHERE account_id = $1', [u.accountId])).toBe(0);
      // 첫 실행에서 일부(shell_kept=true)를, 다음 실행에서 나머지를 지웠다: 마지막 줄이 완전 파기
      const logs = (await db().query('SELECT tables FROM account_destruction_log WHERE account_uuid = $1 ORDER BY id', [u.accountUuid])).rows;
      expect(logs.length).toBeGreaterThanOrEqual(1);
      expect(logs[logs.length - 1].tables).toMatchObject({ shell_kept: false });
    }
    expect(await count('SELECT count(*) AS n FROM auction_trades')).toBe(0);
    expect(await count('SELECT count(*) AS n FROM auction_listings WHERE seller_account_id = $1', [seller.accountId])).toBe(0);
    // A 는 여전히 껍데기만 남고, 관리대장에 같은 줄이 늘어나지 않는다(지운 것이 없으면 기록하지 않는다)
    expect(await count('SELECT count(*) AS n FROM account_destruction_log WHERE account_uuid = $1', [a.accountUuid])).toBe(1);
    // 상대(친구)가 파기 대상이 되면 A 도 마저 정리된다: 친구도 탈퇴 -> 익명화 -> 기한 경과
    setApp({});
    const fu: WUser = { access: friend.s.access, accountUuid: friend.s.accountId, accountId: Number(((await db().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [friend.s.accountId])).rows[0] as { id: string }).id), steamId: null, loginId: friend.s.loginId, password: friend.s.password, charUuid: friend.id, charId: friend.dbId, name: '' };
    expect((await requestWithdrawal(app, fu)).status).toBe(201);
    await makeDue(fu.accountId);
    await withdrawalAnonymizeJob(jobCtx);
    await makeRetained(fu.accountId);
    setApp({ WITHDRAW_DESTROY_ENABLED: 'true', PURGE_DATABASE_URL: purgeUrl });
    await runDestroy();
    await runDestroy();
    expect(await count('SELECT count(*) AS n FROM accounts WHERE id = ANY($1::bigint[])', [[a.accountId, fu.accountId]])).toBe(0);
    expect(await count('SELECT count(*) AS n FROM friendships WHERE requester_account_id = $1 OR target_account_id = $1', [a.accountId])).toBe(0);
  });

  it('실삭제 경로는 스위치가 꺼져 있으면 열리지 않는다(PURGE_DATABASE_URL 이 있어도 dry-run). 설정 검증은 T-W34', async () => {
    setApp({ PURGE_DATABASE_URL: purgeUrl });
    const u = await withdrawn();
    await makeRetained(u.accountId);
    const r = await runDestroy();
    expect(r.mode).toBe('dry_run');
    expect(await count('SELECT count(*) AS n FROM accounts WHERE id = $1', [u.accountId])).toBe(1);
    await db().query("UPDATE account_withdrawals SET retain_until = now() + interval '1 year' WHERE account_id = $1", [u.accountId]);
  });
});

describe('정책 레지스트리 대조 (T-W30)', () => {
  const tablesInDb = async (): Promise<string[]> =>
    (await db().query<{ table_name: string }>("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")).rows.map((r) => r.table_name);

  it('T-W30 계정·캐릭터 열을 가진 모든 표(와 모든 표)가 분류돼 있다. 새 표를 분류하지 않으면 실패한다', async () => {
    const all = await tablesInDb();
    expect(unclassifiedTables(all)).toEqual([]);
    expect(unknownPolicyTables(all)).toEqual([]);
    const cols = await db().query<{ table_name: string; column_name: string }>(
      "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1, 2",
    );
    const withOwner = [...new Set(cols.rows.filter((c) => OWNER_COLUMN_RE.test(c.column_name)).map((c) => c.table_name))];
    expect(withOwner.length).toBeGreaterThan(40);
    const known = new Set(POLICY.map((p) => p.table));
    expect(withOwner.filter((t) => !known.has(t))).toEqual([]);
    // 같은 표가 두 번 분류되지 않았다
    expect(new Set(POLICY.map((p) => p.table)).size).toBe(POLICY.length);
    // 파기 순서와 분류가 일치한다
    expect(new Set(DESTROY_ORDER).size).toBe(DESTROY_ORDER.length);
    expect(POLICY.filter((p) => p.destroyWhere !== undefined).map((p) => p.table).sort()).toEqual([...DESTROY_ORDER].sort());
    // 익명화 때 지우는 표는 문장이 있고, 보관(keep)인 표는 T1 문장이 없다
    for (const p of POLICY) {
      // accounts / characters 의 익명화는 코드가 직접 한다(껍데기 행)
      if (p.t2 === 'shell') continue;
      if (p.t1 === 'delete' || p.t1 === 'anonymize') expect({ t: p.table, n: p.t1Sql?.length ?? 0 }).not.toEqual({ t: p.table, n: 0 });
      if (p.t1 === 'keep') expect(p.t1Sql).toBeUndefined();
    }
  });

  it('T-W30 모든 T1·파기 문장이 문법과 열 이름 면에서 유효하다(EXPLAIN), 자식 -> 부모 순서에 외래 키 위반이 없다', async () => {
    for (const s of T1_STEPS) {
      const q = bindIds(s.sql, [0], [0]);
      await db().query(`EXPLAIN ${q.text}`, q.params);
    }
    for (const s of DESTROY_STEPS) {
      const q = bindIds(`DELETE FROM ${s.table} WHERE ${s.destroyWhere}`, [0], [0]);
      await db().query(`EXPLAIN ${q.text}`, q.params);
    }
    // 외래 키: 부모가 자식보다 앞서 지워지지 않는다(둘 다 파기 대상일 때)
    const fks = await db().query<{ child: string; parent: string }>(
      `SELECT c.conrelid::regclass::text AS child, c.confrelid::regclass::text AS parent FROM pg_constraint c
        WHERE c.contype = 'f' AND c.conrelid <> c.confrelid`,
    );
    const pos = new Map(DESTROY_ORDER.map((t, i) => [t, i]));
    const bad = fks.rows.filter((f) => pos.has(f.child) && pos.has(f.parent) && (pos.get(f.parent) as number) < (pos.get(f.child) as number));
    expect(bad).toEqual([]);
    // 파기 대상 표에 추가 전용 트리거가 있으면 전용 역할 허용 목록에 있어야 한다
    const src = (await db().query<{ prosrc: string }>("SELECT prosrc FROM pg_proc WHERE proname = 'ledger_block_mutation'")).rows[0]?.prosrc as string;
    const allowed = new Set([...src.matchAll(/'([a-z_]+)'/g)].map((m) => m[1] as string));
    const guarded = await db().query<{ t: string }>(
      `SELECT DISTINCT tgrelid::regclass::text AS t FROM pg_trigger WHERE tgfoid = 'ledger_block_mutation'::regproc AND (tgtype & 8) = 8`,
    );
    const missing = guarded.rows.map((r) => r.t).filter((t) => DESTROY_ORDER.includes(t) && !allowed.has(t));
    expect(missing).toEqual([]);
    expect(allowed.has('admin_audit_log')).toBe(false);
    expect(allowed.has('account_destruction_log')).toBe(false);
  });
});
