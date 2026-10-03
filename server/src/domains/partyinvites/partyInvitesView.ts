import type { IncomingView } from './partyInvitesRepository';

/** GET /party의 invites_incoming 항목이자 WebSocket party.invite 프레임의 본문 */
export function inviteBody(v: IncomingView) {
  return {
    id: v.uuid,
    from: { id: v.inviter_uuid, name: v.inviter_name, class: v.inviter_class, level: v.inviter_level },
    party: {
      dungeon_id: v.dungeon_id,
      difficulty: v.difficulty,
      members: v.members,
      max_members: v.max_members,
      min_power: v.min_power,
      message: v.message,
    },
    expires_at: v.expires_at.toISOString(),
  };
}
