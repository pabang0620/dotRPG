// RunView(판 상태) 조립. 응답에는 uuid만 싣는다(내부 bigint 키 금지).
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { findRunById } from '../dungeons/dungeonRepository';
import type { PartyRunRow } from '../party/partyTx';
import * as repo from './partyRunRepository';
import { entryToken } from './runTokens';
import { memberLooks, type MemberLook } from '../antiabuse/memberView';

export interface TransportView {
  current: 'relay' | 'steam' | 'dev';
  epoch: number;
  order: ('relay' | 'steam' | 'dev')[];
  relay: { url: string };
}

/** 방(판·세션)의 전송 정보. 클라이언트는 current/epoch로 연결 방식을 정한다 */
export const transportView = (t: { transport: TransportView['current']; transport_epoch: number; transport_order: TransportView['order'] }): TransportView => ({
  current: t.transport,
  epoch: t.transport_epoch,
  order: t.transport_order,
  relay: { url: getConfig().relay.publicUrl },
});

export interface RunView {
  id: string;
  state: string;
  dungeon_id: string;
  difficulty: number;
  humans: number;
  ai_count: number;
  host: { character_id: string; steam_id: string | null; epoch: number };
  transport: TransportView;
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
  members: {
    character_id: string;
    name: string;
    class: string;
    level: number;
    slot: number;
    state: string;
    steam_id: string | null;
    /** 9단계: 멤버 카드 대조용 서버 값(장비 지문, 착용 장비 원본, 서버 기록 전직) */
    gear_hash: string;
    worn: MemberLook['worn'];
    career: number;
  }[];
}

export async function hostInfo(
  db: Queryable,
  run: PartyRunRow,
  members: repo.RunMemberRow[],
): Promise<{ character_id: string; steam_id: string | null; epoch: number }> {
  const host = members.find((m) => m.character_id === run.host_character_id);
  // Steam 계정이면 전송과 무관하게 항상 싣는다(클라이언트는 transport.current == steam 일 때만 쓴다)
  const steam = host ? (await repo.steamIdsOf(db, [host.account_id])).get(host.account_id) ?? null : null;
  return { character_id: host?.character_uuid ?? '', steam_id: steam, epoch: run.host_epoch };
}

export async function buildRunView(db: Queryable, run: PartyRunRow, meCharacterId: number): Promise<RunView> {
  const members = await repo.runMembers(db, run.id);
  const steam = await repo.steamIdsOf(db, members.map((m) => m.account_id));
  const looks = await memberLooks(db, members.map((m) => m.character_id));
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
    transport: transportView(run),
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
      steam_id: steam.get(m.account_id) ?? null,
      gear_hash: looks.get(m.character_id)?.gear_hash ?? '',
      worn: looks.get(m.character_id)?.worn ?? [],
      career: looks.get(m.character_id)?.career ?? 0,
    })),
  };
}
