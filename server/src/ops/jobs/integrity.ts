// 정합성 점검 `integrity-nightly`(phase7_ops.md 8.4). 읽기 전용: 고치는 쿼리를 자동으로 실행하지 않는다.
// 평소에는 "지난 25시간 안에 원장 변동이 있던 캐릭터"만, 일요일(KST)과 수동 full 실행에는 전체 캐릭터를 본다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getNow } from '../../utils/clock';
import type { JobCtx, JobResult } from '../jobRunner';

const SAMPLE_MAX = 20;

interface CheckResult {
  count: number;
  samples: Record<string, unknown>[];
}

const KST_MS = 9 * 3_600_000;
export const isKstSunday = (now: Date): boolean => new Date(now.getTime() + KST_MS).getUTCDay() === 0;

const scopeOf = (table: 'gold_ledger' | 'item_ledger', full: boolean): string =>
  full
    ? 'SELECT id FROM characters'
    : `SELECT DISTINCT character_id FROM ${table} WHERE created_at >= now() - interval '25 hours'`;

async function rows(sql: string): Promise<CheckResult> {
  const r = await getPool().query<Record<string, unknown>>(`${sql}`);
  return { count: r.rows.length, samples: r.rows.slice(0, SAMPLE_MAX) };
}

/** I1: characters.gold = 원장 마지막 balance_after = SUM(delta), 그리고 원장 체인 연속 */
async function i1(full: boolean): Promise<CheckResult> {
  const scope = scopeOf('gold_ledger', full);
  const bal = await rows(
    `SELECT c.uuid AS character, c.gold AS actual, coalesce(l.s, 0) AS ledger_sum, l.last_bal AS ledger_last
       FROM characters c
       LEFT JOIN (SELECT character_id, sum(delta) AS s, (array_agg(balance_after ORDER BY id DESC))[1] AS last_bal
                    FROM gold_ledger GROUP BY character_id) l ON l.character_id = c.id
      WHERE c.id IN (${scope}) AND (c.gold <> coalesce(l.s, 0) OR c.gold <> coalesce(l.last_bal, 0))
      ORDER BY c.id`,
  );
  const chain = await rows(
    `SELECT (SELECT uuid FROM characters WHERE id = t.character_id) AS character, t.id AS ledger_id,
            t.balance_after - t.delta AS expected_prev, t.prev
       FROM (SELECT id, character_id, delta, balance_after,
                    lag(balance_after) OVER (PARTITION BY character_id ORDER BY id) AS prev
               FROM gold_ledger WHERE character_id IN (${scope})) t
      WHERE t.prev IS NOT NULL AND t.balance_after - t.delta <> t.prev
      ORDER BY t.id`,
  );
  return { count: bal.count + chain.count, samples: [...bal.samples, ...chain.samples].slice(0, SAMPLE_MAX) };
}

/** I2: (캐릭터, 위치, 아이템)별 원장 합 = 소지품 수량(bag, storage, worn) */
async function i2(full: boolean): Promise<CheckResult> {
  const scope = scopeOf('item_ledger', full);
  return rows(
    `SELECT (SELECT uuid FROM characters WHERE id = x.character_id) AS character, x.location, x.item_key,
            x.ledger, x.held
       FROM (SELECT coalesce(l.character_id, h.character_id) AS character_id, coalesce(l.location, h.location) AS location,
                    coalesce(l.item_key, h.item_key) AS item_key, coalesce(l.s, 0) AS ledger, coalesce(h.s, 0) AS held
               FROM (SELECT character_id, location, item_key, sum(delta) AS s FROM item_ledger
                      WHERE location IN ('bag', 'storage', 'worn') AND character_id IN (${scope})
                      GROUP BY 1, 2, 3) l
               FULL JOIN (SELECT character_id, location, item_key, sum(count) AS s FROM character_items
                           WHERE character_id IN (${scope}) GROUP BY 1, 2, 3) h
                 ON h.character_id = l.character_id AND h.location = l.location AND h.item_key = l.item_key) x
      WHERE x.ledger <> x.held
      ORDER BY x.character_id`,
  );
}

const num = async (sql: string): Promise<number> =>
  Number(((await getPool().query<{ v: string }>(sql)).rows[0] as { v: string }).v);

/** I3: phase6 9.3 골드 보존식(+ 운영 지급 system 우편 = admin_grants 합)과 아이템 보존식 */
async function i3(): Promise<CheckResult> {
  const out = await num(
    "SELECT coalesce(sum(-delta), 0) AS v FROM gold_ledger WHERE reason IN ('auction_deposit', 'auction_bid', 'auction_buyout')",
  );
  const back = await num("SELECT coalesce(sum(delta), 0) AS v FROM gold_ledger WHERE reason = 'mail_claim'");
  const system = await num("SELECT coalesce(sum(gold), 0) AS v FROM mails WHERE kind = 'system'");
  const grants = await num('SELECT coalesce(sum(gold), 0) AS v FROM admin_grants');
  const deposits = await num("SELECT coalesce(sum(deposit), 0) AS v FROM auction_listings WHERE status = 'active'");
  const bids = await num("SELECT coalesce(sum(current_bid), 0) AS v FROM auction_listings WHERE status = 'active'");
  const inMail = await num('SELECT coalesce(sum(gold), 0) AS v FROM mails WHERE claimed_at IS NULL AND expired_at IS NULL');
  const sinks = await num('SELECT coalesce(sum(amount), 0) AS v FROM auction_sinks');
  const samples: Record<string, unknown>[] = [];
  const left = out - back + system;
  const right = deposits + bids + inMail + sinks;
  if (left !== right) samples.push({ kind: 'gold_conservation', left, right, out, back, system, deposits, bids, in_mail: inMail, sinks });
  if (system !== grants) samples.push({ kind: 'system_mail_vs_admin_grants', system_mail_gold: system, admin_grants_gold: grants });
  const items = await getPool().query<{ item_key: string; loc: string; led: string; held: string }>(
    `SELECT k.item_key, k.loc, coalesce(l.s, 0) AS led, coalesce(h.s, 0) AS held FROM (
       SELECT item_key, location AS loc FROM item_ledger WHERE location IN ('auction', 'mail')
       UNION SELECT item_key, 'auction' FROM auction_listings WHERE status = 'active'
       UNION SELECT item_key, 'mail' FROM mails WHERE item_key IS NOT NULL) k
     LEFT JOIN (SELECT item_key, location AS loc, sum(delta) AS s FROM item_ledger WHERE location IN ('auction', 'mail')
                GROUP BY 1, 2) l ON l.item_key = k.item_key AND l.loc = k.loc
     LEFT JOIN (
       SELECT item_key, 'auction' AS loc, sum(count) AS s FROM auction_listings WHERE status = 'active' GROUP BY 1
       UNION ALL
       SELECT item_key, 'mail', sum(count) FROM mails
        WHERE item_key IS NOT NULL AND claimed_at IS NULL AND expired_at IS NULL GROUP BY 1) h
       ON h.item_key = k.item_key AND h.loc = k.loc`,
  );
  for (const r of items.rows) {
    if (Number(r.led) !== Number(r.held) && samples.length < SAMPLE_MAX) {
      samples.push({ kind: 'item_conservation', location: r.loc, item_key: r.item_key, ledger: Number(r.led), held: Number(r.held) });
    }
  }
  const itemBad = items.rows.filter((r) => Number(r.led) !== Number(r.held)).length;
  const count = (left !== right ? 1 : 0) + (system !== grants ? 1 : 0) + itemBad;
  return { count, samples };
}

/** I4: 막힌 상태(틱·정리 실패의 흔적) */
async function i4(): Promise<CheckResult> {
  const pol = getConfig().policy;
  const now = getNow();
  const samples: Record<string, unknown>[] = [];
  let count = 0;
  const add = (kind: string, n: number, extra: Record<string, unknown> = {}): void => {
    if (n > 0) {
      count += n;
      samples.push({ kind, count: n, ...extra });
    }
  };
  add(
    'auction_overdue',
    await num(`SELECT count(*) AS v FROM auction_listings WHERE status = 'active' AND ends_at < now() - interval '10 minutes'`),
  );
  add(
    'mail_overdue',
    await num(
      `SELECT count(*) AS v FROM mails WHERE claimed_at IS NULL AND expired_at IS NULL AND expires_at < now() - interval '1 day'`,
    ),
  );
  const staleAt = new Date(now.getTime() - 2 * pol.runStaleSeconds * 1000).toISOString();
  add('run_stale', await num(`SELECT count(*) AS v FROM dungeon_runs WHERE state = 'playing' AND started_at < '${staleAt}'`));
  add(
    'party_run_stuck',
    await num(
      `SELECT count(*) AS v FROM party_runs WHERE state IN ('gathering', 'playing') AND created_at < now() - interval '2 hours'`,
    ),
  );
  return { count, samples };
}

/** I5: 정지 일관성 */
async function i5(): Promise<CheckResult> {
  return rows(
    `SELECT a.uuid AS account, a.banned_until,
            (SELECT max(ends_at) FROM account_sanctions s
              WHERE s.account_id = a.id AND s.kind = 'ban' AND s.revoked_at IS NULL AND s.ends_at > now()) AS sanction_until
       FROM accounts a
      WHERE coalesce(a.banned_until, 'epoch') IS DISTINCT FROM
            coalesce((SELECT max(ends_at) FROM account_sanctions s
                       WHERE s.account_id = a.id AND s.kind = 'ban' AND s.revoked_at IS NULL AND s.ends_at > now()), 'epoch')
        AND NOT (a.banned_until IS NOT NULL AND a.banned_until <= now()
                 AND NOT EXISTS (SELECT 1 FROM account_sanctions s
                                  WHERE s.account_id = a.id AND s.kind = 'ban' AND s.revoked_at IS NULL AND s.ends_at > now()))
      ORDER BY a.id`,
  );
}

export async function integrityJob(ctx: JobCtx): Promise<JobResult> {
  const full = ctx.opts.full === true || isKstSunday(getNow());
  const checks: Record<string, CheckResult> = {
    I1: await i1(full),
    I2: await i2(full),
    I3: await i3(),
    I4: await i4(),
    I5: await i5(),
  };
  const mismatches = Object.values(checks).reduce((a, c) => a + c.count, 0);
  return { rows: 0, detail: { scope: full ? 'full' : 'recent', mismatches, checks } };
}
