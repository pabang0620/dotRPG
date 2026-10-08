// 5년 파기 `withdrawal-destroy`(설계 8절). 기본은 dry-run: 대상 계정 수와 표별 파기 예정 행 수만 기록하고 아무것도 지우지 않는다.
// WITHDRAW_DESTROY_ENABLED=true 일 때만 실삭제를 하고, 그때는 PURGE_DATABASE_URL(전용 DB 역할 dotrpg_purge)로만 지운다.
// 앱의 기본 풀은 그 역할이 아니라서 SQL 주입이 있어도 원장을 지울 수 없다(ledger_block_mutation 이 session_user 로 거절한다).
import { Client } from 'pg';
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { logger } from '../../utils/logger';
import { DESTROY_STEPS, bindIds } from './withdrawalPolicy';

export interface DestroyTarget {
  withdrawalId: number;
  accountId: number;
  accountUuid: string;
  requestedAt: Date;
  anonymizedAt: Date;
}

export interface DestroyReport {
  mode: 'dry_run' | 'scheduled';
  accounts: number;
  destroyed: number;
  shellKept: number;
  /** 표별 (예정 또는 실제) 행 수 합계 */
  tables: Record<string, number>;
  /** 외래 키로 막혀 남긴 표 이름(계정당) */
  blocked: string[];
}

/** 보관 기한이 지난 완료 건(기한순) */
export async function dueTargets(limit: number): Promise<DestroyTarget[]> {
  const r = await getPool().query<{ id: string; account_id: string; uuid: string; requested_at: Date; anonymized_at: Date }>(
    `SELECT w.id, w.account_id, a.uuid, w.requested_at, w.anonymized_at
       FROM account_withdrawals w JOIN accounts a ON a.id = w.account_id
      WHERE w.state = 'completed' AND w.retain_until <= now() ORDER BY w.retain_until, w.id LIMIT $1`,
    [limit],
  );
  return r.rows.map((x) => ({ withdrawalId: Number(x.id), accountId: Number(x.account_id), accountUuid: x.uuid, requestedAt: x.requested_at, anonymizedAt: x.anonymized_at }));
}

type Q = Pick<Client, 'query'>;

async function characterIds(db: Q, accountId: number): Promise<number[]> {
  const r = await db.query<{ id: string }>('SELECT id FROM characters WHERE account_id = $1 ORDER BY id', [accountId]);
  return r.rows.map((x) => Number(x.id));
}

/** dry-run: 표별 파기 예정 행 수(앱 풀, 읽기만) */
async function countPlan(t: DestroyTarget): Promise<Record<string, number>> {
  const db = getPool();
  const chars = await characterIds(db, t.accountId);
  const out: Record<string, number> = {};
  for (const s of DESTROY_STEPS) {
    const q = bindIds(`SELECT count(*) AS n FROM ${s.table} WHERE ${s.destroyWhere}`, [t.accountId], chars);
    const r = await db.query<{ n: string }>(q.text, q.params);
    const n = Number((r.rows[0] as { n: string }).n);
    if (n > 0) out[s.table] = n;
  }
  return out;
}

/** 한 계정을 실제로 지운다(자기 트랜잭션). 외래 키(23503)로 막힌 표는 SAVEPOINT 로 되돌리고 남긴다(껍데기 유지) */
async function destroyOne(purge: Client, t: DestroyTarget): Promise<{ tables: Record<string, number>; blocked: string[]; shellKept: boolean }> {
  const tables: Record<string, number> = {};
  const blocked: string[] = [];
  await purge.query('BEGIN');
  try {
    // 이 계정이 아직 파기 대상인지 다시 확인(그 사이 다른 작업이 처리했을 수 있다)
    const still = await purge.query("SELECT 1 FROM account_withdrawals WHERE id = $1 AND state = 'completed' AND retain_until <= now()", [t.withdrawalId]);
    if (still.rows.length === 0) {
      await purge.query('ROLLBACK');
      return { tables, blocked, shellKept: true };
    }
    const chars = await characterIds(purge, t.accountId);
    const guarded = async (name: string, sql: string, params: unknown[]): Promise<number | null> => {
      await purge.query('SAVEPOINT destroy_step');
      try {
        const r = await purge.query(sql, params);
        await purge.query('RELEASE SAVEPOINT destroy_step');
        return r.rowCount ?? 0;
      } catch (err) {
        await purge.query('ROLLBACK TO SAVEPOINT destroy_step');
        await purge.query('RELEASE SAVEPOINT destroy_step');
        if ((err as { code?: string }).code === '23503') {
          blocked.push(name);
          return null;
        }
        throw err;
      }
    };
    for (const s of DESTROY_STEPS) {
      const q = bindIds(`DELETE FROM ${s.table} WHERE ${s.destroyWhere}`, [t.accountId], chars);
      const n = await guarded(s.table, q.text, q.params);
      if (n !== null && n > 0) tables[s.table] = n;
    }
    // 껍데기: 캐릭터 -> 계정. 어떤 행도 참조하지 않을 때만 지운다
    const ch = await guarded('characters', 'DELETE FROM characters WHERE account_id = $1', [t.accountId]);
    if (ch !== null && ch > 0) tables.characters = ch;
    let shellKept = ch === null;
    if (!shellKept) {
      // 탈퇴 행 삭제와 계정 삭제는 한 묶음이다: 계정이 다른 행에 참조돼 못 지우면 탈퇴 행도 되살려 다음 실행에서 다시 시도한다
      await purge.query('SAVEPOINT destroy_shell');
      try {
        await purge.query('DELETE FROM account_withdrawals WHERE account_id = $1', [t.accountId]);
        const acc = await purge.query('DELETE FROM accounts WHERE id = $1', [t.accountId]);
        tables.accounts = acc.rowCount ?? 0;
        await purge.query('RELEASE SAVEPOINT destroy_shell');
      } catch (err) {
        await purge.query('ROLLBACK TO SAVEPOINT destroy_shell');
        await purge.query('RELEASE SAVEPOINT destroy_shell');
        if ((err as { code?: string }).code !== '23503') throw err;
        blocked.push('accounts');
        shellKept = true;
      }
    }
    // 관리대장(추가만, 같은 트랜잭션): 완전히 지웠거나, 껍데기를 남기더라도 실제로 지운 행이 있을 때만 한 줄
    const total = Object.values(tables).reduce((a, b) => a + b, 0);
    if (!shellKept || total > 0) {
      await purge.query(
        `INSERT INTO account_destruction_log (account_uuid, requested_at, anonymized_at, mode, tables) VALUES ($1, $2, $3, 'scheduled', $4::jsonb)`,
        [t.accountUuid, t.requestedAt, t.anonymizedAt, JSON.stringify({ ...tables, shell_kept: shellKept })],
      );
    }
    await purge.query('COMMIT');
    return { tables, blocked, shellKept };
  } catch (err) {
    await purge.query('ROLLBACK').catch(() => undefined);
    throw err;
  }
}

/** 작업 본체. shouldStop 은 계정 사이에서 확인한다 */
export async function runDestroy(shouldStop: () => boolean = () => false): Promise<DestroyReport> {
  const cfg = getConfig().withdraw;
  const targets = await dueTargets(cfg.jobBatch);
  const report: DestroyReport = { mode: cfg.destroyEnabled ? 'scheduled' : 'dry_run', accounts: targets.length, destroyed: 0, shellKept: 0, tables: {}, blocked: [] };
  if (targets.length === 0) return report;
  const add = (src: Record<string, number>): void => {
    for (const [k, v] of Object.entries(src)) report.tables[k] = (report.tables[k] ?? 0) + v;
  };
  if (!cfg.destroyEnabled || !cfg.purgeDatabaseUrl) {
    for (const t of targets) {
      if (shouldStop()) break;
      add(await countPlan(t));
    }
    return report;
  }
  const purge = new Client({ connectionString: cfg.purgeDatabaseUrl });
  await purge.connect();
  try {
    for (const t of targets) {
      if (shouldStop()) break;
      try {
        const r = await destroyOne(purge, t);
        add(r.tables);
        report.blocked.push(...r.blocked);
        if (r.shellKept) report.shellKept++;
        else report.destroyed++;
      } catch (err) {
        logger.error({ err, withdrawalId: t.withdrawalId }, 'withdrawal.destroy.failed');
        throw err;
      }
    }
  } finally {
    await purge.end().catch(() => undefined);
  }
  return report;
}
