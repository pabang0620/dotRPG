// `play-window-sweep`(1분): 게임시간 선택제(0039) 계정 중 접속 중인데 허용 구간 밖이 된 계정을 끊는다
import { query } from '../../db/pool';
import { isOpen } from '../../domains/auth/playWindow';
import { CLOSE } from '../../domains/chat/wsProtocol';
import { kickAccount, registry } from '../../domains/chat/realtimeNotifier';
import { relayHub } from '../../domains/relay/relayHub';
import type { JobResult } from '../jobRunner';

export async function playWindowSweepJob(): Promise<JobResult> {
  const ids = [...new Set(registry.all().map((s) => s.accountId))];
  if (ids.length === 0) return { rows: 0, detail: {} };
  const r = await query<{ id: string; play_window_start: number; play_window_end: number }>(
    'SELECT id, play_window_start, play_window_end FROM accounts WHERE id = ANY($1::bigint[]) AND play_window_start IS NOT NULL',
    [ids],
  );
  let kicked = 0;
  for (const a of r.rows) {
    if (isOpen({ start: a.play_window_start, end: a.play_window_end })) continue;
    const id = Number(a.id);
    kickAccount(id, CLOSE.KICKED, 'PLAY_WINDOW_CLOSED');
    relayHub().kickAccount(id, CLOSE.KICKED, 'PLAY_WINDOW_CLOSED');
    kicked++;
  }
  return { rows: kicked, detail: { checked: r.rows.length } };
}
