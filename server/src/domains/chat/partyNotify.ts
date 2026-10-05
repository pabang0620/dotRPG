// 4단계 파티·판 변화를 커밋 뒤에 WebSocket 힌트(party.changed)로 알린다. 접속 세션이 없으면 아무 조회도 하지 않는다.
import type { PoolClient } from 'pg';
import { afterCommit, getPool, type Queryable } from '../../db/pool';
import { relayHub } from '../relay/relayHub';
import { getNotifier } from './realtimeNotifier';

/** 파티 구성이 바뀌었다: 지금 멤버와 방금 나간 멤버(강퇴·해산 알림)에게 */
export function notifyPartyChanged(client: PoolClient, partyId: number, now: Date = new Date()): void {
  afterCommit(
    client,
    async () => {
      const n = getNotifier();
      if (!n.active) return;
      const r = await getPool().query<{ character_id: string }>(
        `SELECT DISTINCT character_id FROM party_members
          WHERE party_id = $1 AND (left_at IS NULL OR left_at >= $2::timestamptz - interval '10 seconds')`,
        [partyId, now],
      );
      n.partyChanged(r.rows.map((x) => Number(x.character_id)), 'party');
    },
    `party:${partyId}`,
  );
}

/** 판 상태가 바뀌었다: 판 멤버 전원에게 */
export function notifyRunChanged(db: Queryable, runId: number): void {
  const run = async (): Promise<void> => {
      const n = getNotifier();
      const hub = relayHub();
      if (!n.active && !hub.hasRooms('run')) return;
      const r = await getPool().query<{ uuid: string; character_id: string }>(
        `SELECT r.uuid, m.character_id FROM party_run_members m JOIN party_runs r ON r.id = m.party_run_id WHERE r.id = $1`,
        [runId],
      );
      let uuid = r.rows[0]?.uuid;
      if (!uuid) {
        const u = await getPool().query<{ uuid: string }>('SELECT uuid FROM party_runs WHERE id = $1', [runId]);
        uuid = u.rows[0]?.uuid;
      }
      if (!uuid) return;
      if (n.active) n.partyChanged(r.rows.map((x) => Number(x.character_id)), 'run', uuid);
      // 중계 방은 DB를 다시 읽어 맞춘다(멤버 이탈, 판 종료, 호스트 인계, 전송 전환)
      hub.resync('run', uuid);
  };
  // 트랜잭션 클라이언트면 커밋 뒤에, 풀(자동 커밋)이면 바로
  if (typeof (db as PoolClient).release === 'function') afterCommit(db as PoolClient, run, `run:${runId}`);
  else void run().catch(() => undefined);
}

/** 특정 캐릭터에게(신청 접수·거절 등) party 힌트 */
export function notifyCharacters(client: PoolClient, characterIds: number[]): void {
  afterCommit(client, () => getNotifier().partyChanged(characterIds, 'party'));
}

/** 파티원이 맵을 옮겼다(presence.set): 같은 파티의 다른 멤버에게 다시 불러오라고 알린다(위치 표시) */
export async function notifyPartyMatesOfMove(characterId: number): Promise<void> {
  const n = getNotifier();
  if (!n.active) return;
  const r = await getPool().query<{ character_id: string }>(
    `SELECT m2.character_id FROM party_members m1
       JOIN party_members m2 ON m2.party_id = m1.party_id AND m2.left_at IS NULL AND m2.character_id <> m1.character_id
      WHERE m1.character_id = $1 AND m1.left_at IS NULL`,
    [characterId],
  );
  if (r.rows.length > 0) n.partyChanged(r.rows.map((x) => Number(x.character_id)), 'party');
}
