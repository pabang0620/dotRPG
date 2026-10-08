// 탈퇴 요청(T0)의 정리: 파티·초대·필드 세션·솔로 던전 판·친구·매칭 대기열·이름.
// 새 규칙을 만들지 않고 기존 이탈 규칙(partyRepository, partyRunRepository, fieldCore, dungeonRepository)을 그대로 쓴다.
// 호출 쪽이 이미 캐릭터 행(id 오름차순) -> 계정 행을 잠갔다(락 순서).
import { randomBytes } from 'node:crypto';
import type { PoolClient } from 'pg';
import { afterCommit } from '../../db/pool';
import { closeMembershipOf } from '../fieldsessions/fieldCore';
import { getQueueStore } from '../match/queueStore';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import * as partyRepo from '../party/partyRepository';
import { lockPartyAndRun } from '../party/partyTx';
import * as runRepo from '../partyruns/partyRunRepository';
import { abandonMemberRun } from '../partyruns/partyRunLiveService';
import { setPartyState } from '../partyruns/partyRunRepository';
import * as repo from './withdrawalRepository';

/** 이름 자리표시: '탈퇴' + 캐릭터 uuid 의 16진수 6자(8자, 이름 규칙 ^[가-힣A-Za-z0-9]{2,8}$ 만족) */
export const PLACEHOLDER_RE = /^탈퇴[0-9a-f]{6}$/;
export const placeholderOf = (uuid: string, attempt: number): string => {
  const hex = uuid.replace(/-/g, '');
  const start = attempt * 6;
  return `탈퇴${start + 6 <= hex.length ? hex.slice(start, start + 6) : randomBytes(3).toString('hex')}`;
};

const isUnique = (err: unknown): boolean => (err as { code?: string } | null)?.code === '23505';

async function tryRename(client: PoolClient, characterId: number, candidates: () => Generator<string>): Promise<string> {
  for (const name of candidates()) {
    await client.query('SAVEPOINT rename_try');
    try {
      await repo.renameCharacter(client, characterId, name);
      await client.query('RELEASE SAVEPOINT rename_try');
      return name;
    } catch (err) {
      await client.query('ROLLBACK TO SAVEPOINT rename_try');
      await client.query('RELEASE SAVEPOINT rename_try');
      if (!isUnique(err)) throw err;
    }
  }
  throw new Error('이름을 바꿀 수 없습니다');
}

/** 살아 있는 캐릭터를 자리표시 이름으로 바꾼다. 원래 이름 {uuid: name}을 돌려준다 */
export async function renameToPlaceholders(client: PoolClient, chars: { id: number; uuid: string; name: string }[]): Promise<Record<string, string>> {
  const saved: Record<string, string> = {};
  for (const c of chars) {
    if (PLACEHOLDER_RE.test(c.name)) continue;
    saved[c.uuid] = c.name;
    await tryRename(client, c.id, function* () {
      for (let i = 0; i < 5; i++) yield placeholderOf(c.uuid, i);
      for (let i = 0; i < 20; i++) yield `탈퇴${randomBytes(3).toString('hex')}`;
    });
  }
  return saved;
}

/** 철회: 원래 이름을 되돌린다. 그 사이 다른 사람이 가져갔으면 앞 6자 + 숫자 2자리로 복원한다. 접미사로 복원한 수를 돌려준다 */
export async function restoreNames(client: PoolClient, chars: { id: number; uuid: string }[], saved: Record<string, string> | null): Promise<number> {
  if (!saved) return 0;
  let renamed = 0;
  for (const c of chars) {
    const original = saved[c.uuid];
    if (!original) continue;
    await client.query('SAVEPOINT restore_try');
    try {
      await repo.renameCharacter(client, c.id, original);
      await client.query('RELEASE SAVEPOINT restore_try');
      continue;
    } catch (err) {
      await client.query('ROLLBACK TO SAVEPOINT restore_try');
      await client.query('RELEASE SAVEPOINT restore_try');
      if (!isUnique(err)) throw err;
    }
    renamed++;
    const stem = Array.from(original).slice(0, 6).join('');
    await tryRename(client, c.id, function* () {
      for (let i = 0; i < 100; i++) yield `${stem}${String(Math.floor(Math.random() * 100)).padStart(2, '0')}`;
      for (let i = 0; i < 20; i++) yield `${stem.slice(0, 2)}${randomBytes(3).toString('hex')}`;
    });
  }
  return renamed;
}

/** 캐릭터 한 명의 파티 판·파티·필드 세션·솔로 던전 판·초대·신청을 닫는다 */
async function closeCharacter(client: PoolClient, characterId: number, now: Date): Promise<void> {
  // 1. 신청·초대(보낸 것·받은 것) 취소. 초대는 조용히(silent)
  await client.query("UPDATE party_applications SET state = 'cancelled', responded_at = $2 WHERE character_id = $1 AND state = 'pending'", [characterId, now]);
  await client.query(
    "UPDATE party_invites SET state = 'cancelled', silent = true WHERE (inviter_character_id = $1 OR invitee_character_id = $1) AND state = 'pending'",
    [characterId],
  );
  // 2. 진행 중 파티 판: 기존 이탈(R8) 규칙. 판 -> 파티 순으로 잠그는 lockPartyAndRun 을 쓴다
  const runs = await client.query<{ uuid: string }>(
    `SELECT r.uuid FROM party_run_members m JOIN party_runs r ON r.id = m.party_run_id
      WHERE m.character_id = $1 AND m.state IN ('invited', 'joined', 'playing', 'disconnected') ORDER BY r.id`,
    [characterId],
  );
  for (const row of runs.rows) {
    const locked = await lockPartyAndRun(client, row.uuid);
    if (!locked) continue;
    const run = locked.run;
    const members = await runRepo.runMembers(client, run.id);
    const me = members.find((m) => m.character_id === characterId);
    if (!me || !runRepo.ACTIVE_STATES.includes(me.state)) continue;
    if (run.state === 'gathering') {
      if (run.host_character_id === characterId) {
        await runRepo.cancelRun(client, run.id, 'host_cancel', now);
        await setPartyState(client, run.party_id, 'forming');
      } else {
        await runRepo.setMemberState(client, run.id, characterId, 'left', now, { leftReason: 'left' });
      }
    } else if (run.state === 'playing') {
      await runRepo.setMemberState(client, run.id, characterId, 'left', now, { leftReason: 'left' });
      await abandonMemberRun(client, run.id, me, now);
      await runRepo.endRunIfDone(client, run, now);
    } else {
      await runRepo.setMemberState(client, run.id, characterId, 'left', now, { leftReason: 'left' });
    }
  }
  // 3. 솔로 던전 판: 방치 규칙과 같은 abandoned(보상 없음)
  const solo = await client.query<{ id: string }>("SELECT id FROM dungeon_runs WHERE character_id = $1 AND state = 'playing' ORDER BY id", [characterId]);
  for (const r of solo.rows) await dungeonRepo.abandonRun(client, Number(r.id), now);
  // 4. 파티: 나가기(방장이면 가장 오래된 멤버가 이어받고, 혼자면 해산). 필드 세션 멤버십도 함께 닫힌다
  const partyId = await partyRepo.findPartyIdOf(client, characterId);
  if (partyId !== null) {
    const party = await partyRepo.lockParty(client, partyId);
    if (party && party.state !== 'closed') {
      const members = await partyRepo.activeMembers(client, party.id);
      const rest = members.filter((m) => m.character_id !== characterId);
      await partyRepo.leaveMember(client, party.id, characterId, 'left', now);
      if (rest.length === 0) {
        await partyRepo.closeParty(client, party.id, 'disbanded', now);
      } else {
        const leaderId = party.leader_character_id === characterId ? (rest[0] as (typeof rest)[number]).character_id : party.leader_character_id;
        if (leaderId !== party.leader_character_id) await partyRepo.setLeader(client, party.id, leaderId);
        await partyRepo.resetReady(client, party.id, leaderId);
        await partyRepo.bump(client, party.id, now);
      }
    }
  }
  // 5. 파티 없이 남은 필드 세션 멤버십(있다면)
  await closeMembershipOf(client, characterId, 'left', now);
  // 6. 매칭 대기열(메모리): 커밋 뒤 제거
  afterCommit(client, () => getQueueStore().remove(characterId), `withdraw-queue:${characterId}`);
}

/** 계정의 친구 관계를 모두 끝낸다: accepted -> removed, pending -> cancelled, 조용히 */
async function endFriendships(client: PoolClient, accountId: number, now: Date): Promise<void> {
  await client.query(
    `UPDATE friendships SET state = CASE WHEN state = 'accepted' THEN 'removed' ELSE 'cancelled' END,
            ended_at = $2, ended_by_account_id = $1, silent = true
      WHERE state IN ('pending', 'accepted') AND (requester_account_id = $1 OR target_account_id = $1)`,
    [accountId, now],
  );
}

/** T0 정리 전체(5.3 표). 캐릭터는 id 오름차순으로 처리한다 */
export async function closeAccountActivity(client: PoolClient, accountId: number, characterIds: number[], now: Date): Promise<void> {
  for (const id of [...characterIds].sort((a, b) => a - b)) await closeCharacter(client, id, now);
  await endFriendships(client, accountId, now);
}
