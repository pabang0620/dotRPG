// 필드 세션 상태 전이의 공통 부분. 호출 쪽이 세션 행을 FOR UPDATE로 잠근 트랜잭션 안에서만 부른다.
// 락 순서: 캐릭터 -> parties -> party_runs -> field_sessions (field_session_members 는 세션 행 아래에서만 바꾼다).
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import * as repo from './fieldRepository';
import { electHost, type Candidate } from './hostElection';
import { notifyFieldChanged } from './fieldNotify';

export const MAX_SEATS = 4;

/** 활성 멤버를 선출 후보로 바꾼다. freshSelf는 방금 신호를 보낸 사람(항상 신선) */
export async function candidatesOf(
  client: PoolClient,
  session: repo.SessionRow,
  members: repo.FMemberRow[],
  now: Date,
  freshSelf: number | null = null,
): Promise<Candidate[]> {
  const { leaderId, order } = await repo.partyOrder(client, session.party_id);
  const stale = getConfig().policy.hostStaleSeconds * 1000;
  return members.map((m) => ({
    characterId: m.character_id,
    seat: m.seat,
    state: m.state,
    isLeader: m.character_id === leaderId,
    partyJoinedAt: order.get(m.character_id) ?? Number.MAX_SAFE_INTEGER,
    fresh: m.character_id === freshSelf || (m.last_seen_at !== null && now.getTime() - m.last_seen_at.getTime() <= stale),
  }));
}

/** 호스트를 다시 뽑는다. 후보가 없으면 NULL. 세대(host_epoch)를 올리고 새 호스트 id를 돌려준다 */
export async function handoff(
  client: PoolClient,
  session: repo.SessionRow,
  members: repo.FMemberRow[],
  now: Date,
  opts: { exclude?: number | null; freshSelf?: number | null } = {},
): Promise<number | null> {
  const cands = await candidatesOf(client, session, members, now, opts.freshSelf ?? null);
  const next = electHost(cands, opts.exclude ?? null);
  // 같은 사람이 이어서 호스트면 세대를 올리지 않는다(고정 호스트)
  if (next !== session.host_character_id) await repo.setHost(client, session.id, next, now);
  return next;
}

const END_REASON: Record<string, repo.FieldEndReason> = { party_closed: 'party_closed', stale: 'stale' };

/** 멤버 한 명을 내보낸다. 호스트였으면 인계, 마지막이면 세션 종료. 변경은 커밋 뒤 알린다 */
export async function closeMember(
  client: PoolClient,
  session: repo.SessionRow,
  characterId: number,
  reason: repo.FieldLeftReason,
  now: Date,
): Promise<{ ended: boolean }> {
  const members = await repo.activeMembers(client, session.id);
  if (!members.some((m) => m.character_id === characterId)) return { ended: false };
  await repo.leaveMember(client, session.id, characterId, reason, now);
  const rest = members.filter((m) => m.character_id !== characterId);
  if (rest.length === 0) {
    await repo.endSession(client, session.id, END_REASON[reason] ?? 'empty', now);
    notifyFieldChanged(client, session.id);
    return { ended: true };
  }
  if (session.host_character_id === characterId) await handoff(client, session, rest, now, { exclude: characterId });
  await repo.bump(client, session.id, now);
  notifyFieldChanged(client, session.id);
  return { ended: false };
}

/** 세션 전체를 끝낸다(파티 해산, 방치) */
export async function endAll(client: PoolClient, session: repo.SessionRow, reason: 'party_closed' | 'stale', now: Date): Promise<void> {
  const members = await repo.activeMembers(client, session.id);
  for (const m of members) await repo.leaveMember(client, session.id, m.character_id, reason, now);
  await repo.endSession(client, session.id, reason, now);
  notifyFieldChanged(client, session.id);
}

/** 한 캐릭터의 활성 세션 멤버십을 닫는다(파티 나가기·강퇴, 던전 출발, 다른 세션 입장). 닫았으면 true */
export async function closeMembershipOf(client: PoolClient, characterId: number, reason: repo.FieldLeftReason, now: Date): Promise<boolean> {
  const sid = await repo.activeSessionIdOf(client, characterId);
  if (sid === null) return false;
  const session = await repo.lockById(client, sid);
  if (!session || session.state !== 'active') return false;
  await closeMember(client, session, characterId, reason, now);
  return true;
}

/** 파티가 해산됐다: 그 파티의 활성 세션을 모두 끝낸다 */
export async function closeSessionsOfParty(client: PoolClient, partyId: number, now: Date): Promise<void> {
  const ids = await repo.activeSessionsOfParty(client, partyId);
  for (const s of await repo.lockMany(client, ids)) if (s.state === 'active') await endAll(client, s, 'party_closed', now);
}

/**
 * 다른 멤버의 지연 전이(하트비트 정체 12초 -> disconnected, 끊긴 뒤 60초 -> left)와 호스트 상실 인계.
 * exceptId는 요청자(방금 신호를 보냈다). 바뀐 것이 있으면 true
 */
export async function sweepMembers(
  client: PoolClient,
  session: repo.SessionRow,
  now: Date,
  exceptId: number | null,
): Promise<{ changed: boolean; hostChanged: boolean }> {
  const pol = getConfig().policy;
  let members = await repo.activeMembers(client, session.id);
  let changed = false;
  for (const m of members) {
    if (m.character_id === exceptId) continue;
    if ((m.state === 'playing' || m.state === 'joined') && m.last_seen_at && now.getTime() - m.last_seen_at.getTime() > pol.hostStaleSeconds * 1000) {
      await repo.setMemberState(client, session.id, m.character_id, 'disconnected', now);
      changed = true;
    } else if (m.state === 'disconnected' && m.disconnected_at && now.getTime() - m.disconnected_at.getTime() > pol.partyRejoinSeconds * 1000) {
      await repo.leaveMember(client, session.id, m.character_id, 'rejoin_timeout', now);
      changed = true;
    }
  }
  let hostChanged = false;
  if (changed) members = await repo.activeMembers(client, session.id);
  const host = members.find((m) => m.character_id === session.host_character_id);
  const hostDead = !host || host.state === 'disconnected';
  if (hostDead && members.some((m) => m.character_id !== session.host_character_id && m.state !== 'disconnected')) {
    await handoff(client, session, members, now, { exclude: session.host_character_id, freshSelf: exceptId });
    hostChanged = true;
    changed = true;
  } else if (session.host_character_id === null) {
    const next = await handoff(client, session, members, now, { freshSelf: exceptId });
    hostChanged = next !== session.host_character_id;
    changed = changed || hostChanged;
  }
  return { changed, hostChanged };
}

/** 가장 낮은 빈 좌석(0..3), 모두 차면 null */
export function freeSeat(members: repo.FMemberRow[]): number | null {
  const used = new Set(members.map((m) => m.seat));
  for (let s = 0; s < MAX_SEATS; s++) if (!used.has(s)) return s;
  return null;
}
