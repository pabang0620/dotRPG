// 마을에서 서로 보이기: 같은 마을·같은 채팅 채널(shard)에 있는 사람에게 모습과 위치를 중계한다.
// 표시용 데이터라 DB에 남기지 않고, 전투·보상 판정에 쓰지 않는다. 사냥터는 필드 파티 세션이 맡는다.
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { parseItemKey } from '../../utils/itemKey';
import type { ChatSession, ServerProfile, TownLook } from './chatSession';
import { registry } from './realtimeNotifier';
import type { Frame, TownPosFrame } from './wsProtocol';

/** 한 사람의 위치 프레임을 받는 최소 간격(ms). 클라이언트는 약 5회/초 이하로 보낸다 */
const MIN_GAP_MS = 150;

function isTown(mapId: string): boolean {
  const m = getGameData().maps.get(mapId);
  return !!m && !m.instanced && m.safe === true;
}

function frameOf(s: ChatSession, t: TownLook): Frame {
  return {
    t: 'town.pos',
    id: s.characterUuid,
    name: s.characterName,
    map_id: t.map,
    x: t.x,
    y: t.y,
    f: t.f,
    m: t.m,
    cls: t.cls,
    career: t.career,
    skin: t.skin,
    weapon: t.weapon,
    level: t.level,
  };
}

/** 같은 채널에서 그 마을에 있는 다른 사람(서로 차단한 사이는 뺀다) */
function neighbours(s: ChatSession, map: string): ChatSession[] {
  return registry
    .inShard(s.shard)
    .filter((o) => o !== s && o.ready && !o.closed && o.town?.map === map && !o.blocks.has(s.accountId) && !s.blocks.has(o.accountId));
}

/** 마을을 떠났다(다른 맵, 접속 종료): 그 마을 사람들 화면에서 지운다 */
export function leaveTown(s: ChatSession): void {
  const t = s.town;
  if (!t) return;
  s.town = null;
  for (const o of neighbours(s, t.map)) o.send({ t: 'town.gone', id: s.characterUuid });
}

/**
 * 9단계 11.2: 마을 표시를 서버 값으로. 클래스·레벨은 characters, 전직은 character_career(없으면 0), 무기는 착용 무기 슬롯의 기본 id,
 * 외형은 그 계정이 소유한 것만(없으면 빈 문자열). 이름은 이미 서버 값이다. 클라이언트가 보낸 값은 무시한다(프로토콜은 그대로).
 */
export async function loadProfile(s: ChatSession): Promise<void> {
  const r = await getPool().query<{ class: string; level: number; career: number | null; weapon: string | null; skins: string[] | null }>(
    `SELECT c.class, c.level,
            (SELECT cc.career FROM character_career cc WHERE cc.character_id = c.id) AS career,
            (SELECT i.item_key FROM character_items i WHERE i.character_id = c.id AND i.location = 'worn' AND i.slot = 0) AS weapon,
            (SELECT array_agg(a.item_id) FROM account_cosmetics a WHERE a.account_id = c.account_id) AS skins
       FROM characters c WHERE c.id = $1`,
    [s.characterId],
  );
  const row = r.rows[0];
  if (!row) return;
  const profile: ServerProfile = {
    cls: row.class === 'mage' ? 1 : 0,
    level: row.level,
    career: row.career ?? 0,
    weapon: row.weapon ? (parseItemKey(row.weapon)?.base ?? '') : '',
    ownedSkins: new Set(row.skins ?? []),
  };
  s.profile = profile;
  s.profileAt = Date.now();
}

export async function handleTownPos(s: ChatSession, f: TownPosFrame): Promise<void> {
  if (!isTown(f.map_id)) {
    leaveTown(s);
    return;
  }
  const now = Date.now();
  const entering = s.town?.map !== f.map_id;
  if (!entering && now - s.townAt < MIN_GAP_MS) return;
  // 마을 진입과 WS_REVALIDATE_SECONDS마다 서버 값을 다시 읽는다(레벨업·장비 교체가 수십 초 안에 반영된다)
  if (!s.profile || entering || now - s.profileAt > getConfig().social.wsRevalidateSeconds * 1000) await loadProfile(s);
  const profile = s.profile;
  if (!profile || s.closed) return;
  if (entering) leaveTown(s);
  s.townAt = now;
  const look: TownLook = {
    map: f.map_id,
    x: Math.round(f.x * 100) / 100,
    y: Math.round(f.y * 100) / 100,
    f: f.f,
    m: f.m,
    cls: profile.cls,
    career: profile.career,
    skin: f.skin !== undefined && profile.ownedSkins.has(f.skin) ? f.skin : '',
    weapon: profile.weapon,
    level: profile.level,
  };
  s.town = look;
  const others = neighbours(s, look.map);
  const mine = frameOf(s, look);
  for (const o of others) {
    o.send(mine);
    // 새로 들어왔으면 이미 있던 사람들 모습을 한 번에 받는다
    if (entering && o.town) s.send(frameOf(o, o.town));
  }
}
