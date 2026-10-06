import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getGameData } from '../src/gamedata/loader';
import { effectiveHp } from '../src/domains/kills/killRules';
import { auth } from './helpers';
import { newHero, post, get, type Hero } from './economyHelpers';

export { newHero, post, get };
export type { Hero };

/** request_id 없이 보내는 요청(ready, heartbeat) */
export const raw = (app: Express, h: Hero, path: string, body: Record<string, unknown>) =>
  request(app).post(`/characters/${h.id}${path}`).set(auth(h.s)).send(body);

export const patch = (app: Express, h: Hero, path: string, body: Record<string, unknown>) =>
  request(app).patch(`/characters/${h.id}${path}`).set(auth(h.s)).send(body);
export const del = (app: Express, h: Hero, path: string) => request(app).delete(`/characters/${h.id}${path}`).set(auth(h.s));

/** 던전의 방 구성(dungeons.json): 방마다 맵과 처치해야 할 (몬스터, 마릿수) */
export function roomsOf(dungeonId = 'gold_vein'): { map: string; kills: [string, number][] }[] {
  const d = getGameData().economy.dungeons.byId.get(dungeonId);
  if (!d) throw new Error(`던전 없음 ${dungeonId}`);
  return d.rooms.map((r) => ({ map: r.mapId, kills: r.groups.map((g) => [g.monsterId, g.count] as [string, number]) }));
}

/** 레이드 입장 레벨(dungeons.json raidNumbers.recommendedLevel). 데이터가 바뀌어도 테스트가 따라간다 */
export function raidEntryLevel(dungeonId = 'raid_skeleton_king'): number {
  const d = getGameData().economy.dungeons.byId.get(dungeonId);
  const need = d?.raidNumbers?.recommendedLevel;
  if (need === undefined) throw new Error(`레이드 없음 ${dungeonId}`);
  return need;
}

export interface Duo {
  host: Hero;
  member: Hero;
  partyId: string;
  runId: string;
  hostRun: string;
  memberRun: string;
}

export const createParty = (app: Express, h: Hero, over: Record<string, unknown> = {}) =>
  post(app, h, '/parties', { dungeon_id: 'gold_vein', difficulty: 0, max_members: 4, min_power: 0, listed: true, ...over });

/** 방장이 모집 글을 만들고 멤버가 신청해 수락받은 상태(준비 완료까지) */
export async function formParty(app: Express, host: Hero, members: Hero[], over: Record<string, unknown> = {}): Promise<string> {
  const created = await createParty(app, host, over);
  if (created.status !== 201) throw new Error(`createParty ${created.status} ${JSON.stringify(created.body)}`);
  const partyId = created.body.data.party.id as string;
  for (const m of members) {
    const ap = await post(app, m, `/parties/${partyId}/apply`, {});
    if (ap.status !== 201) throw new Error(`apply ${ap.status} ${JSON.stringify(ap.body)}`);
    const rs = await post(app, host, `/party/applications/${ap.body.data.application.id}/respond`, { accept: true });
    if (rs.status !== 200) throw new Error(`respond ${rs.status} ${JSON.stringify(rs.body)}`);
  }
  // 구성이 바뀌면 준비가 풀리므로 모두 들어온 뒤에 준비한다
  for (const m of members) {
    const rd = await raw(app, m, '/party/ready', { ready: true });
    if (rd.status !== 200) throw new Error(`ready ${rd.status} ${JSON.stringify(rd.body)}`);
  }
  return partyId;
}

/** 출발 -> 멤버 전원 입장 확인 -> 자동 시작. 각자의 dungeon_runs uuid를 돌려준다 */
export async function startAndBegin(
  app: Express,
  host: Hero,
  members: Hero[],
  aiCount = 0,
): Promise<{ runId: string; runs: Map<string, string>; hostKey: string }> {
  const st = await post(app, host, '/party/start', { ai_count: aiCount });
  if (st.status !== 201) throw new Error(`start ${st.status} ${JSON.stringify(st.body)}`);
  const runId = st.body.data.run.id as string;
  for (const m of members) {
    const pv = await get(app, m, '/party');
    const token = pv.body.data.party.run.me.entry_token as string;
    const j = await post(app, m, `/party-runs/${runId}/join`, { entry_token: token });
    if (j.status !== 200) throw new Error(`join ${j.status} ${JSON.stringify(j.body)}`);
  }
  if (members.length === 0) {
    const b = await post(app, host, `/party-runs/${runId}/begin`, {});
    if (b.status !== 201) throw new Error(`begin ${b.status} ${JSON.stringify(b.body)}`);
  }
  const runs = new Map<string, string>();
  for (const h of [host, ...members]) {
    const r = await get(app, h, `/party-runs/${runId}`);
    runs.set(h.id, r.body.data.run.me.run_id as string);
  }
  return { runId, runs, hostKey: st.body.data.host_key as string };
}

/** 방장이 이어서 begin을 눌러 시작(들어온 사람으로) */
export async function killRoom(
  app: Express,
  h: Hero,
  runId: string,
  room: number,
  advance: (s: number) => void,
  dungeonId = 'gold_vein',
  step = 2,
): Promise<void> {
  const rooms = roomsOf(dungeonId);
  for (const [monster, n] of (rooms[room] as { kills: [string, number][] }).kills) {
    for (let i = 0; i < n; i++) {
      advance(step);
      const res = await post(app, h, '/kills', {
        map_id: (rooms[room] as { map: string }).map,
        monster_id: monster,
        run_id: runId,
        room_index: room,
      });
      if (res.status !== 200) throw new Error(`kill ${res.status} ${JSON.stringify(res.body)}`);
    }
  }
}

export async function clearAll(
  app: Express,
  h: Hero,
  runId: string,
  advance: (s: number) => void,
  dungeonId = 'gold_vein',
  step = 2,
): Promise<void> {
  for (let room = 0; room < roomsOf(dungeonId).length; room++) await killRoom(app, h, runId, room, advance, dungeonId, step);
}

export const stats = (over: Record<string, unknown> = {}) => ({ elapsed_ms: 80_000, hits_taken: 2, max_combo: 20, revives_used: 0, ...over });

/** 방장 보고를 정직하게 조립한다: 방별 처치 수는 방 구성, 딜은 서버가 받은 처치의 실효 HP를 나눈 값 */
export async function honestHostReport(
  hostRunUuid: string,
  heroes: Hero[],
  over: Record<string, unknown> = {},
  dungeonId = 'gold_vein',
): Promise<Record<string, unknown>> {
  const eco = getGameData().economy;
  const db = getPool();
  const dungeon = eco.dungeons.byId.get(dungeonId);
  const run = await db.query<{ id: string; party_size: number; difficulty: number }>(
    'SELECT id, party_size, difficulty FROM dungeon_runs WHERE uuid = $1',
    [hostRunUuid],
  );
  const r = run.rows[0] as { id: string; party_size: number; difficulty: number };
  const kills = await db.query<{ monster_id: string; monster_level: number }>(
    'SELECT monster_id, monster_level FROM kill_log WHERE run_id = $1',
    [r.id],
  );
  const baseMul = dungeon?.isRaid ? (dungeon.raidNumbers as { hpMul: number }).hpMul : (eco.dungeons.difficulties[r.difficulty] as { hpMul: number }).hpMul;
  const hpMul = baseMul * (eco.dungeons.partyHpScale[r.party_size - 1] as number);
  const hp = kills.rows.reduce((a, k) => a + effectiveHp(eco, eco.monsters.get(k.monster_id) as never, k.monster_level, hpMul), 0);
  const each = Math.ceil(hp / heroes.length);
  return {
    request_id: randomUUID(),
    host_epoch: 1,
    outcome: 'cleared',
    elapsed_ms: 80_000,
    rooms: roomsOf(dungeonId).map((room, i) => ({ room_index: i, kills: room.kills.map(([m, c]) => ({ monster_id: m, count: c })) })),
    members: heroes.map((h) => ({ character_id: h.id, hits_taken: 2, max_combo: 20, revives_used: 0, damage_dealt: each })),
    ai: [],
    ...over,
  };
}
