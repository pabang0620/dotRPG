// 전투 전송 선택(phase8_api.md 5.1): 서버가 판·세션마다 정한다. 자격이 되는 후보만 남긴 우선순위가 그 방의 transport_order다.
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { registry } from '../chat/realtimeNotifier';
import { relayHub } from '../relay/relayHub';

export type Transport = 'relay' | 'steam' | 'dev';

async function steamLinked(db: Queryable, accountIds: number[]): Promise<Set<number>> {
  const r = await db.query<{ account_id: string }>(
    "SELECT account_id FROM auth_identities WHERE provider = 'steam' AND account_id = ANY($1::bigint[])",
    [accountIds],
  );
  return new Set(r.rows.map((x) => Number(x.account_id)));
}

/** 사람 멤버의 계정 id들로 자격이 되는 전송 우선순위를 만든다. 하나도 없으면 422 TRANSPORT_UNAVAILABLE */
export async function pickTransport(db: Queryable, accountIds: number[]): Promise<{ transport: Transport; order: Transport[] }> {
  const cfg = getConfig();
  const order: Transport[] = [];
  const linked = cfg.transport.order.includes('steam') && cfg.transport.steamP2pEnabled ? await steamLinked(db, accountIds) : new Set<number>();
  for (const t of cfg.transport.order) {
    if (t === 'relay') {
      if (cfg.relay.enabled && relayHub().admission().ok) order.push(t);
    } else if (t === 'steam') {
      if (!cfg.transport.steamP2pEnabled) continue;
      // 모든 사람 멤버가 Steam 연결이 있고, 접속 중인 /ws 세션이 steam_p2p 능력을 알렸어야 한다
      const ok = accountIds.length > 0 && accountIds.every((a) => linked.has(a) && registry.ofAccount(a)?.caps.steamP2p === true);
      if (ok) order.push(t);
    } else if (t === 'dev') {
      if (cfg.nodeEnv !== 'production') order.push(t);
    }
  }
  const first = order[0];
  if (!first) throw new AppError(422, '지금은 파티 전투 연결을 만들 수 없습니다.', 'TRANSPORT_UNAVAILABLE');
  return { transport: first, order };
}
