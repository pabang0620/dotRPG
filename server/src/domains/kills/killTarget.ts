// 처치 보고의 맥락(3.2.1): 이 맵에서 이 몬스터가 나올 수 있는가, 속도·화력 검사에 쓸 값은 무엇인가.
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AnomalyError } from '../economy/economyService';
import { countFieldKillsSince, killCount } from './killRepository';

/** 필드 화력 창(초). 3.2.3 */
export const FIELD_POWER_WINDOW_SECONDS = 10;

export interface KillTarget {
  context: 'field' | 'scripted' | 'dungeon';
  mapId: string;
  level: number;
  hpMul: number;
  /** 1초 창 처치 수 상한 */
  burst: number;
  /** 화력 창 길이(초) */
  powerWindowSeconds: number;
  run: { id: number; roomIndex: number } | null;
  /** 받아들인 뒤(kill_log 기록 직전) 던전 진행을 갱신한다 */
  commit: (client: PoolClient) => Promise<void>;
}

export const rejected = (kind: AnomalyError['anomaly']['kind'], severity: 1 | 2 | 3, detail: Record<string, unknown>) =>
  new AnomalyError(422, '처치를 인정할 수 없습니다.', 'KILL_REJECTED', { kind, severity, detail });

/** run_id 없는 보고: 필드 스포너 또는 마을 연출 스폰. 그 밖은 거절 */
export async function resolveFieldTarget(
  client: PoolClient,
  characterId: number,
  mapId: string,
  monsterId: string,
  now: Date,
): Promise<KillTarget> {
  const data = getGameData();
  const map = data.maps.get(mapId);
  const extra = data.economy.mapExtra.get(mapId);
  const detailBase = { map_id: mapId, monster_id: monsterId };
  if (!map || !extra || map.instanced) throw rejected('kill_target', 3, { ...detailBase, why: 'map' });

  const burst = getConfig().policy.killBurstField;
  const field = extra.fieldSpawns.find((s) => s.monsterId === monsterId);
  if (field) {
    // 리스폰 공급 상한(3.2.2): N개 스폰점, R초 리스폰이면 R초 안에 ceil(N * 여유)마리를 넘을 수 없다
    const def = data.economy.monsters.get(monsterId);
    const respawn = def?.respawnSeconds ?? 25;
    const limit = Math.ceil(field.points * getConfig().policy.killSupplyMargin);
    const since = new Date(now.getTime() - respawn * 1000);
    const n = await countFieldKillsSince(client, characterId, mapId, monsterId, since);
    if (n >= limit) throw rejected('kill_supply', 2, { ...detailBase, count: n, limit, window_seconds: respawn });
    return {
      context: 'field',
      mapId,
      level: 1,
      hpMul: 1,
      burst,
      powerWindowSeconds: FIELD_POWER_WINDOW_SECONDS,
      run: null,
      commit: async () => {},
    };
  }

  const scripted = extra.scriptedSpawns.find((s) => s.monsterId === monsterId);
  if (scripted) {
    const kills = await killCount(client, characterId, monsterId);
    if (kills >= scripted.total) {
      throw rejected('kill_supply', 2, { ...detailBase, count: kills, limit: scripted.total, scripted: true });
    }
    return {
      context: 'scripted',
      mapId,
      level: 1,
      hpMul: 1,
      burst,
      powerWindowSeconds: FIELD_POWER_WINDOW_SECONDS,
      run: null,
      commit: async () => {},
    };
  }
  throw rejected('kill_target', 3, { ...detailBase, why: 'monster_not_on_map' });
}
