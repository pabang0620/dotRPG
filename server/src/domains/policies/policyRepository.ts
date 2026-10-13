// 정책 동의 SQL(account_consents)
import type { Queryable } from '../../db/pool';

export interface ConsentRow {
  doc_id: string;
  version: string;
}

export async function agreedOf(db: Queryable, accountId: number): Promise<ConsentRow[]> {
  const r = await db.query<ConsentRow>('SELECT doc_id, version FROM account_consents WHERE account_id = $1', [accountId]);
  return r.rows;
}

/** 계정 uuid로(동의 확인 미들웨어: 토큰 검증 전 단계라 uuid만 안다) */
export async function agreedOfUuid(db: Queryable, accountUuid: string): Promise<ConsentRow[]> {
  const r = await db.query<ConsentRow>(
    'SELECT c.doc_id, c.version FROM account_consents c JOIN accounts a ON a.id = c.account_id WHERE a.uuid = $1',
    [accountUuid],
  );
  return r.rows;
}

export async function insertConsents(db: Queryable, accountId: number, rows: ConsentRow[]): Promise<void> {
  for (const row of rows) {
    await db.query(
      'INSERT INTO account_consents (account_id, doc_id, version) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING',
      [accountId, row.doc_id, row.version],
    );
  }
}
