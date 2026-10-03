// WT1~WT3: 이상 기록, 경매 플래그, 의심 계정 순위(검토 작업 대기열)
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import * as repo from './watchRepository';
import type { AnomalyQueryT, FlagQueryT } from './watchValidation';

async function accountIdOf(uuid: string | undefined): Promise<number | null | 'none'> {
  if (!uuid) return null;
  const r = await getPool().query<{ id: string }>('SELECT id FROM accounts WHERE uuid = $1', [uuid]);
  return r.rows[0] ? Number(r.rows[0].id) : 'none';
}

const outRow = (r: { id: string; account: string; character: string | null; kind: string; severity: number; detail: unknown; created_at: Date }) => ({
  account: r.account,
  character: r.character,
  kind: r.kind,
  severity: r.severity,
  detail: r.detail,
  at: r.created_at.toISOString(),
});

export async function anomalies(q: AnomalyQueryT) {
  const acct = await accountIdOf(q.account);
  if (acct === 'none') return { items: [], next_before: null };
  const rows = await repo.anomalies(getPool(), {
    accountId: acct,
    kind: q.kind ?? null,
    minSeverity: q.min_severity ?? null,
    since: q.since ?? null,
    before: q.before ? Number(q.before) : null,
    limit: q.limit,
  });
  const page = rows.slice(0, q.limit);
  return { items: page.map(outRow), next_before: rows.length > q.limit ? (page[page.length - 1] as { id: string }).id : null };
}

export async function auctionFlags(q: FlagQueryT) {
  const acct = await accountIdOf(q.account);
  if (acct === 'none') return { items: [], next_before: null };
  const rows = await repo.auctionFlags(getPool(), {
    accountId: acct,
    kind: q.kind ?? null,
    since: q.since ?? null,
    before: q.before ? Number(q.before) : null,
    limit: q.limit,
  });
  const page = rows.slice(0, q.limit);
  return { items: page.map(outRow), next_before: rows.length > q.limit ? (page[page.length - 1] as { id: string }).id : null };
}

export async function watchlist() {
  const a = getConfig().admin;
  return { items: await repo.watchlist(getPool(), a.watchlistWindowHours, a.watchlistMinScore, 20) };
}
