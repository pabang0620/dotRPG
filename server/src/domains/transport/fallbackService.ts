// 전송 전환 T2(phase8_api.md 5.3): 연결 실패를 보고하면 서버가 transport_order의 다음 후보로 바꾼다.
// 한 멤버의 실패가 방 전체를 옮기고, 중계로 전환한 방은 Steam으로 자동 복귀하지 않는다.
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';
import { relayMetrics } from '../relay/relayMetrics';
import { relayHub } from '../relay/relayHub';
import type { RoomKind } from '../relay/relayTicket';
import { notifyFieldChanged } from '../fieldsessions/fieldNotify';
import * as fieldRepo from '../fieldsessions/fieldRepository';
import { notifyRunChanged } from '../chat/partyNotify';
import type { StoredResult } from '../economy/economyService';
import { lockPartyAndRun, runParty, type PartyCtx } from '../party/partyTx';
import * as runRepo from '../partyruns/partyRunRepository';
import { transportView, type TransportView } from '../partyruns/runView';
import type { Transport } from './pickTransport';

export type SwitchReason = 'connect_timeout' | 'connect_failed' | 'repeated_drop' | 'host_unreachable';

export interface FallbackInput {
  requestId: string;
  /** 클라이언트가 본 전송 세대 */
  epoch: number;
  /** 실패한 전송 이름(주면 서버의 현재 전송과 대조한다) */
  failed?: Transport;
  reason: SwitchReason;
}

/** 마지막 전환 시각(메모리, 프로세스 하나 전제). 쿨다운 판정에만 쓴다 */
const lastSwitch = new Map<string, number>();
/** 멤버별 전환 시도 수(방 + 캐릭터). 한 멤버가 한 방에서 MEMBER_SWITCH_MAX 번까지만 */
const memberTries = new Map<string, { n: number; at: number }>();
const MEMBER_SWITCH_MAX = 2;
const TRIES_KEEP_MS = 6 * 3_600_000;
export const resetSwitchCooldowns = (): void => {
  lastSwitch.clear();
  memberTries.clear();
};
/** 테스트·진단용 */
export const switchMapSizes = (): { cooldowns: number; members: number } => ({ cooldowns: lastSwitch.size, members: memberTries.size });

/** 쿨다운이 지난 항목, 오래된 멤버 카운터, 닫힌 방의 항목을 지운다(메모리가 방 수만큼 늘지 않게) */
function prune(cooldownMs: number, closedRoom?: string): void {
  const now = Date.now();
  for (const [k, at] of lastSwitch) if (now - at >= cooldownMs || (closedRoom && k === closedRoom)) lastSwitch.delete(k);
  for (const [k, v] of memberTries) if (now - v.at > TRIES_KEEP_MS || (closedRoom && k.startsWith(`${closedRoom}|`))) memberTries.delete(k);
}

interface RoomState {
  transport: Transport;
  epoch: number;
  switches: number;
  order: Transport[];
  hostAlive: boolean;
  view: () => TransportView;
}

const notFound = () => new AppError(404, '방을 찾을 수 없습니다.', 'ROOM_NOT_FOUND');
const closed = (key: string) => {
  prune(getConfig().transport.switchCooldownSeconds * 1000, key);
  return new AppError(409, '끝난 방입니다.', 'ROOM_CLOSED');
};

export function fallbackTransport(accountId: number, characterUuid: string, kind: RoomKind, roomUuid: string, input: FallbackInput): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: `POST /characters/:uuid/rooms/${kind}/:id/transport`,
    requestId: input.requestId,
    payload: { kind, room_id: roomUuid, epoch: input.epoch, failed: input.failed ?? null, reason: input.reason },
    handler: (ctx) => process(ctx, kind, roomUuid, input),
  });
}

async function process(ctx: PartyCtx, kind: RoomKind, roomUuid: string, input: FallbackInput) {
  const cfg = getConfig();
  const { client, char: me, now } = ctx;
  let st: RoomState;
  let apply: (next: Transport, epoch: number, switches: number) => Promise<void>;
  if (kind === 'run') {
    const locked = await lockPartyAndRun(client, roomUuid);
    if (!locked) throw notFound();
    const run = locked.run;
    const members = await runRepo.runMembers(client, run.id);
    const mine = members.find((m) => m.character_id === me.id);
    if (!mine) throw notFound();
    if (run.state === 'ended' || run.state === 'cancelled') throw closed(`${kind}:${roomUuid}`);
    if (!runRepo.ACTIVE_STATES.includes(mine.state)) throw notFound();
    const host = members.find((m) => m.character_id === run.host_character_id);
    const pres = relayHub().hostPresence('run', run.uuid);
    const hb = !!host && host.state === 'playing' && !!host.last_seen_at && now.getTime() - host.last_seen_at.getTime() <= cfg.policy.hostStaleSeconds * 1000;
    st = { transport: run.transport, epoch: run.transport_epoch, switches: run.transport_switches, order: run.transport_order, hostAlive: pres ? pres.connected : hb, view: () => transportView(run) };
    apply = async (next, epoch, switches) => {
      await client.query('UPDATE party_runs SET transport = $2, transport_epoch = $3, transport_switches = $4 WHERE id = $1', [run.id, next, epoch, switches]);
      notifyRunChanged(client, run.id);
    };
  } else {
    const session = await fieldRepo.lockByUuid(client, roomUuid);
    const row = session ? await fieldRepo.memberRow(client, session.id, me.id) : null;
    if (!session || !row) throw notFound();
    if (session.state === 'ended') throw closed(`${kind}:${roomUuid}`);
    if (row.state === 'left') throw notFound();
    const members = await fieldRepo.activeMembers(client, session.id);
    const host = members.find((m) => m.character_id === session.host_character_id);
    const pres = relayHub().hostPresence('field', session.uuid);
    const hb = !!host && host.state === 'playing' && !!host.last_seen_at && now.getTime() - host.last_seen_at.getTime() <= cfg.policy.hostStaleSeconds * 1000;
    st = { transport: session.transport, epoch: session.transport_epoch, switches: session.transport_switches, order: session.transport_order, hostAlive: pres ? pres.connected : hb, view: () => transportView(session) };
    apply = async (next, epoch, switches) => {
      await fieldRepo.setTransport(client, session.id, next, epoch, switches);
      notifyFieldChanged(client, session.id);
    };
  }
  const current = { transport: st.transport, epoch: st.epoch };
  // 이미 누가 바꿨다: 클라이언트는 현재 전송으로 간다
  if (input.epoch !== st.epoch || (input.failed !== undefined && input.failed !== st.transport)) {
    throw new AppError(409, '이미 전송이 바뀌었습니다.', 'TRANSPORT_CHANGED', { current });
  }
  if (input.reason === 'host_unreachable' && !st.hostAlive) {
    throw new AppError(409, '호스트가 서버 기준으로 살아 있지 않습니다.', 'HOST_NOT_ALIVE', { current });
  }
  const key = `${kind}:${roomUuid}`;
  prune(cfg.transport.switchCooldownSeconds * 1000);
  // 한 멤버가 한 방에서 전환을 계속 두드리는 것을 막는다
  const mk = `${key}|${me.id}`;
  const tries = memberTries.get(mk) ?? { n: 0, at: 0 };
  if (tries.n >= MEMBER_SWITCH_MAX) throw new AppError(429, '이 방에서 더 이상 전환을 요청할 수 없습니다.', 'TRANSPORT_SWITCH_LIMIT', { current });
  memberTries.set(mk, { n: tries.n + 1, at: Date.now() });
  const last = lastSwitch.get(key);
  if (last !== undefined && Date.now() - last < cfg.transport.switchCooldownSeconds * 1000) {
    throw new AppError(429, '전송 전환이 너무 잦습니다.', 'TRANSPORT_SWITCH_COOLDOWN', { retry_after_sec: Math.max(1, Math.ceil((cfg.transport.switchCooldownSeconds * 1000 - (Date.now() - last)) / 1000)), current });
  }
  const idx = st.order.indexOf(st.transport);
  const next = st.switches >= 4 ? undefined : st.order[idx + 1];
  if (!next) throw new AppError(409, '연결할 수 있는 전송이 더 없습니다.', 'TRANSPORT_EXHAUSTED', { current });
  await apply(next, st.epoch + 1, st.switches + 1);
  lastSwitch.set(key, Date.now());
  const from = st.transport;
  relayMetrics.fallbacks.add();
  if (from === 'steam' && next === 'relay') relayMetrics.steamToRelay.add();
  const view = { ...st.view(), current: next, epoch: st.epoch + 1 };
  return { status: 200, data: { transport: view } };
}
