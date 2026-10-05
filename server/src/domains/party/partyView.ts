// PartyView / PartyPost 조립. 응답에는 uuid만 싣는다.
import type { Queryable } from '../../db/pool';
import { registry } from '../chat/realtimeNotifier';
import { powerOf } from '../characters/powerEstimate';
import * as runRepo from '../partyruns/partyRunRepository';
import { buildRunView, type RunView } from '../partyruns/runView';
import * as repo from './partyRepository';

export interface PartyView {
  id: string;
  version: number;
  state: string;
  dungeon_id: string;
  difficulty: number;
  max_members: number;
  min_power: number;
  message: string;
  listed: boolean;
  listed_until: string | null;
  source: string;
  start_by: string | null;
  members: { character_id: string; name: string; class: string; level: number; power: number; ready: boolean; is_leader: boolean; is_me: boolean; online: boolean; map_id: string | null }[];
  applications: { id: string; character: { id: string; name: string; class: string; level: number; power: number }; expires_at: string }[];
  run: RunView | null;
}

export async function buildPartyView(db: Queryable, party: repo.PartyRow, meCharacterId: number, now: Date): Promise<PartyView> {
  const members = await repo.activeMembers(db, party.id);
  const isLeader = party.leader_character_id === meCharacterId;
  const view: PartyView = {
    id: party.uuid,
    version: party.version,
    state: party.state,
    dungeon_id: party.dungeon_id,
    difficulty: party.difficulty,
    max_members: party.max_members,
    min_power: party.min_power,
    message: party.message,
    listed: party.listed,
    listed_until: party.listed_until ? party.listed_until.toISOString() : null,
    source: party.source,
    start_by: party.start_by ? party.start_by.toISOString() : null,
    members: [],
    applications: [],
    run: null,
  };
  for (const m of members) {
    const leader = m.character_id === party.leader_character_id;
    view.members.push({
      character_id: m.character_uuid,
      name: m.name,
      class: m.class,
      level: m.level,
      power: await powerOf(db, m.character_id, m.class, m.level),
      ready: leader || m.ready,
      is_leader: leader,
      is_me: m.character_id === meCharacterId,
      // 채팅 접속 세션이 알려 준 지금 맵(접속 안 했거나 아직 안 알렸으면 null)
      online: registry.ofCharacter(m.character_id) !== undefined,
      map_id: registry.ofCharacter(m.character_id)?.mapId ?? null,
    });
  }
  if (isLeader) {
    for (const a of await repo.pendingForParty(db, party.id, now)) {
      view.applications.push({
        id: a.uuid,
        character: { id: a.char_uuid, name: a.name, class: a.class, level: a.level, power: a.power_estimate },
        expires_at: a.expires_at.toISOString(),
      });
    }
  }
  const run = await runRepo.activeRunOfParty(db, party.id);
  if (run) view.run = await buildRunView(db, run, meCharacterId);
  return view;
}
