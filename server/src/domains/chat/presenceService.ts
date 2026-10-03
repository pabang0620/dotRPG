// 접속 상태·위치: 접속 목록(메모리)과 친구 관계(DB)로 친구에게만 보인다
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import type { ChatSession } from './chatSession';
import { registry } from './realtimeNotifier';

export interface DisplayCharacter {
  id: string;
  name: string;
  class: 'warrior' | 'mage';
  level: number;
}
export interface FriendWhere {
  map_id: string;
  activity: 'field' | 'dungeon' | 'raid';
}
export interface PresenceEntry {
  id: string;
  online: boolean;
  character: DisplayCharacter;
  where: FriendWhere | null;
}

/**
 * 계정마다 보여 줄 캐릭터: 접속 중인 캐릭터, 아니면 last_character_id, 그것도 삭제됐으면 가장 오래된 살아 있는 캐릭터
 */
export async function displayCharacters(accountIds: number[]): Promise<Map<number, DisplayCharacter>> {
  const out = new Map<number, DisplayCharacter>();
  if (accountIds.length === 0) return out;
  const online = accountIds.map((a) => registry.ofAccount(a)?.characterId).filter((x): x is number => x !== undefined);
  const r = await getPool().query<{ account_id: string; uuid: string; name: string; class: 'warrior' | 'mage'; level: number }>(
    `SELECT DISTINCT ON (a.id) a.id AS account_id, c.uuid, c.name, c.class, c.level
       FROM accounts a JOIN characters c ON c.account_id = a.id AND c.deleted_at IS NULL
      WHERE a.id = ANY($1::bigint[])
      ORDER BY a.id, (c.id = ANY($2::bigint[])) DESC, (c.id = a.last_character_id) DESC, c.id`,
    [accountIds, online],
  );
  for (const x of r.rows) out.set(Number(x.account_id), { id: x.uuid, name: x.name, class: x.class, level: x.level });
  return out;
}

/** 던전·레이드 판에 들어 있는 캐릭터의 활동 */
async function runActivities(characterIds: number[]): Promise<Map<number, 'dungeon' | 'raid'>> {
  const out = new Map<number, 'dungeon' | 'raid'>();
  if (characterIds.length === 0) return out;
  const r = await getPool().query<{ character_id: string; dungeon_id: string }>(
    `SELECT m.character_id, r.dungeon_id FROM party_run_members m JOIN party_runs r ON r.id = m.party_run_id
      WHERE m.character_id = ANY($1::bigint[]) AND m.state IN ('invited', 'joined', 'playing', 'disconnected')
        AND r.state IN ('gathering', 'playing')`,
    [characterIds],
  );
  const dungeons = getGameData().economy.dungeons.byId;
  for (const x of r.rows) out.set(Number(x.character_id), dungeons.get(x.dungeon_id)?.isRaid ? 'raid' : 'dungeon');
  return out;
}

export interface FriendLink {
  friendshipUuid: string;
  otherAccountId: number;
  since: Date | null;
}

export async function friendLinks(accountId: number): Promise<FriendLink[]> {
  const r = await getPool().query<{ uuid: string; other: string; responded_at: Date | null }>(
    `SELECT uuid, CASE WHEN requester_account_id = $1 THEN target_account_id ELSE requester_account_id END AS other, responded_at
       FROM friendships WHERE state = 'accepted' AND (requester_account_id = $1 OR target_account_id = $1)`,
    [accountId],
  );
  return r.rows.map((x) => ({ friendshipUuid: x.uuid, otherAccountId: Number(x.other), since: x.responded_at }));
}

/** 친구 목록 표시용(GET /friends, friends.presence) */
export async function presenceEntries(links: FriendLink[]): Promise<(PresenceEntry & { since: Date | null })[]> {
  const chars = await displayCharacters(links.map((l) => l.otherAccountId));
  const sessions = links.map((l) => registry.ofAccount(l.otherAccountId));
  const acts = await runActivities(sessions.filter((s): s is ChatSession => !!s).map((s) => s.characterId));
  const out: (PresenceEntry & { since: Date | null })[] = [];
  links.forEach((l, i) => {
    const character = chars.get(l.otherAccountId);
    if (!character) return;
    const s = sessions[i];
    let where: FriendWhere | null = null;
    if (s && s.mapId) where = { map_id: s.mapId, activity: acts.get(s.characterId) ?? 'field' };
    out.push({ id: l.friendshipUuid, online: !!s, character, where, since: l.since });
  });
  return out;
}

/** 내 접속·종료·맵 이동을 접속 중인 친구에게 알린다(친구마다 그 관계의 id로) */
export async function pushMyPresence(accountId: number): Promise<void> {
  const links = await friendLinks(accountId);
  const online = links.filter((l) => registry.ofAccount(l.otherAccountId));
  if (online.length === 0) return;
  const me = (await displayCharacters([accountId])).get(accountId);
  if (!me) return;
  const mine = registry.ofAccount(accountId);
  const acts = mine ? await runActivities([mine.characterId]) : new Map<number, 'dungeon' | 'raid'>();
  for (const l of online) {
    const where: FriendWhere | null = mine && mine.mapId ? { map_id: mine.mapId, activity: acts.get(mine.characterId) ?? 'field' } : null;
    const entry: PresenceEntry = { id: l.friendshipUuid, online: !!mine, character: me, where };
    registry.ofAccount(l.otherAccountId)?.send({ t: 'friends.presence', list: [entry] });
  }
}

export async function sendAllPresence(s: ChatSession): Promise<void> {
  const entries = await presenceEntries(await friendLinks(s.accountId));
  s.sendNow({ t: 'friends.presence', list: entries.map(({ since: _s, ...e }) => e) });
}
