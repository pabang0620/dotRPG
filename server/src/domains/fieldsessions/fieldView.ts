// FieldSessionView 조립(phase8_api.md 6.4). 응답에는 uuid만 싣는다(내부 bigint 키 금지).
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { entryToken } from '../partyruns/runTokens';
import { transportView, type TransportView } from '../partyruns/runView';
import * as repo from './fieldRepository';
import { memberLooks, type MemberLook } from '../antiabuse/memberView';

export interface FieldSessionView {
  id: string;
  version: number;
  map_id: string;
  state: 'active' | 'ended';
  host: { character_id: string; seat: number; steam_id: string | null; epoch: number } | null;
  transport: TransportView;
  me: { seat: number; state: string; entry_token: string | null } | null;
  members: {
    character_id: string;
    name: string;
    class: string;
    level: number;
    seat: number;
    state: string;
    is_leader: boolean;
    steam_id: string | null;
    gear_hash: string;
    /** 9단계: 서버가 아는 착용 장비 원본과 전직(호스트가 멤버 카드와 대조해 불일치하면 이 값으로 덮어쓴다) */
    worn: MemberLook['worn'];
    career: number;
  }[];
  election_until: string | null;
  server_time: string;
}

export async function buildFieldView(db: Queryable, session: repo.SessionRow, meCharacterId: number | null, now: Date): Promise<FieldSessionView> {
  const members = await repo.activeMembers(db, session.id);
  const steam = await repo.steamIdsOf(db, members.map((m) => m.account_id));
  const looks = await memberLooks(db, members.map((m) => m.character_id));
  const { leaderId } = await repo.partyOrder(db, session.party_id);
  const host = members.find((m) => m.character_id === session.host_character_id);
  const me = members.find((m) => m.character_id === meCharacterId);
  const tokenOn = session.transport !== 'relay' && session.session_key !== null && me !== undefined;
  const electionEnd = session.created_at.getTime() + getConfig().field.electionWindowSeconds * 1000;
  return {
    id: session.uuid,
    version: session.version,
    map_id: session.map_id,
    state: session.state,
    host: host ? { character_id: host.character_uuid, seat: host.seat, steam_id: steam.get(host.account_id) ?? null, epoch: session.host_epoch } : null,
    transport: transportView(session),
    me: me ? { seat: me.seat, state: me.state, entry_token: tokenOn ? entryToken(session.session_key as Buffer, session.uuid, me.character_uuid, me.seat) : null } : null,
    members: members.map((m) => ({
      character_id: m.character_uuid,
      name: m.name,
      class: m.class,
      level: m.level,
      seat: m.seat,
      state: m.state,
      is_leader: m.character_id === leaderId,
      steam_id: steam.get(m.account_id) ?? null,
      gear_hash: looks.get(m.character_id)?.gear_hash ?? '',
      worn: looks.get(m.character_id)?.worn ?? [],
      career: looks.get(m.character_id)?.career ?? 0,
    })),
    election_until: session.state === 'active' && now.getTime() < electionEnd ? new Date(electionEnd).toISOString() : null,
    server_time: now.toISOString(),
  };
}

export const hostKeyOf = (session: repo.SessionRow, meCharacterId: number): string | null =>
  session.host_character_id === meCharacterId && session.session_key ? session.session_key.toString('base64url') : null;
