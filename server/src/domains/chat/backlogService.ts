// 놓친 메시지 따라잡기(phase5_api.md 3.6). 호출 전에 세션을 수신자 목록에 먼저 등록해 두어야 한다(경합 방지).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import * as repo from './chatRepository';
import type { ChatSession } from './chatSession';
import type { Frame } from './wsProtocol';

function toFrame(l: repo.StoredLine): Frame {
  const f: Frame = {
    t: 'chat.msg',
    seq: l.id,
    channel: l.channel,
    from: { id: l.sender_character_uuid, name: l.sender_name, ...(l.sender_title ? { title: l.sender_title } : {}) },
    text: l.text,
    at: l.created_at.toISOString(),
  };
  if (l.channel === 'whisper' && l.recipient_character_uuid && l.recipient_name) {
    f.to = { id: l.recipient_character_uuid, name: l.recipient_name };
  }
  return f;
}

export async function buildBacklog(
  s: ChatSession,
  since: number | null,
): Promise<{ lines: Frame[]; gap: boolean; cursor: number }> {
  const c = getConfig().social;
  const from = since ?? 0;
  const cursor = await repo.maxSeq(getPool());
  let gap = false;
  const take = (rows: repo.StoredLine[], limit: number): repo.StoredLine[] => {
    if (rows.length > limit) {
      if (since !== null) gap = true;
      return rows.slice(0, limit);
    }
    return rows;
  };
  const general = take(await repo.generalBacklog(s.shard, from, cursor, c.chatBacklogMinutes, c.chatBacklogGeneral), c.chatBacklogGeneral);
  const party = take(await repo.partyBacklog(s.characterId, from, cursor, c.chatBacklogParty), c.chatBacklogParty);
  const whisper = take(
    await repo.whisperBacklog(s.characterId, from, cursor, c.chatBacklogWhisperMinutes, c.chatBacklogWhisper),
    c.chatBacklogWhisper,
  );
  const lines = [...general, ...party, ...whisper]
    .filter((l) => l.sender_account_id !== s.accountId && !s.blocks.has(l.sender_account_id))
    .sort((a, b) => a.id - b.id)
    .map(toFrame);
  return { lines, gap, cursor };
}
