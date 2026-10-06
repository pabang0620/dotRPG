// 사람 키 조회(5.1): 계정의 현재 세션 기기와 Steam 소유자. 파티 판 시작 때 스냅샷으로 쓴다.
import type { Queryable } from '../../db/pool';
import type { HumanKey } from './contribution';

/** 계정 id -> { deviceHash(HMAC 기기 키, device_hash가 없으면 install_id 대체 키), installId, steamKey = COALESCE(steam_owner_id, steam subject) } */
export async function humanKeysOf(db: Queryable, accountIds: number[]): Promise<Map<number, HumanKey>> {
  if (accountIds.length === 0) return new Map();
  const r = await db.query<{ id: string; device: string | null; install: string | null; steam: string | null }>(
    `SELECT a.id, a.active_device_hash AS device, a.active_install_id AS install,
            (SELECT COALESCE(i.steam_owner_id, i.subject) FROM auth_identities i WHERE i.account_id = a.id AND i.provider = 'steam') AS steam
       FROM accounts a WHERE a.id = ANY($1::bigint[])`,
    [accountIds],
  );
  return new Map(r.rows.map((x) => [Number(x.id), { deviceHash: x.device, steamKey: x.steam, installId: x.install }] as const));
}

/** 판 시작 스냅샷을 party_run_members에 쓴다 */
export async function snapshotRunHumans(db: Queryable, partyRunId: number, rows: { characterId: number; accountId: number }[]): Promise<Map<number, HumanKey>> {
  const keys = await humanKeysOf(db, rows.map((r) => r.accountId));
  const out = new Map<number, HumanKey>();
  for (const r of rows) {
    const k = keys.get(r.accountId) ?? { deviceHash: null, steamKey: null, installId: null };
    out.set(r.characterId, k);
    await db.query('UPDATE party_run_members SET device_hash = $3, steam_key = $4, install_id = $5 WHERE party_run_id = $1 AND character_id = $2', [partyRunId, r.characterId, k.deviceHash, k.steamKey, k.installId ?? null]);
  }
  return out;
}
