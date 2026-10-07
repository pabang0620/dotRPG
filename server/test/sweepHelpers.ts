// 10단계 테스트 공용: 직접 클리어 기록·클리어권 로트 시드, 소탕 요청, 원장 일관성 확인
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import { get, post, type Hero } from './economyHelpers';

/** 월요일 12:00 KST: gold_vein이 열려 있다 */
export const MONDAY = '2026-10-05T03:00:00Z';

export async function accountIdOf(h: Hero): Promise<number> {
  const r = await getPool().query<{ account_id: string }>('SELECT account_id FROM characters WHERE id = $1', [h.dbId]);
  return Number((r.rows[0] as { account_id: string }).account_id);
}

/** 직접 클리어 기록 한 줄(랭크 번호: 0 SSS ... 4 B, 5 C). locked면 기여 부족으로 보상이 잠긴 판 */
export async function seedClear(h: Hero, dungeonId: string, difficulty: number, rank = 2, locked = false): Promise<void> {
  await getPool().query(
    `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, ended_at, rank, cards, reward_locked, lock_reason, counts_entry)
     VALUES ($1, $2, $3, 'cleared', now() - interval '3 days', now() - interval '3 days', $4, '[]'::jsonb, $5, $6, false)`,
    [h.dbId, dungeonId, difficulty, rank, locked, locked ? 'LOW_CONTRIBUTION' : null],
  );
}

/** 계정에 클리어권을 심는다(원장도 같이). 일반 로트 하나와 이벤트 로트 여러 개 */
export async function seedTickets(h: Hero, o: { normal?: number; events?: { n: number; expiresAt: Date }[] }): Promise<void> {
  const acct = await accountIdOf(h);
  const db = getPool();
  let running = 0;
  const put = async (kind: 'normal' | 'event', n: number, exp: Date | null): Promise<void> => {
    const lot = await db.query<{ id: string }>(
      `INSERT INTO sweep_ticket_lots (account_id, kind, granted, remaining, expires_at) VALUES ($1, $2, $3, $3, $4)
       ON CONFLICT (account_id) WHERE kind = 'normal' DO UPDATE SET granted = sweep_ticket_lots.granted + $3, remaining = sweep_ticket_lots.remaining + $3
       RETURNING id`,
      [acct, kind, n, exp],
    );
    running += n;
    await db.query(
      `INSERT INTO sweep_ticket_ledger (account_id, lot_id, character_id, delta, balance_after, reason, ref) VALUES ($1, $2, $3, $4, $5, 'shop_buy', $6)`,
      [acct, (lot.rows[0] as { id: string }).id, h.dbId, n, running, randomUUID()],
    );
  };
  if (o.normal) await put('normal', o.normal, null);
  for (const e of o.events ?? []) await put('event', e.n, e.expiresAt);
}

export async function ticketTotal(h: Hero, at: Date = new Date()): Promise<number> {
  const r = await getPool().query<{ n: string }>(
    `SELECT coalesce(sum(remaining), 0) AS n FROM sweep_ticket_lots WHERE account_id = $1 AND remaining > 0 AND (expires_at IS NULL OR expires_at > $2)`,
    [await accountIdOf(h), at],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 로트별 SUM(원장 delta) = remaining */
export async function expectTicketLedgerConsistent(h: Hero): Promise<void> {
  const r = await getPool().query<{ id: string; remaining: number; s: string }>(
    `SELECT l.id, l.remaining, coalesce((SELECT sum(delta) FROM sweep_ticket_ledger WHERE lot_id = l.id), 0) AS s
       FROM sweep_ticket_lots l WHERE l.account_id = $1`,
    [await accountIdOf(h)],
  );
  for (const row of r.rows) {
    if (Number(row.s) !== row.remaining) throw new Error(`클리어권 원장 불일치 lot ${row.id}: 원장 ${row.s} 남은 ${row.remaining}`);
  }
}

export const sweepRows = async (h: Hero): Promise<Record<string, unknown>[]> =>
  (await getPool().query('SELECT * FROM dungeon_sweeps WHERE character_id = $1 ORDER BY id', [h.dbId])).rows;

export const sweepRun = (app: Express, h: Hero, dungeonId = 'gold_vein', difficulty = 0, rid?: string) =>
  post(app, h, '/sweep/run', { dungeon_id: dungeonId, difficulty }, rid);
export const sweepAll = (app: Express, h: Hero, dungeonId = 'gold_vein', difficulty = 0, rid?: string) =>
  post(app, h, '/sweep/run-all', { dungeon_id: dungeonId, difficulty }, rid);
export const sweepStatus = (app: Express, h: Hero) => get(app, h, '/sweep');
