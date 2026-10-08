// 익명화(T1, 설계 7절): 유예가 끝난 탈퇴 요청의 개인 식별 정보를 지우고 계정을 "껍데기"로 남긴다. 되돌릴 수 없다.
// 계정마다 자기 트랜잭션이고 모든 단계가 멱등이다. 잔액·재고·원장은 건드리지 않는다(원장 보존식 I1~I3, I6이 그대로 성립).
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getPool, withTransaction } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { placeholderOf } from './withdrawalCleanup';
import { FOREVER, identityHash, upsertTombstone, type Carry, type PaymentBlock } from './withdrawalIdentity';
import * as repo from './withdrawalRepository';
import { T1_STEPS, bindIds } from './withdrawalPolicy';

export interface AnonymizeOutcome {
  status: 'completed' | 'deferred' | 'skipped';
  reasons: repo.DeferReason[];
  tables: Record<string, number>;
  /** 보류 상한(또는 운영자 강제)으로 보류를 풀고 진행했는가 */
  forced: boolean;
  tombstone: boolean;
}

export interface AnonymizeOpts {
  now: Date;
  /** job: 기한이 지났고 운영자 보류가 아닌 요청만. admin: WD5 즉시 익명화(기한 무시) */
  mode: 'job' | 'admin';
  /** WD5 override_deferral: 보류 사유가 있어도 진행 */
  override?: boolean;
}

/** 보류 사유 계산(7.2의 2번). 제재·경제 정지·열린 신고·결제 미결. 경고(warning)만 있으면 보류하지 않는다 */
export async function computeDeferReasons(db: Pick<PoolClient, 'query'>, accountId: number, now: Date, manualHold: boolean): Promise<repo.DeferReason[]> {
  const out: repo.DeferReason[] = [];
  const has = async (sql: string, params: unknown[]): Promise<boolean> => (await db.query(sql, params)).rows.length > 0;
  if (
    await has(
      `SELECT 1 FROM account_sanctions WHERE account_id = $1 AND revoked_at IS NULL AND (ends_at IS NULL OR ends_at > $2) AND kind IN ('ban', 'chat_mute')
       UNION ALL SELECT 1 FROM accounts WHERE id = $1 AND banned_until > $2 LIMIT 1`,
      [accountId, now],
    )
  ) {
    out.push('sanction');
  }
  if (await has("SELECT 1 FROM economy_holds WHERE account_id = $1 AND state IN ('active', 'clawed_back') LIMIT 1", [accountId])) out.push('economy_hold');
  if (await has("SELECT 1 FROM reports WHERE target_account_id = $1 AND state IN ('open', 'reviewing') LIMIT 1", [accountId])) out.push('open_report');
  if (
    await has(
      `SELECT 1 FROM star_orders WHERE account_id = $1 AND state IN ('pending_init', 'created', 'authorized', 'finalized')
       UNION ALL SELECT 1 FROM payment_flags WHERE account_id = $1 AND state = 'open' AND severity >= 2 LIMIT 1`,
      [accountId],
    )
  ) {
    out.push('payment_open');
  }
  if (manualHold) out.push('manual');
  return out;
}

/** 이월할 사유(9.3): 활성 정지, 결제 정지·부채, 활성 경제 정지, 그리고 재가입 대기(쿨다운) */
async function computeCarry(client: PoolClient, accountId: number, now: Date): Promise<Carry | null> {
  const cfg = getConfig().withdraw;
  const ban = await client.query<{ ends: Date | null }>(
    `SELECT ends_at AS ends FROM account_sanctions WHERE account_id = $1 AND kind = 'ban' AND revoked_at IS NULL AND (ends_at IS NULL OR ends_at > $2)`,
    [accountId, now],
  );
  let banUntil: Date | null = null;
  for (const b of ban.rows) {
    const end = b.ends ?? FOREVER;
    if (!banUntil || end.getTime() > banUntil.getTime()) banUntil = end;
  }
  const acct = await client.query<{ banned_until: Date | null }>('SELECT banned_until FROM accounts WHERE id = $1', [accountId]);
  const bu = acct.rows[0]?.banned_until ?? null;
  if (bu && bu.getTime() > now.getTime() && (!banUntil || bu.getTime() > banUntil.getTime())) banUntil = bu;
  if (cfg.rejoinCooldownDays > 0) {
    const until = new Date(now.getTime() + cfg.rejoinCooldownDays * 86_400_000);
    if (!banUntil || until.getTime() > banUntil.getTime()) banUntil = until;
  }
  const prof = await client.query<{ block_reason: PaymentBlock }>("SELECT block_reason FROM payment_profiles WHERE account_id = $1 AND status = 'blocked'", [accountId]);
  const debt = await client.query<{ debt: string }>('SELECT debt FROM star_wallets WHERE account_id = $1 AND debt > 0', [accountId]);
  const paymentBlock: PaymentBlock | null = prof.rows[0]?.block_reason ?? (debt.rows.length > 0 ? 'chargeback' : null);
  const hold = await client.query("SELECT 1 FROM economy_holds WHERE account_id = $1 AND state IN ('active', 'clawed_back') LIMIT 1", [accountId]);
  const econHold = hold.rows.length > 0;
  if (!banUntil && !paymentBlock && !econHold) return null;
  return { banUntil, paymentBlock, econHold };
}

/** 결제·게임 내 원장의 마지막 시각 + 보관 연수. 둘 다 없으면 익명화 + 1일 (설계 2절) */
async function computeRetainUntil(client: PoolClient, accountId: number, charIds: number[], anonymizedAt: Date): Promise<Date> {
  const cfg = getConfig().withdraw;
  const r = await client.query<{ t: Date }>(
    `SELECT GREATEST(
        (SELECT max(t) FROM (SELECT max(created_at) AS t FROM star_orders WHERE account_id = $1
                             UNION ALL SELECT max(reversed_at) FROM star_orders WHERE account_id = $1
                             UNION ALL SELECT max(created_at) FROM star_ledger WHERE account_id = $1) x) + make_interval(years => $3::int),
        (SELECT max(t) FROM (SELECT max(created_at) AS t FROM gold_ledger WHERE character_id = ANY($2::bigint[])
                             UNION ALL SELECT max(created_at) FROM item_ledger WHERE character_id = ANY($2::bigint[])
                             UNION ALL SELECT max(created_at) FROM xp_ledger WHERE character_id = ANY($2::bigint[])
                             UNION ALL SELECT max(created_at) FROM enhance_log WHERE character_id = ANY($2::bigint[])
                             UNION ALL SELECT max(created_at) FROM sweep_ticket_ledger WHERE account_id = $1) y) + make_interval(years => $4::int),
        $5::timestamptz + interval '1 day') AS t`,
    [accountId, charIds, cfg.retainPaidYears, cfg.retainLedgerYears, anonymizedAt],
  );
  return (r.rows[0] as { t: Date }).t;
}

/** 요청 한 건을 익명화한다. 호출 쪽이 withTransaction 안에서 부른다(락 순서: 캐릭터 -> 계정 -> 요청 행) */
export async function anonymizeOne(client: PoolClient, withdrawalId: number, o: AnonymizeOpts): Promise<AnonymizeOutcome> {
  const empty = (status: AnonymizeOutcome['status'], reasons: repo.DeferReason[] = []): AnonymizeOutcome => ({ status, reasons, tables: {}, forced: false, tombstone: false });
  const head = await client.query<{ account_id: string }>('SELECT account_id FROM account_withdrawals WHERE id = $1', [withdrawalId]);
  if (!head.rows[0]) return empty('skipped');
  const accountId = Number(head.rows[0].account_id);
  const chars = await repo.lockAllCharacters(client, accountId);
  const acc = await repo.lockAccount(client, accountId);
  const w = await repo.lockById(client, withdrawalId);
  // 그 사이 철회됐거나 이미 처리됐으면 건너뛴다
  if (!acc || !w || w.state !== 'requested' || acc.anonymized_at || !acc.deleted_at) return empty('skipped');
  if (o.mode === 'job' && (w.manual_hold || w.due_at.getTime() > o.now.getTime())) return empty('skipped');

  const cfg = getConfig().withdraw;
  const reasons = await computeDeferReasons(client, accountId, o.now, w.manual_hold);
  const capReached = o.now.getTime() >= w.due_at.getTime() + cfg.deferMaxDays * 86_400_000;
  const overridden = o.override === true;
  if (reasons.length > 0 && !overridden && !(capReached && !w.manual_hold)) {
    await repo.setDefer(client, w.id, reasons, o.now);
    return empty('deferred', reasons);
  }
  await repo.setDefer(client, w.id, reasons, o.now);

  const ids = chars.map((c) => c.id);
  // 1. 캐릭터: 모두 삭제 표시 + 자리표시 이름(이미 삭제된 것의 원래 이름도 가린다). 이름 스냅샷 익명화보다 먼저
  await client.query('UPDATE characters SET deleted_at = coalesce(deleted_at, $2) WHERE account_id = $1', [accountId, o.now]);
  for (const c of chars) {
    const ph = placeholderOf(c.uuid, 0);
    if (c.name !== ph && !/^탈퇴[0-9a-f]{6}$/.test(c.name)) await client.query('UPDATE characters SET name = $2 WHERE id = $1', [c.id, ph]);
  }
  // 2. 이월 표시(Steam 신원이 있고 이월할 사유가 있을 때만. 만들 필요가 없으면 아무것도 남기지 않는다)
  let tombstone = false;
  const steam = await repo.steamSubjectOf(client, accountId);
  if (steam) {
    const carry = await computeCarry(client, accountId, o.now);
    if (carry) {
      await upsertTombstone(client, identityHash(steam), accountId, carry, o.now);
      tombstone = true;
    }
  }
  // 3. 지우기·익명화(정책 레지스트리 T1 문장)
  const tables: Record<string, number> = {};
  for (const step of T1_STEPS) {
    const q = bindIds(step.sql, [accountId], ids);
    const r = await client.query(q.text, q.params);
    tables[step.table] = (tables[step.table] ?? 0) + (r.rowCount ?? 0);
  }
  // 4. 계정 껍데기
  await client.query(
    `UPDATE accounts SET anonymized_at = $2, last_login_at = NULL, prev_login_at = NULL, last_character_id = NULL,
            active_family_id = NULL, active_install_id = NULL, active_device_hash = NULL, active_session_at = NULL
      WHERE id = $1`,
    [accountId, o.now],
  );
  // 5. 요청 행 완료 + 보관 기한
  const retainUntil = await computeRetainUntil(client, accountId, ids, o.now);
  await repo.markCompleted(client, w.id, o.now, retainUntil);
  return { status: 'completed', reasons, tables, forced: reasons.length > 0, tombstone };
}

// ---------- 작업 withdrawal-anonymize ----------

export interface AnonymizeBatch {
  scanned: number;
  completed: number;
  deferred: number;
  skipped: number;
  failed: number;
  tombstones: number;
  tables: Record<string, number>;
  /** 연속 3회 이상 실패한 요청(경보 대상) */
  stuck: number[];
}

const failures = new Map<number, number>();
export const STUCK_AFTER = 3;

/** 기한이 지난 요청을 최대 WITHDRAW_JOB_BATCH 건 처리한다. 오래 보류된 건이 뒤로 가도록 마지막 점검이 오래된 순으로 읽는다 */
export async function runAnonymizeBatch(shouldStop: () => boolean = () => false): Promise<AnonymizeBatch> {
  const cfg = getConfig().withdraw;
  const now = getNow();
  const out: AnonymizeBatch = { scanned: 0, completed: 0, deferred: 0, skipped: 0, failed: 0, tombstones: 0, tables: {}, stuck: [] };
  const due = await getPool().query<{ id: string }>(
    `SELECT id FROM account_withdrawals WHERE state = 'requested' AND NOT manual_hold AND due_at <= $1
      ORDER BY coalesce(defer_checked_at, 'epoch'::timestamptz), due_at LIMIT $2`,
    [now, cfg.jobBatch],
  );
  for (const row of due.rows) {
    if (shouldStop()) break;
    const id = Number(row.id);
    out.scanned++;
    try {
      const r = await withTransaction((client) => anonymizeOne(client, id, { now, mode: 'job' }));
      failures.delete(id);
      if (r.status === 'completed') out.completed++;
      else if (r.status === 'deferred') out.deferred++;
      else out.skipped++;
      if (r.tombstone) out.tombstones++;
      for (const [t, n] of Object.entries(r.tables)) out.tables[t] = (out.tables[t] ?? 0) + n;
    } catch (err) {
      // 계정 하나의 오류는 그 트랜잭션만 롤백하고 다음 계정으로 간다("반쯤 익명화된" 계정이 생기지 않는다)
      out.failed++;
      const n = (failures.get(id) ?? 0) + 1;
      failures.set(id, n);
      if (n >= STUCK_AFTER) out.stuck.push(id);
      logger.error({ err, withdrawalId: id, attempts: n }, 'withdrawal.anonymize.failed');
    }
  }
  return out;
}
