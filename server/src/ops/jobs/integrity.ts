// 정합성 점검 `integrity-nightly`(phase7_ops.md 8.4). 읽기 전용: 고치는 쿼리를 자동으로 실행하지 않는다.
// 평소에는 "지난 25시간 안에 원장 변동이 있던 캐릭터"만, 일요일(KST)과 수동 full 실행에는 전체 캐릭터를 본다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getNow } from '../../utils/clock';
import { reconcileIncome } from '../../domains/antiabuse/incomeReconcile';
import { forbiddenReason } from '../../domains/antiabuse/reservedNames';
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
  // 10단계: 캠페인 우편의 골드는 mails.gold가 아니라 첨부 표에 있다(mails.gold는 0). 수령하면 같은 mail_claim 원장으로 들어온다
  const campaignGold = await num("SELECT coalesce(sum(amount), 0) AS v FROM mail_attachments WHERE kind = 'gold'");
  const campaignGoldOpen = await num(
    `SELECT coalesce(sum(a.amount), 0) AS v FROM mail_attachments a JOIN mails m ON m.id = a.mail_id
      WHERE a.kind = 'gold' AND m.claimed_at IS NULL AND m.expired_at IS NULL`,
  );
  const grants = await num('SELECT coalesce(sum(gold), 0) AS v FROM admin_grants');
  const deposits = await num("SELECT coalesce(sum(deposit), 0) AS v FROM auction_listings WHERE status = 'active'");
  const bids = await num("SELECT coalesce(sum(current_bid), 0) AS v FROM auction_listings WHERE status = 'active'");
  const inMail = (await num('SELECT coalesce(sum(gold), 0) AS v FROM mails WHERE claimed_at IS NULL AND expired_at IS NULL')) + campaignGoldOpen;
  const sinks = await num('SELECT coalesce(sum(amount), 0) AS v FROM auction_sinks');
  const samples: Record<string, unknown>[] = [];
  const left = out - back + system + campaignGold;
  const right = deposits + bids + inMail + sinks;
  if (left !== right) samples.push({ kind: 'gold_conservation', left, right, out, back, system, campaign_gold: campaignGold, deposits, bids, in_mail: inMail, sinks });
  if (system !== grants) samples.push({ kind: 'system_mail_vs_admin_grants', system_mail_gold: system, admin_grants_gold: grants });
  const items = await getPool().query<{ item_key: string; loc: string; led: string; held: string }>(
    `SELECT k.item_key, k.loc, coalesce(l.s, 0) AS led, coalesce(h.s, 0) AS held FROM (
       SELECT item_key, location AS loc FROM item_ledger WHERE location IN ('auction', 'mail')
       UNION SELECT item_key, 'auction' FROM auction_listings WHERE status = 'active'
       UNION SELECT item_key, 'mail' FROM mails WHERE item_key IS NOT NULL
       UNION SELECT item_key, 'mail' FROM mail_attachments WHERE kind = 'item') k
     LEFT JOIN (SELECT item_key, location AS loc, sum(delta) AS s FROM item_ledger WHERE location IN ('auction', 'mail')
                GROUP BY 1, 2) l ON l.item_key = k.item_key AND l.loc = k.loc
     LEFT JOIN (
       SELECT item_key, loc, sum(s) AS s FROM (
         SELECT item_key, 'auction' AS loc, sum(count) AS s FROM auction_listings WHERE status = 'active' GROUP BY 1
         UNION ALL
         SELECT item_key, 'mail', sum(count) FROM mails
          WHERE item_key IS NOT NULL AND claimed_at IS NULL AND expired_at IS NULL GROUP BY 1
         UNION ALL
         SELECT a.item_key, 'mail', sum(a.amount) FROM mail_attachments a JOIN mails m ON m.id = a.mail_id
          WHERE a.kind = 'item' AND m.claimed_at IS NULL AND m.expired_at IS NULL GROUP BY 1) u
       GROUP BY 1, 2) h
       ON h.item_key = k.item_key AND h.loc = k.loc`,
  );
  for (const r of items.rows) {
    if (Number(r.led) !== Number(r.held) && samples.length < SAMPLE_MAX) {
      samples.push({ kind: 'item_conservation', location: r.loc, item_key: r.item_key, ledger: Number(r.led), held: Number(r.held) });
    }
  }
  const itemBad = items.rows.filter((r) => Number(r.led) !== Number(r.held)).length;
  // 10단계: 클리어권 로트별 SUM(원장 delta) = remaining (만료 작업이 아직 못 돈 기한 지난 로트는 제외)
  const lots = await getPool().query<{ id: string; remaining: number; s: string }>(
    `SELECT l.id, l.remaining, coalesce(sum(e.delta), 0) AS s FROM sweep_ticket_lots l
       LEFT JOIN sweep_ticket_ledger e ON e.lot_id = l.id
      WHERE NOT (l.kind = 'event' AND l.expires_at <= now() AND l.remaining > 0)
      GROUP BY l.id, l.remaining HAVING l.remaining <> coalesce(sum(e.delta), 0)`,
  );
  for (const r of lots.rows) {
    if (samples.length < SAMPLE_MAX) samples.push({ kind: 'sweep_ticket_conservation', lot: Number(r.id), remaining: r.remaining, ledger: Number(r.s) });
  }
  const count = (left !== right ? 1 : 0) + (system !== grants ? 1 : 0) + itemBad + lots.rows.length;
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
  // 8단계: 하트비트가 2시간 넘게 없는 활성 필드 세션, 활성 멤버가 없는 활성 세션
  add('field_session_stuck', await num(`SELECT count(*) AS v FROM field_sessions WHERE state = 'active' AND last_active_at < now() - interval '2 hours'`));
  add(
    'field_session_empty',
    await num(`SELECT count(*) AS v FROM field_sessions s WHERE s.state = 'active'
                  AND NOT EXISTS (SELECT 1 FROM field_session_members m WHERE m.session_id = s.id AND m.state <> 'left')`),
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

/** I6: 별조각 결제·지갑(11단계 12.3). 불일치는 critical. 열 이름이 아니라 종류(kind)별로 샘플을 남긴다 */
async function i6(): Promise<CheckResult> {
  const parts: [string, CheckResult][] = [];
  const add = async (kind: string, sql: string): Promise<void> => {
    const r = await rows(sql);
    if (r.count > 0) parts.push([kind, { count: r.count, samples: r.samples.map((x) => ({ kind, ...x })) }]);
  };
  // I6-1 지갑: balance = SUM(delta) = 마지막 balance_after, paid_balance = SUM(paid_delta) = SUM(로트 remaining), debt = SUM(debt_delta)
  await add(
    'wallet',
    `SELECT a.uuid AS account, w.balance, coalesce(l.s, 0) AS ledger_sum, l.last_bal AS ledger_last, w.paid_balance, coalesce(l.ps, 0) AS paid_sum,
            coalesce(lots.r, 0) AS lots_remaining, w.debt, coalesce(l.ds, 0) AS debt_sum
       FROM star_wallets w JOIN accounts a ON a.id = w.account_id
       LEFT JOIN (SELECT account_id, sum(delta) AS s, sum(paid_delta) AS ps, sum(debt_delta) AS ds, (array_agg(balance_after ORDER BY id DESC))[1] AS last_bal
                    FROM star_ledger GROUP BY account_id) l ON l.account_id = w.account_id
       LEFT JOIN (SELECT account_id, sum(remaining) AS r FROM star_paid_lots GROUP BY account_id) lots ON lots.account_id = w.account_id
      WHERE w.balance <> coalesce(l.s, 0) OR w.balance <> coalesce(l.last_bal, 0) OR w.paid_balance <> coalesce(l.ps, 0)
         OR w.paid_balance <> coalesce(lots.r, 0) OR w.debt <> coalesce(l.ds, 0)
      ORDER BY w.account_id`,
  );
  // I6-2 주문당 1회: 지급된 주문마다 purchase 원장 정확히 1줄(delta = stars), 회수된 주문마다 회수 원장 정확히 1줄. 주문 없는 purchase 원장은 0줄
  await add(
    'order_ledger',
    `SELECT o.uuid AS "order", o.state, o.stars,
            (SELECT count(*) FROM star_ledger l WHERE l.reason = 'purchase' AND l.ref = o.uuid::text) AS purchases,
            (SELECT coalesce(sum(delta), 0) FROM star_ledger l WHERE l.reason = 'purchase' AND l.ref = o.uuid::text) AS purchased
       FROM star_orders o
      WHERE o.granted_at IS NOT NULL
        AND ((SELECT count(*) FROM star_ledger l WHERE l.reason = 'purchase' AND l.ref = o.uuid::text) <> 1
          OR (SELECT coalesce(sum(delta), 0) FROM star_ledger l WHERE l.reason = 'purchase' AND l.ref = o.uuid::text) <> o.stars
          OR (o.state IN ('refunded', 'chargeback')
              AND (SELECT count(*) FROM star_ledger l WHERE l.reason IN ('refund_revoke', 'chargeback_revoke') AND l.ref = o.uuid::text) <> 1))
      ORDER BY o.id`,
  );
  await add(
    'orphan_purchase',
    `SELECT l.id AS ledger_id, l.ref FROM star_ledger l WHERE l.reason = 'purchase' AND NOT EXISTS (SELECT 1 FROM star_orders o WHERE o.uuid::text = l.ref) ORDER BY l.id`,
  );
  // I6-3 Steam 확정 금액 = 주문 스냅샷 금액(과거 가격은 주문 행이 정본이다)
  await add(
    'steam_amount',
    `SELECT o.uuid AS "order", o.amount_minor, o.steam_amount_minor, o.currency, o.steam_currency FROM star_orders o
      WHERE o.granted_at IS NOT NULL AND (o.steam_amount_minor IS DISTINCT FROM o.amount_minor OR o.steam_currency IS DISTINCT FROM o.currency) ORDER BY o.id`,
  );
  // I6-4 배분: 유료분 감소 원장 줄마다 -paid_delta = SUM(배분)
  await add(
    'alloc',
    `SELECT l.id AS ledger_id, l.paid_delta, (SELECT coalesce(sum(a.stars), 0) FROM star_spend_allocs a WHERE a.ledger_id = l.id) AS allocated
       FROM star_ledger l WHERE l.paid_delta < 0 AND -l.paid_delta <> (SELECT coalesce(sum(a.stars), 0) FROM star_spend_allocs a WHERE a.ledger_id = l.id) ORDER BY l.id`,
  );
  // I6-5 막힌 주문
  await add(
    'stuck_order',
    `SELECT uuid AS "order", state, created_at FROM star_orders
      WHERE (state = 'pending_init' AND created_at < now() - interval '10 minutes')
         OR (state IN ('created', 'authorized') AND expires_at < now() - interval '10 minutes')
         OR (state = 'finalized' AND finalized_at < now() - interval '5 minutes') ORDER BY id`,
  );
  // I6-6 근거 없는 입금(트리거가 꺼진 DB 대비): 승인 없는 운영 지급·탕감
  await add(
    'unbacked_credit',
    `SELECT l.id AS ledger_id, l.reason, l.ref FROM star_ledger l
      WHERE l.reason IN ('admin_grant', 'debt_forgive')
        AND NOT EXISTS (SELECT 1 FROM star_admin_grants g WHERE g.uuid::text = l.ref AND g.state = 'applied') ORDER BY l.id`,
  );
  // I6-7 Steam 교차: 마지막 payment-report 결과의 미지 주문, 해결되지 않은 unknown_steam_order 플래그
  await add(
    'unknown_steam_order',
    `SELECT 'report' AS source, (detail->>'unknown_orders')::int AS n FROM (
        SELECT detail FROM job_runs WHERE job = 'payment-report' AND status = 'ok' ORDER BY started_at DESC LIMIT 1) j
      WHERE coalesce((detail->>'unknown_orders')::int, 0) > 0
     UNION ALL SELECT 'flag', count(*)::int FROM payment_flags WHERE kind = 'unknown_steam_order' AND state = 'open' HAVING count(*) > 0`,
  );
  // I6-8 부채 일관: debt > 0 인 계정마다 그 부채를 만든 회수 원장이 있다
  await add(
    'debt_without_revoke',
    `SELECT a.uuid AS account, w.debt FROM star_wallets w JOIN accounts a ON a.id = w.account_id
      WHERE w.debt > 0 AND NOT EXISTS (SELECT 1 FROM star_ledger l WHERE l.account_id = w.account_id AND l.reason IN ('refund_revoke', 'chargeback_revoke') AND l.debt_delta > 0)
      ORDER BY w.account_id`,
  );
  return { count: parts.reduce((a, [, r]) => a + r.count, 0), samples: parts.flatMap(([, r]) => r.samples).slice(0, SAMPLE_MAX) };
}

/** 9단계 정보 점검(경보의 mismatches에 넣지 않는다: 파생 표는 스스로 고쳐지고, 이름·정지는 운영 판단이다) */
async function nineInfo(): Promise<Record<string, CheckResult>> {
  const income = await reconcileIncome({ hours: 48, fix: false });
  const names = await getPool().query<{ uuid: string; name: string }>('SELECT uuid, name FROM characters WHERE deleted_at IS NULL');
  const badNames = names.rows.filter((r) => forbiddenReason(r.name) !== null);
  const career = await rows(
    `SELECT c.uuid AS character, COALESCE((cs.career->>'career')::int, 0) AS state_career, COALESCE(cc.career, 0) AS granted
       FROM character_state cs JOIN characters c ON c.id = cs.character_id
       LEFT JOIN character_career cc ON cc.character_id = c.id
      WHERE c.deleted_at IS NULL AND (cs.career IS NULL OR jsonb_typeof(cs.career) = 'object')
        AND COALESCE((cs.career->>'career')::int, 0) <> COALESCE(cc.career, 0)
      ORDER BY c.id`,
  );
  const holds = await rows(
    "SELECT uuid, account_id, character_id, created_at FROM economy_holds WHERE state = 'active' AND reviewed_at IS NULL AND created_at < now() - interval '24 hours' ORDER BY id",
  );
  return {
    'I-income': { count: income.mismatched, samples: income.samples },
    'I-names': { count: badNames.length, samples: badNames.slice(0, SAMPLE_MAX).map((r) => ({ character: r.uuid, name: r.name })) },
    'I-career': career,
    'I-holds': holds,
  };
}

/**
 * I7 (탈퇴): 익명화 누락과 상태 어긋남. 읽기 전용.
 *  a 익명화된 계정에 신원·토큰·접속 기록이 남아 있다  b 익명화된 계정에 살아 있는/자리표시가 아닌 캐릭터가 있다
 *  c 요청 상태와 accounts.deleted_at 이 어긋난다     d 요청이 보류 상한(WITHDRAW_DEFER_MAX_DAYS)을 넘게 지났다(작업 정지)
 */
async function i7(): Promise<CheckResult> {
  const maxDays = Math.trunc(getConfig().withdraw.deferMaxDays);
  const leftovers = ['auth_identities', 'refresh_tokens', 'login_events', 'account_devices', 'account_ips', 'online_sessions']
    .map((t) => `SELECT a.uuid AS account, '${t}' AS leftover FROM accounts a WHERE a.anonymized_at IS NOT NULL AND EXISTS (SELECT 1 FROM ${t} x WHERE x.account_id = a.id)`)
    .join(' UNION ALL ');
  const a = await rows(leftovers);
  const b = await rows(
    `SELECT a.uuid AS account, c.uuid AS character FROM accounts a JOIN characters c ON c.account_id = a.id
      WHERE a.anonymized_at IS NOT NULL AND (c.deleted_at IS NULL OR c.name !~ '^탈퇴[0-9a-f]{6}$') ORDER BY c.id`,
  );
  const c = await rows(
    `SELECT a.uuid AS account, w.uuid AS withdrawal, w.state FROM account_withdrawals w JOIN accounts a ON a.id = w.account_id
      WHERE (w.state = 'requested' AND a.deleted_at IS NULL) OR (w.state = 'completed' AND a.anonymized_at IS NULL)
     UNION ALL
     SELECT a.uuid, NULL, 'no_request' FROM accounts a
      WHERE a.deleted_at IS NOT NULL AND NOT EXISTS (SELECT 1 FROM account_withdrawals w WHERE w.account_id = a.id AND w.state IN ('requested', 'completed'))`,
  );
  const d = await rows(
    `SELECT uuid AS withdrawal, due_at FROM account_withdrawals
      WHERE state = 'requested' AND NOT manual_hold AND due_at < now() - (${maxDays}::int * interval '1 day') ORDER BY id`,
  );
  return { count: a.count + b.count + c.count + d.count, samples: [...a.samples, ...b.samples, ...c.samples, ...d.samples].slice(0, SAMPLE_MAX) };
}

export async function integrityJob(ctx: JobCtx): Promise<JobResult> {
  const full = ctx.opts.full === true || isKstSunday(getNow());
  const checks: Record<string, CheckResult> = {
    I1: await i1(full),
    I2: await i2(full),
    I3: await i3(),
    I4: await i4(),
    I5: await i5(),
    I6: await i6(),
    I7: await i7(),
  };
  const mismatches = Object.values(checks).reduce((a, c) => a + c.count, 0);
  const info = await nineInfo();
  return { rows: 0, detail: { scope: full ? 'full' : 'recent', mismatches, checks, info } };
}
