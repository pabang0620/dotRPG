// 결제 SQL(star_orders, star_order_events, payment_profiles, payment_flags). 별조각 잔액·원장은 starshop/starWallet.ts 한 곳이다.
import type { Queryable } from '../../db/pool';

export type OrderState = 'pending_init' | 'created' | 'authorized' | 'finalized' | 'granted' | 'failed' | 'expired' | 'refunded' | 'chargeback';
export const OPEN_STATES: readonly OrderState[] = ['pending_init', 'created', 'authorized', 'finalized'];
export const COUNTED_EXCLUDED: readonly OrderState[] = ['failed', 'expired'];
export type FailReason = 'init_rejected' | 'init_lost' | 'user_denied' | 'steam_failed' | 'blocked' | 'mismatch';

export interface OrderRow {
  id: number;
  uuid: string;
  account_id: number;
  request_id: string;
  request_hash: string;
  steam_order_id: string;
  steam_trans_id: string | null;
  steam_id: string;
  app_id: number;
  product_id: string;
  steam_item_id: number;
  catalog_version: string;
  stars: number;
  currency: string;
  amount_minor: number;
  steam_country: string | null;
  steam_status: string | null;
  steam_amount_minor: number | null;
  steam_currency: string | null;
  tier: 'new' | 'standard' | 'restricted';
  state: OrderState;
  fail_reason: FailReason | null;
  needs_review: boolean;
  review_reason: string | null;
  ip: string | null;
  device_hash: string | null;
  lease_until: Date | null;
  lease_token: string | null;
  attempts: number;
  next_check_at: Date | null;
  last_checked_at: Date | null;
  expires_at: Date;
  created_at: Date;
  init_at: Date | null;
  authorized_at: Date | null;
  finalized_at: Date | null;
  granted_at: Date | null;
  reversed_at: Date | null;
  closed_at: Date | null;
}

interface RawOrder extends Omit<OrderRow, 'id' | 'account_id' | 'app_id' | 'amount_minor' | 'steam_amount_minor'> {
  id: string;
  account_id: string;
  app_id: string;
  amount_minor: string;
  steam_amount_minor: string | null;
}

export const ORDER_COLS = `id, uuid, account_id, request_id, request_hash, steam_order_id::text AS steam_order_id, steam_trans_id::text AS steam_trans_id,
  steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency, amount_minor, steam_country, steam_status, steam_amount_minor,
  steam_currency, tier, state, fail_reason, needs_review, review_reason, ip::text AS ip, device_hash, lease_until, lease_token, attempts, next_check_at,
  last_checked_at, expires_at, created_at, init_at, authorized_at, finalized_at, granted_at, reversed_at, closed_at`;

export const toOrder = (r: RawOrder): OrderRow => ({
  ...r,
  id: Number(r.id),
  account_id: Number(r.account_id),
  app_id: Number(r.app_id),
  amount_minor: Number(r.amount_minor),
  steam_amount_minor: r.steam_amount_minor === null ? null : Number(r.steam_amount_minor),
});

export async function orderById(db: Queryable, id: number): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(`SELECT ${ORDER_COLS} FROM star_orders WHERE id = $1`, [id]);
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

/**
 * 주문 행 잠금(락 ③). FOR UPDATE 가 아니라 FOR NO KEY UPDATE 다: 별조각 소비가 `star_spend_allocs`(order_id FK)를 넣을 때 주문 행에 FOR KEY SHARE 를 잡는데,
 * FOR UPDATE 는 그것과 충돌해 "회수(주문 -> 지갑)" 와 "소비(지갑 -> 배분 FK -> 주문)" 가 교착한다(시험으로 확인). 키 열은 바꾸지 않으므로 같은 보호가 된다
 */
export async function lockOrderById(db: Queryable, id: number): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(`SELECT ${ORDER_COLS} FROM star_orders WHERE id = $1 FOR NO KEY UPDATE`, [id]);
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

/** 내 주문만(남의 것은 null: 404로 구분하지 않는다) */
export async function ownedOrderByUuid(db: Queryable, accountId: number, uuid: string): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(`SELECT ${ORDER_COLS} FROM star_orders WHERE uuid = $1 AND account_id = $2`, [uuid, accountId]);
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

export async function orderByUuid(db: Queryable, uuid: string): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(`SELECT ${ORDER_COLS} FROM star_orders WHERE uuid = $1`, [uuid]);
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

export async function orderByRequest(db: Queryable, accountId: number, requestId: string): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(`SELECT ${ORDER_COLS} FROM star_orders WHERE account_id = $1 AND request_id = $2`, [accountId, requestId]);
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

export async function orderBySteamOrderId(db: Queryable, steamOrderId: string): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(`SELECT ${ORDER_COLS} FROM star_orders WHERE steam_order_id = $1::bigint`, [steamOrderId]);
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

export async function openOrderOf(db: Queryable, accountId: number): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(
    `SELECT ${ORDER_COLS} FROM star_orders WHERE account_id = $1 AND state IN ('pending_init', 'created', 'authorized', 'finalized') LIMIT 1`,
    [accountId],
  );
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

export async function listOrders(db: Queryable, accountId: number, beforeId: number | null, limit: number): Promise<OrderRow[]> {
  const r = await db.query<RawOrder>(
    `SELECT ${ORDER_COLS} FROM star_orders WHERE account_id = $1 AND ($2::bigint IS NULL OR id < $2) ORDER BY id DESC LIMIT $3`,
    [accountId, beforeId, limit],
  );
  return r.rows.map(toOrder);
}

// ---------------- 계정 ----------------

/** accounts 행 잠금(락 ②). 많은 표가 FK 로 계정을 가리켜 FOR UPDATE 는 지갑·원장 쓰기(FOR KEY SHARE)와 교착할 수 있어 FOR NO KEY UPDATE 를 쓴다 */
export async function lockAccount(db: Queryable, accountId: number): Promise<{ created_at: Date } | null> {
  const r = await db.query<{ created_at: Date }>('SELECT created_at FROM accounts WHERE id = $1 FOR NO KEY UPDATE', [accountId]);
  return r.rows[0] ?? null;
}

export async function accountInfo(db: Queryable, accountId: number): Promise<{ created_at: Date; active_device_hash: string | null } | null> {
  const r = await db.query<{ created_at: Date; active_device_hash: string | null }>('SELECT created_at, active_device_hash FROM accounts WHERE id = $1', [accountId]);
  return r.rows[0] ?? null;
}

export async function accountIdByUuid(db: Queryable, uuid: string): Promise<number | null> {
  const r = await db.query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [uuid]);
  return r.rows[0] ? Number(r.rows[0].id) : null;
}

export async function accountUuid(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ uuid: string }>('SELECT uuid FROM accounts WHERE id = $1', [accountId]);
  return r.rows[0]?.uuid ?? null;
}

/** 로그인 계정의 Steam ID(auth_identities.subject). 요청으로 받지 않는다 */
export async function steamIdOf(db: Queryable, accountId: number): Promise<string | null> {
  const r = await db.query<{ subject: string }>("SELECT subject FROM auth_identities WHERE account_id = $1 AND provider = 'steam'", [accountId]);
  return r.rows[0]?.subject ?? null;
}

export async function accountByActiveHold(db: Queryable, accountId: number): Promise<{ created_at: Date; character_id: string | null } | null> {
  const r = await db.query<{ created_at: Date; character_id: string | null }>(
    "SELECT created_at, character_id FROM economy_holds WHERE account_id = $1 AND state IN ('active', 'clawed_back') ORDER BY created_at LIMIT 1",
    [accountId],
  );
  return r.rows[0] ?? null;
}

// ---------------- 결제 프로필 ----------------

export interface ProfileRow {
  account_id: number;
  status: 'active' | 'blocked';
  block_reason: string | null;
  blocked_at: Date | null;
  blocked_by: string | null;
  tier_floor: 'none' | 'restricted';
  tier_floor_until: Date | null;
  last_country: string | null;
  last_currency: string | null;
  note: string | null;
}

const PROFILE_COLS = 'account_id, status, block_reason, blocked_at, blocked_by, tier_floor, tier_floor_until, last_country, last_currency, note';

export async function profileOf(db: Queryable, accountId: number): Promise<ProfileRow | null> {
  const r = await db.query<ProfileRow & { account_id: string }>(`SELECT ${PROFILE_COLS} FROM payment_profiles WHERE account_id = $1`, [accountId]);
  return r.rows[0] ? { ...r.rows[0], account_id: Number(r.rows[0].account_id) } : null;
}

export async function ensureProfile(db: Queryable, accountId: number): Promise<void> {
  await db.query('INSERT INTO payment_profiles (account_id) VALUES ($1) ON CONFLICT DO NOTHING', [accountId]);
}

export async function noteCountry(db: Queryable, accountId: number, country: string, currency: string): Promise<void> {
  await ensureProfile(db, accountId);
  await db.query('UPDATE payment_profiles SET last_country = $2, last_currency = $3, updated_at = now() WHERE account_id = $1', [accountId, country, currency]);
}

export async function blockProfile(db: Queryable, accountId: number, reason: string, by: string, note: string | null): Promise<boolean> {
  await ensureProfile(db, accountId);
  const r = await db.query(
    `UPDATE payment_profiles SET status = 'blocked', block_reason = $2, blocked_at = now(), blocked_by = $3, note = coalesce($4, note), updated_at = now()
      WHERE account_id = $1 AND status <> 'blocked'`,
    [accountId, reason, by, note],
  );
  return (r.rowCount ?? 0) > 0;
}

export async function unblockProfile(db: Queryable, accountId: number, floorUntil: Date | null, note: string | null): Promise<boolean> {
  const r = await db.query(
    `UPDATE payment_profiles SET status = 'active', block_reason = NULL, blocked_at = NULL, blocked_by = NULL,
            tier_floor = CASE WHEN $2::timestamptz IS NULL THEN tier_floor ELSE 'restricted' END,
            tier_floor_until = CASE WHEN $2::timestamptz IS NULL THEN tier_floor_until ELSE $2 END,
            note = coalesce($3, note), updated_at = now()
      WHERE account_id = $1 AND status = 'blocked'`,
    [accountId, floorUntil, note],
  );
  return (r.rowCount ?? 0) > 0;
}

/** 제한 단계 하한을 올린다(더 늦은 쪽을 유지) */
export async function raiseTierFloor(db: Queryable, accountId: number, until: Date): Promise<void> {
  await ensureProfile(db, accountId);
  await db.query(
    `UPDATE payment_profiles SET tier_floor = 'restricted',
            tier_floor_until = CASE WHEN tier_floor = 'restricted' AND tier_floor_until > $2 THEN tier_floor_until ELSE $2 END, updated_at = now()
      WHERE account_id = $1`,
    [accountId, until],
  );
}

// ---------------- 이벤트 ----------------

export type EventKind =
  | 'created' | 'init_ok' | 'init_failed' | 'init_unknown' | 'status_seen' | 'authorized' | 'finalize_ok' | 'finalize_failed' | 'finalized'
  | 'granted' | 'failed' | 'expired' | 'refunded' | 'chargeback' | 'revoked_stars' | 'mismatch' | 'needs_review' | 'review_cleared'
  | 'admin_recheck' | 'outcomes_revoked';

/** detail 은 허용 목록 필드만(상태 문자열, 주문·거래 번호, 통화, 금액, 국가, 시각, 사유 코드). 키·URL·원문·IP는 넣지 않는다 */
export async function insertEvent(
  db: Queryable,
  orderId: number,
  e: { kind: EventKind; from?: string | null; to?: string | null; steamStatus?: string | null; actor: 'player' | 'job' | 'admin' | 'system'; detail?: Record<string, unknown> },
): Promise<void> {
  await db.query(
    'INSERT INTO star_order_events (order_id, kind, from_state, to_state, steam_status, actor, detail) VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb)',
    [orderId, e.kind, e.from ?? null, e.to ?? null, e.steamStatus ?? null, e.actor, JSON.stringify(e.detail ?? {})],
  );
}

export interface EventRow {
  id: number;
  kind: string;
  from_state: string | null;
  to_state: string | null;
  steam_status: string | null;
  actor: string;
  detail: Record<string, unknown>;
  created_at: Date;
}

export async function eventsOf(db: Queryable, orderId: number): Promise<EventRow[]> {
  const r = await db.query<EventRow & { id: string }>(
    'SELECT id, kind, from_state, to_state, steam_status, actor, detail, created_at FROM star_order_events WHERE order_id = $1 ORDER BY id',
    [orderId],
  );
  return r.rows.map((x) => ({ ...x, id: Number(x.id) }));
}

// ---------------- 임대(lease)와 펜싱 ----------------

/** 6.4-1: 한 작업자만 주문을 진행한다. 열린 상태·지급된 주문(감시)·검토 필요 주문만. throttleSec > 0 이면 최근 확인 직후는 건너뛴다 */
export async function acquireLease(db: Queryable, id: number, leaseSeconds: number, throttleSec: number): Promise<OrderRow | null> {
  const r = await db.query<RawOrder>(
    `UPDATE star_orders SET lease_until = now() + ($2::int * interval '1 second'), lease_token = gen_random_uuid()
      WHERE id = $1 AND (state IN ('pending_init', 'created', 'authorized', 'finalized', 'granted', 'refunded') OR needs_review)
        AND (lease_until IS NULL OR lease_until < now())
        AND ($3::int <= 0 OR last_checked_at IS NULL OR last_checked_at <= now() - ($3::int * interval '1 second'))
      RETURNING ${ORDER_COLS}`,
    [id, leaseSeconds, throttleSec],
  );
  return r.rows[0] ? toOrder(r.rows[0]) : null;
}

export async function releaseLease(db: Queryable, id: number, token: string): Promise<void> {
  await db.query('UPDATE star_orders SET lease_until = NULL, lease_token = NULL WHERE id = $1 AND lease_token = $2', [id, token]);
}

const SETTABLE = new Set([
  'state', 'fail_reason', 'needs_review', 'review_reason', 'steam_trans_id', 'steam_country', 'steam_status', 'steam_amount_minor', 'steam_currency',
  'attempts', 'next_check_at', 'last_checked_at', 'init_at', 'authorized_at', 'finalized_at', 'granted_at', 'reversed_at', 'closed_at', 'expires_at',
]);

/** 펜싱 쓰기: WHERE id AND lease_token AND state = 기대 상태. 0행이면 임대를 잃은 것이므로 아무것도 바꾸지 못한다. set 키는 코드가 정한 열 이름만(허용 목록 검사) */
export async function updateFenced(
  db: Queryable,
  id: number,
  token: string,
  expectState: OrderState | OrderState[],
  set: Record<string, unknown>,
): Promise<boolean> {
  const cols = Object.keys(set);
  for (const c of cols) if (!SETTABLE.has(c)) throw new Error(`star_orders column not settable: ${c}`);
  const sets = cols.map((c, i) => `${c} = $${i + 4}`).join(', ');
  const states = Array.isArray(expectState) ? expectState : [expectState];
  const r = await db.query(
    `UPDATE star_orders SET ${sets || 'attempts = attempts'} WHERE id = $1 AND lease_token = $2 AND state = ANY($3::text[])`,
    [id, token, states, ...cols.map((c) => set[c])],
  );
  return (r.rowCount ?? 0) > 0;
}

/** 상태 변경 없이 다음 확인 시각만(임대 없이 쓰는 안전한 갱신: 확인한 흔적) */
export async function touchChecked(db: Queryable, id: number, token: string, nextCheckAt: Date | null): Promise<void> {
  await db.query('UPDATE star_orders SET last_checked_at = now(), next_check_at = $3 WHERE id = $1 AND lease_token = $2', [id, token, nextCheckAt]);
}

// ---------------- 한도·속도 조회 ----------------

export async function lastOrderAt(db: Queryable, accountId: number): Promise<Date | null> {
  const r = await db.query<{ t: Date | null }>('SELECT max(created_at) AS t FROM star_orders WHERE account_id = $1', [accountId]);
  return r.rows[0]?.t ?? null;
}

export async function ordersSince(db: Queryable, accountId: number, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM star_orders WHERE account_id = $1 AND created_at > $2', [accountId, since]);
  return Number(r.rows[0]?.n ?? 0);
}

export async function failedSince(db: Queryable, accountId: number, since: Date): Promise<{ n: number; oldest: Date | null }> {
  const r = await db.query<{ n: string; t: Date | null }>(
    "SELECT count(*) AS n, min(created_at) AS t FROM star_orders WHERE account_id = $1 AND state IN ('failed', 'expired') AND created_at > $2",
    [accountId, since],
  );
  return { n: Number(r.rows[0]?.n ?? 0), oldest: r.rows[0]?.t ?? null };
}

/** 한도 합산: failed·expired 를 뺀 모든 주문의 별조각(환불된 주문도 센다) */
export async function starsOrderedSince(db: Queryable, accountId: number, since: Date): Promise<number> {
  const r = await db.query<{ s: string }>(
    "SELECT coalesce(sum(stars), 0) AS s FROM star_orders WHERE account_id = $1 AND created_at >= $2 AND state NOT IN ('failed', 'expired')",
    [accountId, since],
  );
  return Number(r.rows[0]?.s ?? 0);
}

export async function distinctCountriesSince(db: Queryable, accountId: number, since: Date): Promise<string[]> {
  const r = await db.query<{ c: string }>('SELECT DISTINCT steam_country AS c FROM star_orders WHERE account_id = $1 AND created_at > $2 AND steam_country IS NOT NULL', [accountId, since]);
  return r.rows.map((x) => x.c);
}

/** 이 계정의 최근 기기와, 각 기기를 쓴 서로 다른 계정 수(공용 기기 판별) */
export async function purchasingAccountsOnDevices(
  db: Queryable,
  accountId: number,
  deviceSince: Date,
  orderSince: Date,
  publicMax: number,
): Promise<{ device_hash: string; accounts: number; purchasers: number }[]> {
  const r = await db.query<{ device_hash: string; accounts: string; purchasers: string }>(
    `SELECT d.device_hash,
            (SELECT count(DISTINCT x.account_id) FROM account_devices x WHERE x.device_hash = d.device_hash AND x.last_seen_at > $2) AS accounts,
            (SELECT count(DISTINCT x.account_id) FROM account_devices x
               WHERE x.device_hash = d.device_hash AND x.last_seen_at > $2 AND x.account_id <> $1
                 AND EXISTS (SELECT 1 FROM star_orders o WHERE o.account_id = x.account_id AND o.created_at > $3 AND o.state NOT IN ('failed', 'expired'))) AS purchasers
       FROM account_devices d WHERE d.account_id = $1 AND d.last_seen_at > $2`,
    [accountId, deviceSince, orderSince],
  );
  return r.rows.map((x) => ({ device_hash: x.device_hash, accounts: Number(x.accounts), purchasers: Number(x.purchasers) })).filter((x) => x.accounts <= publicMax);
}

/** 이 계정과 연결된(같은 비공용 기기·같은 Steam 소유자 키) 계정 중 차지백 주문 또는 결제 정지가 있는 계정 */
export async function linkedChargebackAccounts(db: Queryable, accountIds: number[]): Promise<number[]> {
  if (accountIds.length === 0) return [];
  const r = await db.query<{ account_id: string }>(
    `SELECT DISTINCT a AS account_id FROM unnest($1::bigint[]) a
      WHERE EXISTS (SELECT 1 FROM star_orders o WHERE o.account_id = a AND o.state = 'chargeback')
         OR EXISTS (SELECT 1 FROM payment_profiles p WHERE p.account_id = a AND p.status = 'blocked' AND p.block_reason IN ('chargeback', 'linked_chargeback', 'refund_abuse'))`,
    [accountIds],
  );
  return r.rows.map((x) => Number(x.account_id));
}

// ---------------- 플래그(검토 큐) ----------------

export type FlagKind =
  | 'rapid_orders' | 'limit_exceeded' | 'fail_burst' | 'country_changed' | 'shared_device' | 'linked_chargeback' | 'refund_after_spend' | 'chargeback'
  | 'partial_refund' | 'amount_mismatch' | 'steamid_mismatch' | 'appid_mismatch' | 'unknown_steam_order' | 'stuck_order' | 'report_gap' | 'unknown_steam_status';

export async function insertFlag(
  db: Queryable,
  f: { accountId: number; orderId: number | null; kind: FlagKind; severity: 1 | 2 | 3; detail: Record<string, unknown>; dedupeMinutes: number },
): Promise<boolean> {
  if (f.dedupeMinutes > 0) {
    const dup = await db.query(
      `SELECT 1 FROM payment_flags WHERE account_id = $1 AND kind = $2 AND order_id IS NOT DISTINCT FROM $3 AND created_at > now() - ($4::int * interval '1 minute') LIMIT 1`,
      [f.accountId, f.kind, f.orderId, f.dedupeMinutes],
    );
    if (dup.rows.length > 0) return false;
  }
  await db.query('INSERT INTO payment_flags (account_id, order_id, kind, severity, detail) VALUES ($1, $2, $3, $4, $5::jsonb)', [
    f.accountId,
    f.orderId,
    f.kind,
    f.severity,
    JSON.stringify(f.detail),
  ]);
  return true;
}

export async function countFlags(db: Queryable, accountId: number, kind: FlagKind, since: Date): Promise<number> {
  const r = await db.query<{ n: string }>('SELECT count(*) AS n FROM payment_flags WHERE account_id = $1 AND kind = $2 AND created_at > $3', [accountId, kind, since]);
  return Number(r.rows[0]?.n ?? 0);
}

// ---------------- 경제 정지(결제 종류) ----------------

/** 차지백이 거는 경제 정지(kind='payment'). 같은 종류의 활성 정지가 이미 있으면 만들지 않는다. 연결 계정으로 전파하지 않는다 */
export async function insertPaymentHold(db: Queryable, accountId: number, evidence: Record<string, unknown>): Promise<boolean> {
  const exist = await db.query(
    "SELECT 1 FROM economy_holds WHERE account_id = $1 AND scope_char = 0 AND kind = 'payment' AND state IN ('active', 'clawed_back') LIMIT 1",
    [accountId],
  );
  if (exist.rows.length > 0) return false;
  await db.query(
    `INSERT INTO economy_holds (account_id, character_id, kind, state, evidence, note)
     VALUES ($1, NULL, 'payment', 'active', $2::jsonb, '결제 차지백(자동)')`,
    [accountId, JSON.stringify(evidence)],
  );
  return true;
}

// ---------------- 작업·관리자 ----------------

export async function dueOrders(db: Queryable, states: OrderState[], now: Date, limit: number): Promise<{ id: number; account_id: number }[]> {
  const r = await db.query<{ id: string; account_id: string }>(
    `SELECT id, account_id FROM star_orders WHERE next_check_at IS NOT NULL AND next_check_at <= $2 AND state = ANY($1::text[])
        AND (lease_until IS NULL OR lease_until < now()) ORDER BY next_check_at LIMIT $3`,
    [states, now, limit],
  );
  return r.rows.map((x) => ({ id: Number(x.id), account_id: Number(x.account_id) }));
}

export async function orderStatsSince(db: Queryable, since: Date): Promise<Record<string, number>> {
  const r = await db.query<{ state: string; n: string }>('SELECT state, count(*) AS n FROM star_orders WHERE created_at > $1 GROUP BY state', [since]);
  return Object.fromEntries(r.rows.map((x) => [x.state, Number(x.n)]));
}

export async function oldestOrderSince(db: Queryable, accountId: number, since: Date): Promise<Date | null> {
  const r = await db.query<{ t: Date | null }>('SELECT min(created_at) AS t FROM star_orders WHERE account_id = $1 AND created_at > $2', [accountId, since]);
  return r.rows[0]?.t ?? null;
}

export interface NewOrder {
  accountId: number;
  requestId: string;
  requestHash: string;
  steamId: string;
  appId: number;
  productId: string;
  steamItemId: number;
  catalogVersion: string;
  stars: number;
  currency: string;
  amountMinor: number;
  steamCountry: string | null;
  tier: 'new' | 'standard' | 'restricted';
  ip: string | null;
  deviceHash: string | null;
  expiresAt: Date;
  nextCheckAt: Date;
}

/** 6.2-13: pending_init 행을 먼저 커밋한다(Steam에 보내기 전에 의도를 남긴다). 임대를 잡은 채로 만들어 대사 작업과 겹치지 않는다 */
export async function insertOrder(db: Queryable, n: NewOrder): Promise<OrderRow> {
  const r = await db.query<RawOrder>(
    `INSERT INTO star_orders (account_id, request_id, request_hash, steam_id, app_id, product_id, steam_item_id, catalog_version, stars, currency,
                              amount_minor, steam_country, tier, state, ip, device_hash, lease_until, lease_token, next_check_at, expires_at)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, 'pending_init', $14, $15, now() + interval '30 seconds', gen_random_uuid(), $16, $17)
     RETURNING ${ORDER_COLS}`,
    [n.accountId, n.requestId, n.requestHash, n.steamId, n.appId, n.productId, n.steamItemId, n.catalogVersion, n.stars, n.currency, n.amountMinor, n.steamCountry, n.tier, n.ip, n.deviceHash, n.nextCheckAt, n.expiresAt],
  );
  return toOrder(r.rows[0] as RawOrder);
}
