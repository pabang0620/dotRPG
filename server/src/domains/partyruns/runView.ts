// RunView(판 상태) 조립. 응답에는 uuid만 싣는다(내부 bigint 키 금지).
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { findRunById } from '../dungeons/dungeonRepository';
import type { PartyRunRow } from '../party/partyTx';
import * as repo from './partyRunRepository';
import { entryToken } from './runTokens';

export interface RunView {
  id: string;
  state: string;
  dungeon_id: string;
  difficulty: number;
  humans: number;
  ai_count: number;
  host: { character_id: string; steam_id: string | null; epoch: number };
  gather_deadline_at: string;
  begun_at: string | null;
  me: {
    slot: number;
    state: string;
    entry_token: string | null;
    run_id: string | null;
    reward_locked: boolean;
    lock_reason: string | null;
  } | null;
  members: { character_id: string; name: string; class: string; level: number; slot: number; state: string; steam_id: string | null }[];
}

export async function hostInfo(
  db: Queryable,
  run: PartyRunRow,
  members: repo.RunMemberRow[],
): Promise<{ character_id: string; steam_id: string | null; epoch: number }> {
  const host = members.find((m) => m.character_id === run.host_character_id);
  const steam = getConfig().partyTransport === 'steam' && host ? (await repo.steamIdsOf(db, [host.account_id])).get(host.account_id) ?? null : null;
  return { character_id: host?.character_uuid ?? '', steam_id: steam, epoch: run.host_epoch };
}

export async function buildRunView(db: Queryable, run: PartyRunRow, meCharacterId: number): Promise<RunView> {
  const members = await repo.runMembers(db, run.id);
  const steamOn = getConfig().partyTransport === 'steam';
  const steam = steamOn ? await repo.steamIdsOf(db, members.map((m) => m.account_id)) : new Map<number, string>();
  const me = members.find((m) => m.character_id === meCharacterId);
  let meView: RunView['me'] = null;
  if (me) {
    const active = repo.ACTIVE_STATES.includes(me.state);
    const dr = me.dungeon_run_id !== null ? await findRunById(db, me.dungeon_run_id) : null;
    meView = {
      slot: me.slot,
      state: me.state,
      entry_token: active && run.run_key ? entryToken(run.run_key, run.uuid, me.character_uuid, me.slot) : null,
      run_id: dr ? dr.uuid : null,
      reward_locked: dr ? dr.reward_locked : false,
      lock_reason: dr ? dr.lock_reason : null,
    };
  }
  return {
    id: run.uuid,
    state: run.state,
    dungeon_id: run.dungeon_id,
    difficulty: run.difficulty,
    humans: run.humans,
    ai_count: run.ai_count,
    host: await hostInfo(db, run, members),
    gather_deadline_at: run.gather_deadline_at.toISOString(),
    begun_at: run.begun_at ? run.begun_at.toISOString() : null,
    me: meView,
    members: members.map((m) => ({
      character_id: m.character_uuid,
      name: m.name,
      class: m.class,
      level: m.level,
      slot: m.slot,
      state: m.state,
      steam_id: steamOn ? (steam.get(m.account_id) ?? null) : null,
    })),
  };
}
