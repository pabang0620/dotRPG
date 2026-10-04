// 필드 세션 변화를 커밋 뒤에 알린다: /ws field.changed 힌트(멤버와 방금 나간 멤버)와 중계 방 동기화.
import type { PoolClient } from 'pg';
import { afterCommit, getPool } from '../../db/pool';
import { getNotifier } from '../chat/realtimeNotifier';
import { relayHub } from '../relay/relayHub';

export function notifyFieldChanged(client: PoolClient, sessionId: number): void {
  afterCommit(
    client,
    async () => {
      const hub = relayHub();
      const n = getNotifier();
      if (!n.active && !hub.hasRooms('field')) return;
      const s = await getPool().query<{ uuid: string }>('SELECT uuid FROM field_sessions WHERE id = $1', [sessionId]);
      const uuid = s.rows[0]?.uuid;
      if (!uuid) return;
      if (n.active) {
        const r = await getPool().query<{ character_id: string }>(
          `SELECT character_id FROM field_session_members
            WHERE session_id = $1 AND (state <> 'left' OR left_at >= now() - interval '10 seconds')`,
          [sessionId],
        );
        n.fieldChanged(r.rows.map((x) => Number(x.character_id)), uuid);
      }
      hub.resync('field', uuid);
    },
    `field:${sessionId}`,
  );
}
