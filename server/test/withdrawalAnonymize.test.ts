// 익명화 작업, 보류, 이월 표시, 재가입, 정합성 (Docs/server/phase12_withdrawal.md 12절 T-W18 ~ T-W27)
import { randomBytes, randomUUID } from 'node:crypto';
import request from 'supertest';
import { getConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { identityHash } from '../src/domains/withdrawal/withdrawalIdentity';
import { integrityJob } from '../src/ops/jobs/integrity';
import { purgeDaily } from '../src/ops/jobs/purge';
import { withdrawalAnonymizeJob } from '../src/ops/jobs/withdrawalJobs';
import { setClockOverride } from '../src/utils/clock';
import { resetDb, shutdown, ver } from './helpers';
import { expectLedgerConsistent, seedGold, seedItem, type Hero } from './economyHelpers';
import { newSteamId, payApp, purchase, resetPay, seedStars } from './payHelpers';
import {
  accountRow, charNames, jobCtx, makeDue, openWithdrawal, requestWithdrawal, steamLoginApi, steamUser, ticketFor, withdrawalRows, type WUser,
} from './withdrawalHelpers';
import { createChar, randomName } from './helpers';

const IP = '203.0.113.77';
const app = payApp({ TRUST_PROXY: '1' });
beforeAll(resetDb);
beforeEach(() => resetPay());
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

const db = () => getPool();
const count = async (sql: string, params: unknown[]): Promise<number> => Number(((await db().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);
const runJobOnce = () => withdrawalAnonymizeJob(jobCtx);

/** Steam 계정 + 기기·IP 기록 + 캐릭터 + 재화. 이 계정만 쓰는 IP(203.0.113.77)로 접속한다 */
async function richUser(): Promise<WUser & { hero: Hero }> {
  const steamId = newSteamId();
  const device = { install_id: randomUUID(), device_hash: randomBytes(32).toString('hex') };
  const login = await request(app).post('/auth/steam').set(ver()).set('X-Forwarded-For', IP).send({ ticket: ticketFor(steamId), device });
  expect(login.status).toBe(201);
  const access = login.body.data.access_token as string;
  const accountUuid = login.body.data.account.id as string;
  const acc = await db().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [accountUuid]);
  const accountId = Number((acc.rows[0] as { id: string }).id);
  const name = randomName();
  const ch = await createChar(app, { access } as never, name);
  expect(ch.status).toBe(201);
  const charUuid = ch.body.data.character.id as string;
  const cr = await db().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [charUuid]);
  const charId = Number((cr.rows[0] as { id: string }).id);
  const hero: Hero = { s: { access } as never, id: charUuid, dbId: charId, cls: 'warrior' };
  await seedGold(hero, 1234);
  await seedItem(hero, 'potion_hp', 5);
  await seedStars(accountId, 25);
  return { access, accountUuid, accountId, steamId, loginId: null, password: null, charUuid, charId, name, hero };
}

/** 요청 -> 기한 경과 -> 작업 1회 */
async function withdrawAndRun(u: WUser, over: Record<string, unknown> = {}): Promise<void> {
  expect((await requestWithdrawal(app, u, over)).status).toBe(201);
  await makeDue(u.accountId);
  await runJobOnce();
}

const state = async (accountId: number): Promise<string> => ((await db().query<{ state: string }>('SELECT state FROM account_withdrawals WHERE account_id = $1 ORDER BY id DESC LIMIT 1', [accountId])).rows[0] as { state: string }).state;

describe('작업 기본 동작 (T-W18, T-W19)', () => {
  it('T-W18 기한 전에는 건드리지 않고 후에는 한 번에 처리한다. 두 번 실행해도 같다(멱등). 중간 오류는 전체 롤백', async () => {
    const u = await richUser();
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    const early = await runJobOnce();
    expect(early.detail).toMatchObject({ scanned: 0, completed: 0 });
    expect((await accountRow(u.accountId)).anonymized_at).toBeNull();
    expect(await count('SELECT count(*) AS n FROM auth_identities WHERE account_id = $1', [u.accountId])).toBe(1);

    await makeDue(u.accountId);
    // 중간 오류: 계정 갱신(마지막 단계 근처)에서 실패시킨다 -> 앞에서 지운 것까지 모두 되돌아간다
    await db().query(`CREATE FUNCTION t_fail_anon() RETURNS trigger AS $$ BEGIN
      IF NEW.anonymized_at IS NOT NULL AND OLD.anonymized_at IS NULL THEN RAISE EXCEPTION 'boom'; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql`);
    await db().query('CREATE TRIGGER t_fail_anon BEFORE UPDATE ON accounts FOR EACH ROW EXECUTE FUNCTION t_fail_anon()');
    try {
      const failed = await runJobOnce();
      expect(failed.detail).toMatchObject({ failed: 1, completed: 0 });
    } finally {
      await db().query('DROP TRIGGER t_fail_anon ON accounts');
      await db().query('DROP FUNCTION t_fail_anon()');
    }
    expect(await count('SELECT count(*) AS n FROM auth_identities WHERE account_id = $1', [u.accountId])).toBe(1);
    expect(await count('SELECT count(*) AS n FROM login_events WHERE account_id = $1', [u.accountId])).toBeGreaterThan(0);
    expect(await state(u.accountId)).toBe('requested');
    expect((await charNames(u.accountId))[0]).toMatch(/^탈퇴[0-9a-f]{6}$/); // T0 정리는 롤백되지 않는다

    const done = await runJobOnce();
    expect(done.detail).toMatchObject({ completed: 1, failed: 0 });
    const first = (await withdrawalRows(u.accountId))[0] as Record<string, unknown>;
    expect(first).toMatchObject({ state: 'completed', saved_character_names: null });
    expect(first.retain_until).not.toBeNull();
    const snapshot = JSON.stringify([await accountRow(u.accountId), first]);
    // 두 번째 실행: 대상이 없다(결과 동일)
    const again = await runJobOnce();
    expect(again.detail).toMatchObject({ scanned: 0, completed: 0 });
    expect(JSON.stringify([await accountRow(u.accountId), (await withdrawalRows(u.accountId))[0]])).toBe(snapshot);
  });

  it('T-W19 익명화 뒤 신원·접속 기록·채팅이 비고, 나를 차단한 행의 이름과 신고 대상 이름이 자리표시가 된다', async () => {
    const u = await richUser();
    const other = await steamUser(app);
    const nick = u.name;
    // 채팅(보낸 것·받은 것), 내가 건 차단, 나를 차단한 행, 나에 대한 종결된 신고
    await db().query("INSERT INTO chat_messages (channel, shard, sender_account_id, sender_character_id, sender_name, text, client_msg_id) VALUES ('general', 1, $1, $2, $3, '안녕', gen_random_uuid())", [u.accountId, u.charId, nick]);
    await db().query(
      "INSERT INTO chat_messages (channel, sender_account_id, sender_character_id, sender_name, recipient_account_id, recipient_character_id, recipient_name, text, client_msg_id) VALUES ('whisper', $1, $2, 'x', $3, $4, $5, '귓속말', gen_random_uuid())",
      [other.accountId, other.charId, u.accountId, u.charId, nick],
    );
    await db().query('INSERT INTO blocks (blocker_account_id, blocked_account_id, blocked_character_id, blocked_name) VALUES ($1, $2, $3, $4)', [u.accountId, other.accountId, other.charId, other.name]);
    await db().query('INSERT INTO blocks (blocker_account_id, blocked_account_id, blocked_character_id, blocked_name) VALUES ($1, $2, $3, $4)', [other.accountId, u.accountId, u.charId, nick]);
    await db().query(
      "INSERT INTO reports (reporter_account_id, reporter_character_id, target_account_id, target_character_id, target_name, reason, state, handled_at, handled_by) VALUES ($1, $2, $3, $4, $5, 'abuse', 'dismissed', now(), 'tester')",
      [other.accountId, other.charId, u.accountId, u.charId, nick],
    );
    await db().query(
      `INSERT INTO online_sessions (account_id, character_id, family_id, ip, device_hash, map_id, map_since, map_changed_at, last_seen_at)
       VALUES ($1, $2, gen_random_uuid(), '203.0.113.77', $3, 'village', now(), now(), now())`,
      [u.accountId, u.charId, randomBytes(32).toString('hex')],
    );
    await withdrawAndRun(u);
    expect(await state(u.accountId)).toBe('completed');
    for (const t of ['auth_identities', 'refresh_tokens', 'login_events', 'account_devices', 'account_ips', 'online_sessions']) {
      expect(await count(`SELECT count(*) AS n FROM ${t} WHERE account_id = $1`, [u.accountId])).toBe(0);
    }
    expect(await count('SELECT count(*) AS n FROM chat_messages WHERE sender_account_id = $1 OR recipient_account_id = $1', [u.accountId])).toBe(0);
    expect(await count('SELECT count(*) AS n FROM blocks WHERE blocker_account_id = $1', [u.accountId])).toBe(0);
    const mine = await db().query<{ blocked_name: string }>('SELECT blocked_name FROM blocks WHERE blocked_account_id = $1', [u.accountId]);
    expect(mine.rows).toHaveLength(1);
    expect(mine.rows[0]?.blocked_name).toMatch(/^탈퇴[0-9a-f]{6}$/);
    const rep = await db().query<{ target_name: string }>('SELECT target_name FROM reports WHERE target_account_id = $1', [u.accountId]);
    expect(rep.rows[0]?.target_name).toMatch(/^탈퇴[0-9a-f]{6}$/);
    const acc = await accountRow(u.accountId);
    expect(acc).toMatchObject({ last_login_at: null, prev_login_at: null, last_character_id: null, active_family_id: null });
    expect(acc.anonymized_at).not.toBeNull();
    const chars = await db().query('SELECT name, deleted_at FROM characters WHERE account_id = $1', [u.accountId]);
    expect(chars.rows.every((c: { name: string; deleted_at: Date | null }) => /^탈퇴[0-9a-f]{6}$/.test(c.name) && c.deleted_at !== null)).toBe(true);
    // 이미 지운 캐릭터(원래 이름 그대로 남은 것)도 가린다: 소프트 삭제된 캐릭터가 있던 계정
    const v = await richUser();
    const extra = await createChar(app, { access: v.access } as never, randomName());
    expect(extra.status).toBe(201);
    const extraUuid = extra.body.data.character.id as string;
    const del = await request(app).delete(`/characters/${extraUuid}`).set('Authorization', `Bearer ${v.access}`).set(ver());
    expect(del.status).toBe(200);
    const origExtra = (await db().query<{ name: string }>('SELECT name FROM characters WHERE uuid = $1', [extraUuid])).rows[0]?.name as string;
    expect(origExtra).not.toMatch(/^탈퇴/);
    await withdrawAndRun(v);
    expect((await db().query<{ name: string }>('SELECT name FROM characters WHERE uuid = $1', [extraUuid])).rows[0]?.name).toMatch(/^탈퇴[0-9a-f]{6}$/);
  });
});

describe('PII 잔존 점검과 원장 불변 (T-W20, T-W21, T-W27)', () => {
  /** 모든 텍스트·INET·JSONB 열에서 needle 을 찾아 "표.열" 목록을 돌려준다 */
  async function findEverywhere(needle: string): Promise<string[]> {
    const cols = await db().query<{ table_name: string; column_name: string; data_type: string }>(
      `SELECT c.table_name, c.column_name, c.data_type FROM information_schema.columns c JOIN information_schema.tables t
         ON t.table_name = c.table_name AND t.table_schema = c.table_schema
        WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' AND c.data_type IN ('text', 'character varying', 'inet', 'jsonb', 'json', 'ARRAY')`,
    );
    const hits: string[] = [];
    for (const c of cols.rows) {
      const r = await db().query<{ n: string }>(`SELECT count(*) AS n FROM "${c.table_name}" WHERE "${c.column_name}"::text LIKE $1`, [`%${needle}%`]);
      if (Number((r.rows[0] as { n: string }).n) > 0) hits.push(`${c.table_name}.${c.column_name}`);
    }
    return hits;
  }

  it('T-W20 익명화한 계정의 Steam ID·닉네임·IP 는 허용 위치(star_orders.steam_id, report_lines)에만 남는다', async () => {
    const u = await richUser();
    await purchase(app, { access: u.access, accountUuid: u.accountUuid, accountId: u.accountId, steamId: u.steamId as string });
    // 접속 기록이 실제로 이 IP 로 쌓였는지(점검이 의미 있도록) 먼저 확인
    expect(await findEverywhere(IP)).toEqual(expect.arrayContaining(['login_events.ip']));
    expect(await findEverywhere(u.steamId as string)).toEqual(expect.arrayContaining(['auth_identities.subject', 'star_orders.steam_id']));
    await withdrawAndRun(u, { ack_paid_loss: true });
    expect(await state(u.accountId)).toBe('completed');
    const allowed = new Set(['star_orders.steam_id', 'report_lines.sender_name', 'report_lines.body']);
    for (const needle of [u.steamId as string, u.name, IP]) {
      const hits = (await findEverywhere(needle)).filter((h) => !allowed.has(h));
      expect({ needle: needle === u.name ? '닉네임' : needle, hits }).toEqual({ needle: needle === u.name ? '닉네임' : needle, hits: [] });
    }
  });

  it('T-W21 원장·지갑·재고가 익명화 전후 동일하고, 야간 정합성 점검 I1~I3·I6·I7 이 전체에서 0이다', async () => {
    const u = await richUser();
    await purchase(app, { access: u.access, accountUuid: u.accountUuid, accountId: u.accountId, steamId: u.steamId as string });
    const snap = async () => {
      const r = await db().query(
        `SELECT (SELECT count(*) FROM gold_ledger WHERE character_id = $2) AS g, (SELECT coalesce(sum(delta), 0) FROM gold_ledger WHERE character_id = $2) AS gs,
                (SELECT count(*) FROM item_ledger WHERE character_id = $2) AS i, (SELECT coalesce(sum(delta), 0) FROM item_ledger WHERE character_id = $2) AS isum,
                (SELECT count(*) FROM xp_ledger WHERE character_id = $2) AS x, (SELECT count(*) FROM star_ledger WHERE account_id = $1) AS sl,
                (SELECT gold FROM characters WHERE id = $2) AS gold, (SELECT balance || '/' || paid_balance || '/' || debt FROM star_wallets WHERE account_id = $1) AS wallet,
                (SELECT coalesce(sum(remaining), 0) FROM star_paid_lots WHERE account_id = $1) AS lots, (SELECT count(*) FROM star_orders WHERE account_id = $1) AS orders,
                (SELECT md5(string_agg(item_key || ':' || count || ':' || location, ',' ORDER BY id)) FROM character_items WHERE character_id = $2) AS inv`,
        [u.accountId, u.charId],
      );
      return r.rows[0];
    };
    const before = await snap();
    await withdrawAndRun(u, { ack_paid_loss: true });
    expect(await state(u.accountId)).toBe('completed');
    expect(await snap()).toEqual(before);
    // 보관 기한(2절): 결제·원장 마지막 시각 + 5년 중 늦은 쪽, 최소 익명화 + 1일
    const ret = await db().query<{ ok: boolean }>(
      `SELECT w.retain_until >= greatest((SELECT max(created_at) FROM star_ledger WHERE account_id = $1) + interval '5 years' - interval '1 minute',
                                         (SELECT max(created_at) FROM gold_ledger WHERE character_id = $2) + interval '5 years' - interval '1 minute',
                                         w.anonymized_at + interval '1 day') AS ok
         FROM account_withdrawals w WHERE w.account_id = $1`,
      [u.accountId, u.charId],
    );
    expect(ret.rows[0]).toEqual({ ok: true });
    await expectLedgerConsistent(u.hero);
    const res = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    const checks = res.detail.checks as Record<string, { count: number; samples: unknown[] }>;
    expect({ I1: checks.I1?.count, I2: checks.I2?.count, I3: checks.I3?.count, I6: checks.I6?.count, I7: checks.I7?.count, samples: checks.I7?.samples }).toEqual({
      I1: 0, I2: 0, I3: 0, I6: 0, I7: 0, samples: [],
    });
    expect(res.detail.mismatches).toBe(0);
  });

  it('T-W27 야간 정합성 점검 I7 이 익명화 누락·상태 어긋남을 잡는다', async () => {
    const u = await richUser();
    await withdrawAndRun(u);
    const clean = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    expect((clean.detail.checks as Record<string, { count: number }>).I7?.count).toBe(0);
    // 의도적으로 틀어 둔다: 신원 행을 되살리고, 캐릭터를 살려 두고, 열린 요청인데 계정이 풀린 상태를 만든다
    await db().query("INSERT INTO auth_identities (account_id, provider, subject, secret_hash) VALUES ($1, 'dev', $2, 'x')", [u.accountId, `leak_${randomBytes(4).toString('hex')}`]);
    await db().query('UPDATE characters SET deleted_at = NULL WHERE account_id = $1', [u.accountId]);
    const w = await steamUser(app);
    expect((await requestWithdrawal(app, w)).status).toBe(201);
    await db().query('UPDATE accounts SET deleted_at = NULL WHERE id = $1', [w.accountId]);
    const bad = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    const i7 = (bad.detail.checks as Record<string, { count: number; samples: Record<string, unknown>[] }>).I7 as { count: number; samples: Record<string, unknown>[] };
    expect(i7.count).toBeGreaterThanOrEqual(3);
    expect(JSON.stringify(i7.samples)).toMatch(/auth_identities/);
    // 되돌려 둔다(다음 테스트가 전체 점검을 쓰지 않도록)
    await db().query("DELETE FROM auth_identities WHERE account_id = $1 AND provider = 'dev'", [u.accountId]);
    await db().query('UPDATE characters SET deleted_at = now() WHERE account_id = $1', [u.accountId]);
    await db().query('UPDATE accounts SET deleted_at = now() WHERE id = $1', [w.accountId]);
  });
});

describe('보류와 상한 (T-W22 ~ T-W24)', () => {
  const insertBan = (accountId: number, kind = 'ban', ends: string | null = "now() + interval '10 days'") =>
    db().query(
      `INSERT INTO account_sanctions (account_id, kind, source, reason_code, starts_at, ends_at, created_by) VALUES ($1, $2, 'admin', 'cheat', now() - interval '1 hour', ${ends ?? 'NULL'}, 'tester')`,
      [accountId, kind],
    );

  it('T-W22 활성 정지·채팅 금지·경제 정지·열린 신고·미결 결제 플래그 각각 익명화를 미루고, 풀리면 다음 실행에서 처리한다. 경고만이면 보류하지 않는다', async () => {
    const reporter = await steamUser(app);
    const cases: { reason: string; set: (u: WUser) => Promise<void>; clear: (u: WUser) => Promise<void> }[] = [
      { reason: 'sanction', set: (u) => insertBan(u.accountId).then(() => undefined), clear: async (u) => void (await db().query("UPDATE account_sanctions SET revoked_at = now(), revoked_by = 't' WHERE account_id = $1", [u.accountId])) },
      { reason: 'sanction', set: (u) => insertBan(u.accountId, 'chat_mute').then(() => undefined), clear: async (u) => void (await db().query("UPDATE account_sanctions SET revoked_at = now(), revoked_by = 't' WHERE account_id = $1", [u.accountId])) },
      {
        reason: 'economy_hold',
        set: async (u) => void (await db().query("INSERT INTO economy_holds (account_id, kind, state, evidence) VALUES ($1, 'manual', 'active', '{}'::jsonb)", [u.accountId])),
        clear: async (u) => void (await db().query("UPDATE economy_holds SET state = 'released', reviewed_at = now(), released_at = now() WHERE account_id = $1", [u.accountId])),
      },
      {
        reason: 'open_report',
        set: async (u) =>
          void (await db().query(
            "INSERT INTO reports (reporter_account_id, reporter_character_id, target_account_id, target_character_id, target_name, reason) VALUES ($1, $2, $3, $4, 'x', 'abuse')",
            [reporter.accountId, reporter.charId, u.accountId, u.charId],
          )),
        clear: async (u) => void (await db().query("UPDATE reports SET state = 'dismissed', handled_at = now(), handled_by = 't' WHERE target_account_id = $1", [u.accountId])),
      },
      {
        reason: 'payment_open',
        set: async (u) => void (await db().query("INSERT INTO payment_flags (account_id, kind, severity, detail) VALUES ($1, 'chargeback', 3, '{}'::jsonb)", [u.accountId])),
        clear: async (u) => void (await db().query("UPDATE payment_flags SET state = 'dismissed', reviewed_at = now(), reviewed_by = 't' WHERE account_id = $1", [u.accountId])),
      },
    ];
    for (const c of cases) {
      const u = await steamUser(app);
      expect((await requestWithdrawal(app, u)).status).toBe(201);
      await c.set(u);
      await makeDue(u.accountId);
      const r = await runJobOnce();
      expect(r.detail).toMatchObject({ deferred: 1, completed: 0 });
      const w = await openWithdrawal(u.accountId);
      expect(w.defer_reasons).toContain(c.reason);
      expect(w.defer_checked_at).not.toBeNull();
      expect((await accountRow(u.accountId)).anonymized_at).toBeNull();
      expect(await count('SELECT count(*) AS n FROM auth_identities WHERE account_id = $1', [u.accountId])).toBe(1);
      await c.clear(u);
      const r2 = await runJobOnce();
      expect(r2.detail).toMatchObject({ completed: 1 });
      expect(await state(u.accountId)).toBe('completed');
    }
    // 경고(warning)만 있으면 보류하지 않는다
    const w = await steamUser(app);
    expect((await requestWithdrawal(app, w)).status).toBe(201);
    await db().query("INSERT INTO account_sanctions (account_id, kind, source, reason_code, starts_at, created_by) VALUES ($1, 'warning', 'admin', 'abuse', now(), 't')", [w.accountId]);
    await makeDue(w.accountId);
    expect((await runJobOnce()).detail).toMatchObject({ completed: 1, deferred: 0 });
  });

  it('T-W23 보류 상한(180일)에 이르면 익명화하되 withdrawn_identities 가 생긴다. 원문 Steam ID 는 어디에도 없다', async () => {
    const u = await steamUser(app);
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    await insertBan(u.accountId, 'ban', null); // 영구 정지
    await db().query("UPDATE account_withdrawals SET requested_at = now() - interval '220 days', due_at = now() - interval '181 days' WHERE account_id = $1", [u.accountId]);
    const r = await runJobOnce();
    expect(r.detail).toMatchObject({ completed: 1, tombstones: 1 });
    const t = await db().query('SELECT * FROM withdrawn_identities WHERE source_account_id = $1', [u.accountId]);
    expect(t.rows).toHaveLength(1);
    const row = t.rows[0] as Record<string, unknown>;
    expect(row.identity_hash).toBe(identityHash(u.steamId as string));
    expect(row.identity_hash).not.toBe(u.steamId);
    expect(row.carry_ban_until).not.toBeNull();
    expect((row.carry_ban_until as Date).getUTCFullYear()).toBe(9999);
    // 만료는 이월 상한(3년)으로 끊긴다
    const created = (row.created_at as Date).getTime();
    expect((row.expires_at as Date).getTime()).toBeLessThanOrEqual(created + getConfig().withdraw.tombstoneMaxDays * 86_400_000 + 1000);
    expect(JSON.stringify((await db().query('SELECT * FROM withdrawn_identities')).rows)).not.toContain(u.steamId as string);
    // 상한 전에는 이월 표시가 없다: 보류가 풀린 평범한 탈퇴는 아무것도 남기지 않는다
    const clean = await steamUser(app);
    await withdrawAndRun(clean);
    expect(await count('SELECT count(*) AS n FROM withdrawn_identities WHERE source_account_id = $1', [clean.accountId])).toBe(0);
  });

  it('T-W24 운영자 보류(manual_hold)는 작업이 건너뛴다', async () => {
    const u = await steamUser(app);
    expect((await requestWithdrawal(app, u)).status).toBe(201);
    await db().query('UPDATE account_withdrawals SET manual_hold = true WHERE account_id = $1', [u.accountId]);
    await makeDue(u.accountId);
    const r = await runJobOnce();
    expect(r.detail).toMatchObject({ scanned: 0, completed: 0 });
    expect(await state(u.accountId)).toBe('requested');
    await db().query('UPDATE account_withdrawals SET manual_hold = false WHERE account_id = $1', [u.accountId]);
    expect((await runJobOnce()).detail).toMatchObject({ completed: 1 });
  });
});

describe('재가입과 이월 (T-W25, T-W26)', () => {
  it('T-W25 익명화 뒤 같은 Steam ID 로 로그인하면 완전히 새 계정이다(옛 캐릭터·재화 없음)', async () => {
    const u = await richUser();
    await withdrawAndRun(u);
    expect(await state(u.accountId)).toBe('completed');
    const again = await steamLoginApi(app, u.steamId as string);
    expect(again.status).toBe(201);
    expect(again.body.data.created).toBe(true);
    expect(again.body.data.account.id).not.toBe(u.accountUuid);
    const token = { access: again.body.data.access_token as string };
    const chars = await request(app).get('/characters').set(ver({ Authorization: `Bearer ${token.access}` }));
    expect(chars.body.data.characters).toEqual([]);
    const fresh = await db().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [again.body.data.account.id]);
    const freshId = Number((fresh.rows[0] as { id: string }).id);
    expect(await count('SELECT count(*) AS n FROM star_wallets WHERE account_id = $1', [freshId])).toBe(0);
    expect(await count('SELECT count(*) AS n FROM withdrawn_identities WHERE source_account_id = $1', [u.accountId])).toBe(0); // 이월할 사유가 없었다
    // 옛 계정 껍데기는 그대로(복구되지 않는다)
    expect((await accountRow(u.accountId)).anonymized_at).not.toBeNull();
    // 유예 중에는 새 계정이 안 생긴다
    const pendingUser = await steamUser(app);
    expect((await requestWithdrawal(app, pendingUser)).status).toBe(201);
    expect((await steamLoginApi(app, pendingUser.steamId as string)).status).toBe(403);
    expect(await count("SELECT count(*) AS n FROM auth_identities WHERE provider = 'steam' AND subject = $1", [pendingUser.steamId])).toBe(1);
  });

  it('T-W26 이월: 정지는 가입 거절, 결제·경제 정지는 새 계정에 걸린다. 해시는 원문과 다르고 키가 다르면 일치하지 않으며 만료·해제된 표시는 무시한다', async () => {
    // 정지 이월
    const banned = await steamUser(app);
    expect((await requestWithdrawal(app, banned)).status).toBe(201);
    await db().query("INSERT INTO account_sanctions (account_id, kind, source, reason_code, starts_at, ends_at, created_by) VALUES ($1, 'ban', 'admin', 'cheat', now() - interval '1 hour', now() + interval '30 days', 't')", [banned.accountId]);
    await db().query("UPDATE account_withdrawals SET requested_at = now() - interval '220 days', due_at = now() - interval '181 days' WHERE account_id = $1", [banned.accountId]);
    await runJobOnce();
    const t = (await db().query('SELECT * FROM withdrawn_identities WHERE source_account_id = $1', [banned.accountId])).rows[0] as Record<string, unknown>;
    expect(t).toBeDefined();
    const key = Buffer.from('another-key-another-key-another-key-0123456789');
    expect(identityHash(banned.steamId as string, key)).not.toBe(t.identity_hash);
    expect(identityHash(banned.steamId as string)).toBe(t.identity_hash);
    const rejected = await steamLoginApi(app, banned.steamId as string);
    expect(rejected.status).toBe(403);
    expect(rejected.body.errors.code).toBe('ACCOUNT_BANNED');
    expect(typeof rejected.body.errors.banned_until).toBe('string');
    expect(await count("SELECT count(*) AS n FROM auth_identities WHERE provider = 'steam' AND subject = $1", [banned.steamId])).toBe(0);
    // 정지가 이미 끝났으면(과거) 통과하고 다른 이월이 없으면 아무것도 걸리지 않는다
    await db().query("UPDATE withdrawn_identities SET carry_ban_until = now() - interval '1 hour' WHERE id = $1", [t.id]);
    const ok = await steamLoginApi(app, banned.steamId as string);
    expect(ok.status).toBe(201);
    const nid = Number(((await db().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [ok.body.data.account.id])).rows[0] as { id: string }).id);
    expect(await count('SELECT count(*) AS n FROM payment_profiles WHERE account_id = $1', [nid])).toBe(0);

    // 결제 정지 + 경제 정지 이월
    const pay = await steamUser(app);
    expect((await requestWithdrawal(app, pay)).status).toBe(201);
    await db().query("INSERT INTO payment_profiles (account_id, status, block_reason, blocked_at, blocked_by) VALUES ($1, 'blocked', 'refund_abuse', now(), 'system')", [pay.accountId]);
    await db().query("INSERT INTO economy_holds (account_id, kind, state, evidence) VALUES ($1, 'manual', 'active', '{}'::jsonb)", [pay.accountId]);
    await db().query("UPDATE account_withdrawals SET requested_at = now() - interval '220 days', due_at = now() - interval '181 days' WHERE account_id = $1", [pay.accountId]);
    await runJobOnce();
    const pt = (await db().query('SELECT * FROM withdrawn_identities WHERE source_account_id = $1', [pay.accountId])).rows[0] as Record<string, unknown>;
    expect(pt).toMatchObject({ carry_payment_block: 'refund_abuse', carry_econ_hold: true });
    expect(pt.carry_ban_until).toBeNull();
    const rejoin = await steamLoginApi(app, pay.steamId as string);
    expect(rejoin.status).toBe(201);
    const pid = Number(((await db().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [rejoin.body.data.account.id])).rows[0] as { id: string }).id);
    expect((await db().query('SELECT status, block_reason, blocked_by FROM payment_profiles WHERE account_id = $1', [pid])).rows[0]).toEqual({ status: 'blocked', block_reason: 'refund_abuse', blocked_by: 'system' });
    const hold = (await db().query('SELECT kind, state, evidence FROM economy_holds WHERE account_id = $1', [pid])).rows[0] as Record<string, unknown>;
    expect(hold).toMatchObject({ kind: 'manual', state: 'active', evidence: { source: 'withdrawn_identity' } });

    // 만료·해제 무시
    const exp = await steamUser(app);
    expect((await requestWithdrawal(app, exp)).status).toBe(201);
    await db().query("INSERT INTO payment_profiles (account_id, status, block_reason, blocked_at, blocked_by) VALUES ($1, 'blocked', 'manual', now(), 'system')", [exp.accountId]);
    await makeDue(exp.accountId);
    await db().query("UPDATE account_withdrawals SET due_at = now() - interval '181 days', requested_at = now() - interval '220 days' WHERE account_id = $1", [exp.accountId]);
    await runJobOnce();
    const et = (await db().query('SELECT id FROM withdrawn_identities WHERE source_account_id = $1', [exp.accountId])).rows[0] as { id: string };
    await db().query("UPDATE withdrawn_identities SET expires_at = now() - interval '1 minute' WHERE id = $1", [et.id]);
    const afterExpiry = await steamLoginApi(app, exp.steamId as string);
    expect(afterExpiry.status).toBe(201);
    expect(await count('SELECT count(*) AS n FROM payment_profiles WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1)', [afterExpiry.body.data.account.id])).toBe(0);
    // 만료된 표시는 purge-daily 가 지운다
    await purgeDaily(jobCtx);
    expect(await count('SELECT count(*) AS n FROM withdrawn_identities WHERE id = $1', [et.id])).toBe(0);
    // 해제된 표시도 무시: 정지 이월 -> 가입 거절 -> 운영자 해제 -> 가입 통과
    const rel = await steamUser(app);
    expect((await requestWithdrawal(app, rel)).status).toBe(201);
    await db().query("INSERT INTO account_sanctions (account_id, kind, source, reason_code, starts_at, ends_at, created_by) VALUES ($1, 'ban', 'admin', 'cheat', now() - interval '1 hour', now() + interval '30 days', 't')", [rel.accountId]);
    await db().query("UPDATE account_withdrawals SET requested_at = now() - interval '220 days', due_at = now() - interval '181 days' WHERE account_id = $1", [rel.accountId]);
    await runJobOnce();
    expect((await steamLoginApi(app, rel.steamId as string)).status).toBe(403);
    await db().query("UPDATE withdrawn_identities SET released_at = now(), released_by = 'owner' WHERE source_account_id = $1", [rel.accountId]);
    expect((await steamLoginApi(app, rel.steamId as string)).status).toBe(201);
  });
});

