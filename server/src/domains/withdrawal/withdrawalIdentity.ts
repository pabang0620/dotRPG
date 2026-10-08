// 이월 표시(withdrawn_identities, 설계 9.3): 탈퇴한 Steam 계정의 제재·결제 차단을 재가입으로 피하지 못하게 한다.
// identity_hash = HMAC-SHA256(WITHDRAW_ID_HMAC_KEY, steam_id). 원문 Steam ID는 어디에도 남기지 않는다.
import { createHmac } from 'node:crypto';
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as holdsRepo from '../antiabuse/holdsRepository';

export const PAYMENT_BLOCKS = ['chargeback', 'refund_abuse', 'linked_chargeback', 'fraud_suspect', 'manual'] as const;
export type PaymentBlock = (typeof PAYMENT_BLOCKS)[number];

/** 영구 정지의 종료 시각(9.3) */
export const FOREVER = new Date('9999-12-31T00:00:00Z');

export function identityHash(steamId: string, key: Buffer = getConfig().withdraw.idHmacKey): string {
  return createHmac('sha256', key).update(steamId).digest('hex');
}

export interface TombstoneRow {
  id: number;
  uuid: string;
  identity_hash: string;
  source_account_id: number;
  carry_ban_until: Date | null;
  carry_payment_block: PaymentBlock | null;
  carry_econ_hold: boolean;
  created_at: Date;
  updated_at: Date;
  expires_at: Date;
  released_at: Date | null;
  released_by: string | null;
}
interface Raw extends Omit<TombstoneRow, 'id' | 'source_account_id'> {
  id: string;
  source_account_id: string;
}
const COLS = `id, uuid, identity_hash, source_account_id, carry_ban_until, carry_payment_block, carry_econ_hold, created_at, updated_at,
  expires_at, released_at, released_by`;
const to = (r: Raw): TombstoneRow => ({ ...r, id: Number(r.id), source_account_id: Number(r.source_account_id) });

/** 재가입 검사용: 풀리지 않았고 만료 전인 표시 */
export async function findActive(db: Queryable, hash: string, now: Date): Promise<TombstoneRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM withdrawn_identities WHERE identity_hash = $1 AND released_at IS NULL AND expires_at > $2`, [hash, now]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function findAnyByHash(db: Queryable, hash: string): Promise<TombstoneRow | null> {
  const r = await db.query<Raw>(`SELECT ${COLS} FROM withdrawn_identities WHERE identity_hash = $1`, [hash]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function lockByUuid(client: PoolClient, uuid: string): Promise<TombstoneRow | null> {
  const r = await client.query<Raw>(`SELECT ${COLS} FROM withdrawn_identities WHERE uuid = $1 FOR UPDATE`, [uuid]);
  return r.rows[0] ? to(r.rows[0]) : null;
}

export async function release(client: Queryable, id: number, by: string, now: Date): Promise<void> {
  await client.query('UPDATE withdrawn_identities SET released_at = $2, released_by = $3, updated_at = $2 WHERE id = $1', [id, now, by.slice(0, 40)]);
}

export interface Carry {
  banUntil: Date | null;
  paymentBlock: PaymentBlock | null;
  econHold: boolean;
}

/** 이월 표시 만들기·합치기. 같은 해시가 있으면 더 강한 쪽으로 합치고 풀림 표시를 지운다 */
export async function upsertTombstone(client: Queryable, hash: string, sourceAccountId: number, carry: Carry, now: Date): Promise<void> {
  const cfg = getConfig().withdraw;
  const cap = new Date(now.getTime() + cfg.tombstoneMaxDays * 86_400_000);
  // 만료: 사유의 끝. 정지만 있으면 그 종료 시각, 결제·경제 이월이 있으면 상한까지. 어느 쪽이든 상한을 넘지 않는다
  const openEnded = carry.paymentBlock !== null || carry.econHold;
  const end = openEnded ? cap : carry.banUntil ?? cap;
  const expires = new Date(Math.min(end.getTime(), cap.getTime()));
  await client.query(
    `INSERT INTO withdrawn_identities (identity_hash, source_account_id, carry_ban_until, carry_payment_block, carry_econ_hold, created_at, updated_at, expires_at)
     VALUES ($1, $2, $3, $4, $5, $6, $6, $7)
     ON CONFLICT (identity_hash) DO UPDATE SET
       carry_ban_until = CASE WHEN EXCLUDED.carry_ban_until IS NULL THEN withdrawn_identities.carry_ban_until
                              WHEN withdrawn_identities.carry_ban_until IS NULL THEN EXCLUDED.carry_ban_until
                              ELSE GREATEST(EXCLUDED.carry_ban_until, withdrawn_identities.carry_ban_until) END,
       carry_payment_block = coalesce(EXCLUDED.carry_payment_block, withdrawn_identities.carry_payment_block),
       carry_econ_hold = EXCLUDED.carry_econ_hold OR withdrawn_identities.carry_econ_hold,
       expires_at = GREATEST(EXCLUDED.expires_at, withdrawn_identities.expires_at),
       source_account_id = EXCLUDED.source_account_id,
       released_at = NULL, released_by = NULL, updated_at = EXCLUDED.updated_at`,
    [hash, sourceAccountId, carry.banUntil, carry.paymentBlock, carry.econHold, now, expires],
  );
}

/**
 * 재가입 검사(steamLogin 계정 생성 직전, 같은 트랜잭션). 정지가 남았으면 계정을 만들지 않고 403.
 * 그 밖의 이월은 계정을 만든 뒤 applyCarry 로 건다. 이월할 것이 없으면 null.
 */
export async function checkRejoin(client: Queryable, steamId: string): Promise<TombstoneRow | null> {
  const now = getNow();
  const t = await findActive(client, identityHash(steamId), now);
  if (!t) return null;
  if (t.carry_ban_until && t.carry_ban_until.getTime() > now.getTime()) {
    throw new AppError(403, '정지된 계정입니다.', 'ACCOUNT_BANNED', { banned_until: t.carry_ban_until.toISOString() });
  }
  return t.carry_payment_block || t.carry_econ_hold ? t : null;
}

/** 새로 만든 계정에 이월을 건다: 결제 정지(payment_profiles blocked)와 수동 경제 정지 */
export async function applyCarry(client: Queryable, accountId: number, t: TombstoneRow): Promise<void> {
  const now = getNow();
  if (t.carry_payment_block) {
    await client.query(
      `INSERT INTO payment_profiles (account_id, status, block_reason, blocked_at, blocked_by, note)
       VALUES ($1, 'blocked', $2, $3, 'system', '탈퇴 계정에서 이월')
       ON CONFLICT (account_id) DO UPDATE SET status = 'blocked', block_reason = EXCLUDED.block_reason, blocked_at = EXCLUDED.blocked_at, blocked_by = 'system'`,
      [accountId, t.carry_payment_block, now],
    );
  }
  if (t.carry_econ_hold) {
    // 방금 만든 계정이라 같은 범위의 활성 정지가 있을 수 없다(유일 인덱스 충돌 없음)
    await holdsRepo.insertHold(client, { accountId, characterId: null, kind: 'manual', state: 'active', evidence: { source: 'withdrawn_identity' }, createdAt: now, note: '탈퇴 계정에서 이월' });
  }
}
