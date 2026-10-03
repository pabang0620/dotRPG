// 던전 맥락의 처치 보고(9.4): 진행 중인 내 판, 방 구성, 방 진행을 서버 기록으로 판정한다.
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import type { EconCtx } from '../economy/economyContext';
import { rejected as rejectedBase, type KillTarget } from '../kills/killTarget';
import * as repo from './dungeonRepository';
import { diffOf, roomKillCount, roomTotal } from './dungeonRules';

const notPlaying = () => new AppError(409, '진행 중인 던전이 아닙니다.', 'RUN_NOT_PLAYING');

export async function resolveDungeonTarget(
  ctx: EconCtx,
  runUuid: string,
  mapId: string,
  roomIndex: number,
  monsterId: string,
): Promise<KillTarget> {
  const eco = getGameData().economy;
  const pol = getConfig().policy;
  const run = await repo.findRunByUuid(ctx.client, ctx.char.id, runUuid);
  const stale = run && ctx.now.getTime() - run.started_at.getTime() > pol.runStaleSeconds * 1000;
  if (!run || run.state !== 'playing' || stale) throw notPlaying();

  // 파티 판: 판이 진행 중이고 내가 활성 멤버여야 한다. 방장이 아닌 멤버의 거절은 차단 카운트에 쌓지 않는다
  let nonHost = false;
  if (run.party_run_id !== null) {
    const pr = await ctx.client.query<{ state: string; host: string; mstate: string }>(
      `SELECT r.state, r.host_character_id AS host, m.state AS mstate
         FROM party_runs r JOIN party_run_members m ON m.party_run_id = r.id AND m.character_id = $2
        WHERE r.id = $1`,
      [run.party_run_id, ctx.char.id],
    );
    const row = pr.rows[0];
    if (!row || row.state !== 'playing' || !['playing', 'disconnected'].includes(row.mstate)) throw notPlaying();
    nonHost = Number(row.host) !== ctx.char.id;
  }
  const rejected = (kind: Parameters<typeof rejectedBase>[0], severity: 1 | 2 | 3, detail: Record<string, unknown>) =>
    rejectedBase(kind, nonHost ? 1 : severity, detail);

  const dungeon = eco.dungeons.byId.get(run.dungeon_id);
  const room = dungeon?.rooms[roomIndex];
  const base = { run_id: runUuid, map_id: mapId, monster_id: monsterId, room_index: roomIndex };
  if (!dungeon || !room) throw rejected('kill_target', 3, { ...base, why: 'room' });
  if (room.mapId !== mapId) throw rejected('kill_target', 3, { ...base, why: 'room_map' });

  // 방 진행: 현재 방이거나, 현재 방이 충분히 정리된 뒤의 다음 방만
  if (roomIndex !== run.room_index && roomIndex !== run.room_index + 1) {
    throw rejected('kill_target', 2, { ...base, current_room: run.room_index, why: 'room_order' });
  }
  if (roomIndex === run.room_index + 1) {
    const done = roomKillCount(run.room_kills, run.room_index);
    const need = roomTotal(dungeon, run.room_index) * pol.dungeonRoomClearRatio;
    if (done < need) {
      throw rejected('kill_target', 2, { ...base, current_room: run.room_index, done, need, why: 'room_not_cleared' });
    }
  }

  // 그룹: 방 구성에 있는 몬스터이고 받아들인 처치 수가 그룹 count를 넘지 못한다
  const groups = room.groups.filter((g) => g.monsterId === monsterId);
  if (groups.length === 0) throw rejected('kill_target', 3, { ...base, why: 'monster_not_in_room' });
  const key = `${roomIndex}:${monsterId}`;
  const soFar = run.room_kills[key] ?? 0;
  let acc = 0;
  let group = null as (typeof groups)[number] | null;
  for (const g of groups) {
    acc += g.count;
    if (soFar < acc) {
      group = g;
      break;
    }
  }
  if (!group) throw rejected('kill_supply', 2, { ...base, count: soFar, limit: acc });

  const diff = diffOf(eco, dungeon as NonNullable<typeof dungeon>, run.difficulty);
  if (!diff) throw rejected('kill_target', 3, { ...base, why: 'difficulty' });
  const partyScale = eco.dungeons.partyHpScale[run.party_size - 1] ?? 1;
  const elapsedSec = (ctx.now.getTime() - run.started_at.getTime()) / 1000;
  const newKills = { ...run.room_kills, [key]: soFar + 1 };

  return {
    context: 'dungeon',
    mapId,
    level: 1 + diff.monsterLevel + group.levelOffset,
    hpMul: diff.hpMul * partyScale,
    isRaid: (dungeon as NonNullable<typeof dungeon>).isRaid,
    rewardLocked: run.reward_locked && !pol.raidPracticePaysKills,
    powerCap: run.power_cap === null ? null : run.power_cap * pol.partyPowerSlack,
    nonHost,
    burst: pol.killBurstDungeon,
    powerWindowSeconds: Math.max(0, elapsedSec) + 5,
    run: { id: run.id, roomIndex },
    commit: (client) => repo.updateRunProgress(client, run.id, Math.max(run.room_index, roomIndex), newKills),
  };
}
