// 확률표 공개 기록(0040) SQL
import type { Queryable } from '../../db/pool';

export interface PublicationListRow {
  id: string;
  fingerprint: string;
  versions: Record<string, string>;
  first_seen_at: string;
  last_seen_at: string;
}
export interface PublicationRow extends PublicationListRow {
  content: string;
}

/** 같은 지문이면 last_seen_at 만 갱신한다. 새 지문이면 true */
export async function upsertPublication(db: Queryable, fingerprint: string, versions: Record<string, string>, content: string): Promise<boolean> {
  const r = await db.query<{ inserted: boolean }>(
    `INSERT INTO rates_publications (fingerprint, versions, content) VALUES ($1, $2::jsonb, $3)
     ON CONFLICT (fingerprint) DO UPDATE SET last_seen_at = now()
     RETURNING (xmax = 0) AS inserted`,
    [fingerprint, JSON.stringify(versions), content],
  );
  return r.rows[0]?.inserted ?? false;
}

export async function listPublications(db: Queryable, limit: number, before: number | null): Promise<PublicationListRow[]> {
  const r = await db.query<PublicationListRow>(
    `SELECT id::text AS id, fingerprint, versions, first_seen_at, last_seen_at
       FROM rates_publications
      WHERE ($2::bigint IS NULL OR id < $2)
      ORDER BY id DESC
      LIMIT $1`,
    [limit + 1, before],
  );
  return r.rows;
}

export async function getPublication(db: Queryable, id: number): Promise<PublicationRow | null> {
  const r = await db.query<PublicationRow>(
    'SELECT id::text AS id, fingerprint, versions, content, first_seen_at, last_seen_at FROM rates_publications WHERE id = $1',
    [id],
  );
  return r.rows[0] ?? null;
}
