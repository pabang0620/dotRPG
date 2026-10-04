// 4단계 쓰기 요청의 공통 틀: 캐릭터 행 잠금(필요하면 여러 명, id 오름차순) -> 멱등성 재생 -> 처리 -> 응답 저장.
// 락 순서: 캐릭터(오름차순 한 번에) -> parties -> party_runs. 골드·아이템·경험치는 여기서 바꾸지 않는다(그건 runEconomy).
import type { PoolClient } from 'pg';
import { REQUEST_LOG_UNIQUE, findRequest, hashRequest, saveRequest } from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import * as econRepo from '../economy/economyRepository';
import type { ApiBody, EconResult, StoredResult } from '../economy/economyService';

export interface PartyCtx {
  client: PoolClient;
  /** 요청한 캐릭터(잠김) */
  char: econRepo.LockedChar;
  /** 함께 잠근 캐릭터(요청자 포함, id -> 행) */
  chars: Map<number, econRepo.LockedChar>;
  requestId: string;
  now: Date;
}

export type LockMode =
  | { kind: 'self' }
  /** 내 파티의 활성 멤버 전원(출발) */
  | { kind: 'party' }
  /** 판(party_run_members)의 멤버 전원(입장 확인, 시작) */
  | { kind: 'run'; runUuid: string };

export interface PartyRunOptions {
  accountId: number;
  characterUuid: string;
  endpoint: string;
  requestId: string;
  payload: unknown;
  lock?: LockMode;
  handler: (ctx: PartyCtx) => Promise<EconResult>;
}

class RetryLock extends Error {}

async function ownId(client: PoolClient, accountId: number, uuid: string): Promise<number | null> {
  const r = await client.query<{ id: string }>(
    'SELECT id FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL',
    [uuid, accountId],
  );
  return r.rows[0] ? Number(r.rows[0].id) : null;
}

async function lockSet(client: PoolClient, me: number, mode: LockMode): Promise<number[]> {
  if (mode.kind === 'self') return [me];
  if (mode.kind === 'party') {
    const r = await client.query<{ character_id: string }>(
      `SELECT character_id FROM party_members
        WHERE left_at IS NULL AND party_id = (SELECT party_id FROM party_members WHERE character_id = $1 AND left_at IS NULL)`,
      [me],
    );
    const ids = r.rows.map((x) => Number(x.character_id));
    return ids.length > 0 ? ids.sort((a, b) => a - b) : [me];
  }
  const r = await client.query<{ character_id: string }>(
    `SELECT m.character_id FROM party_run_members m JOIN party_runs r ON r.id = m.party_run_id
      WHERE r.uuid = $1 ORDER BY m.character_id`,
    [mode.runUuid],
  );
  const ids = r.rows.map((x) => Number(x.character_id));
  if (!ids.includes(me)) throw new AppError(404, '판을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
  return ids;
}

/** 잠금 상자: 캐릭터 잠금(+명단 재확인) 뒤 fn을 실행한다. 명단이 바뀌었으면 처음부터 다시 한다 */
export async function withPartyLocks<T>(
  accountId: number,
  characterUuid: string,
  mode: LockMode,
  fn: (ctx: Omit<PartyCtx, 'requestId'>) => Promise<T>,
): Promise<T> {
  for (let attempt = 0; ; attempt++) {
    try {
      return await withTransaction(async (client) => {
        const me = await ownId(client, accountId, characterUuid);
        if (me === null) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
        const ids = await lockSet(client, me, mode);
        const locked = await econRepo.lockCharacters(client, ids);
        const mine = locked.find((c) => c.id === me);
        if (!mine) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
        if (mode.kind !== 'self') {
          // 잠그기 전에 읽은 명단이 그 사이 바뀌었으면 처음부터 다시(다른 순서로 더 잠그면 교착한다)
          const again = await lockSet(client, me, mode);
          if (again.length !== ids.length || again.some((v, i) => v !== ids[i])) throw new RetryLock();
        }
        return fn({ client, char: mine, chars: new Map(locked.map((c) => [c.id, c])), now: getNow() });
      });
    } catch (err) {
      if (err instanceof RetryLock && attempt < 3) continue;
      if (err instanceof RetryLock) throw new AppError(409, '파티 구성이 바뀌었습니다. 다시 시도해 주세요.', 'PARTY_CHANGED');
      throw err;
    }
  }
}

export async function runParty(o: PartyRunOptions): Promise<StoredResult> {
  const hash = hashRequest({ endpoint: o.endpoint, character: o.characterUuid, payload: o.payload });
  try {
    return await withPartyLocks(o.accountId, o.characterUuid, o.lock ?? { kind: 'self' }, async (base) => {
      const stored = await findRequest(base.client, o.accountId, o.requestId);
      if (stored) {
        if (stored.requestHash !== hash || stored.endpoint !== o.endpoint) {
          throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
        }
        return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
      }
      const r = await o.handler({ ...base, requestId: o.requestId });
      const body: ApiBody = { success: true, message: r.message ?? '', data: r.data };
      await saveRequest(base.client, o.accountId, o.requestId, o.endpoint, hash, r.status, body);
      return { status: r.status, body, replay: false };
    });
  } catch (err) {
    if (isUniqueViolation(err, REQUEST_LOG_UNIQUE)) {
      const stored = await findRequest(getPool(), o.accountId, o.requestId);
      if (stored && stored.requestHash === hash && stored.endpoint === o.endpoint) {
        return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
      }
    }
    throw err;
  }
}

/** parties -> party_runs 순으로 잠근다(4단계 락 순서). 판이 없으면 null */
export async function lockPartyAndRun(
  client: PoolClient,
  runUuid: string,
): Promise<{ partyId: number; run: PartyRunRow } | null> {
  const head = await client.query<{ party_id: string }>('SELECT party_id FROM party_runs WHERE uuid = $1', [runUuid]);
  if (!head.rows[0]) return null;
  const partyId = Number(head.rows[0].party_id);
  await client.query('SELECT id FROM parties WHERE id = $1 FOR UPDATE', [partyId]);
  const r = await client.query<RawPartyRun>(`SELECT ${RUN_COLS} FROM party_runs WHERE uuid = $1 FOR UPDATE`, [runUuid]);
  return r.rows[0] ? { partyId, run: toPartyRun(r.rows[0]) } : null;
}

export interface PartyRunRow {
  id: number;
  uuid: string;
  party_id: number;
  dungeon_id: string;
  difficulty: number;
  state: 'gathering' | 'playing' | 'ended' | 'cancelled';
  host_character_id: number;
  host_epoch: number;
  humans: number;
  ai_count: number;
  run_key: Buffer | null;
  power_cap: number | null;
  gather_deadline_at: Date;
  begun_at: Date | null;
  first_report_at: Date | null;
  /** 8단계: 전투 연결 방식 */
  transport: 'relay' | 'steam' | 'dev';
  transport_epoch: number;
  transport_switches: number;
  transport_order: ('relay' | 'steam' | 'dev')[];
}
interface RawPartyRun extends Omit<PartyRunRow, 'id' | 'party_id' | 'host_character_id' | 'power_cap'> {
  id: string;
  party_id: string;
  host_character_id: string;
  power_cap: string | null;
}
export const RUN_COLS = `id, uuid, party_id, dungeon_id, difficulty, state, host_character_id, host_epoch, humans, ai_count,
  run_key, power_cap, gather_deadline_at, begun_at, first_report_at, transport, transport_epoch, transport_switches, transport_order`;
export const toPartyRun = (r: RawPartyRun): PartyRunRow => ({
  ...r,
  id: Number(r.id),
  party_id: Number(r.party_id),
  host_character_id: Number(r.host_character_id),
  power_cap: r.power_cap === null ? null : Number(r.power_cap),
});
