import type { Queryable } from '../../db/pool';
import type { ClientErrorBody } from './clientErrorValidation';

export async function insert(db: Queryable, accountId: number, b: ClientErrorBody): Promise<void> {
  await db.query(
    `INSERT INTO client_errors (account_id, version, platform, message, stack, scene, client_at) VALUES ($1, $2, $3, $4, $5, $6, $7)`,
    [accountId, b.version, b.platform, b.message, b.stack, b.scene ?? null, b.at ?? null],
  );
}
