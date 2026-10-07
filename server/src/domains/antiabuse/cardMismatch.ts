// 멤버 카드 불일치 기록(9.3): 호스트가 멤버 카드(레벨·장비·직업)와 서버 뷰의 불일치를 봤다고 보고하면 대상 멤버 계정에 이상 기록을 남긴다.
// 위조 카드를 계속 보내는 멤버를 사후 추적하는 근거일 뿐이고, 판 결과에는 쓰지 않는다(호스트의 거짓 보고로 남을 몰아내는 것을 막는다).
import type { Queryable } from '../../db/pool';
import { insertAnomaly } from '../economy/economyRepository';

/** 같은 대상·같은 판(또는 세션)에서 한 번만 남긴다. 새로 남겼으면 true */
export async function recordCardMismatch(
  db: Queryable,
  target: { accountId: number; characterId: number; characterUuid: string },
  ref: { kind: 'run_id' | 'session_id'; id: string },
): Promise<boolean> {
  const seen = await db.query("SELECT 1 FROM anomaly_log WHERE kind = 'member_card' AND character_id = $1 AND detail->>'ref' = $2 LIMIT 1", [target.characterId, ref.id]);
  if (seen.rows.length > 0) return false;
  await insertAnomaly(db, target.accountId, target.characterId, 'member_card', 2, { ref: ref.id, [ref.kind]: ref.id, character_id: target.characterUuid });
  return true;
}
