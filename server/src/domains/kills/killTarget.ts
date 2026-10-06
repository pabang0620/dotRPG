// 처치 보고의 맥락(3.2.1): 이 맵에서 이 몬스터가 나올 수 있는가, 속도·화력 검사에 쓸 값은 무엇인가.
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AnomalyError } from '../economy/economyService';
import { countFieldKillsSince, killCount } from './killRepository';

/** 필드 화력 창(초). 3.2.3 */
export const FIELD_POWER_WINDOW_SECONDS = 10;

export interface FieldKillInfo {
  sessionId: number;
  monsterRef: number | null;
  /** 파티 세션(활성 멤버 2명 이상)의 경험치 배율. null이면 감쇠 없음 */
  xpFactor: number | null;
  /** 재료·장비 드롭 확률에 곱하는 값 */
  dropMul: number;
  /** 9단계: 하드 격차(몬스터 레벨 - 멤버 레벨 >= FIELD_CARRY_HARD_GAP): 경험치는 정확히 1, 기본 골드도 1 */
  hardXp: boolean;
}

export interface KillTarget {
  context: 'field' | 'scripted' | 'dungeon';
  /** Trusted map data, never supplied by the client. */
  xpOverride?: number;
  mapId: string;
  level: number;
  hpMul: number;
  /** 1초 창 처치 수 상한 */
  burst: number;
  /** 화력 창 길이(초) */
  powerWindowSeconds: number;
  run: { id: number; roomIndex: number } | null;
  /** 레이드 던전 판인가(레이드 몬스터는 이 맥락에서만 받는다) */
  isRaid: boolean;
  /** 연습판(보상 잠금): 처치는 받아들이되 경험치·드롭을 주지 않는다 */
  rewardLocked: boolean;
  /** 판의 화력 상한 스냅샷 x 여유. null이면 본인 상한(3단계) */
  powerCap: number | null;
  /** 파티 판의 방장이 아닌 멤버: 거절은 기록하되 일시 차단 카운트(severity 2 이상)는 쌓지 않는다 */
  nonHost: boolean;
  /** 8단계: 필드 파티 세션 처치(session_id가 있을 때) */
  field: FieldKillInfo | null;
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
  opts: { sessionOnly?: boolean; nonHost?: boolean } = {},
): Promise<KillTarget> {
  const data = getGameData();
  const map = data.maps.get(mapId);
  const extra = data.economy.mapExtra.get(mapId);
  const detailBase = { map_id: mapId, monster_id: monsterId };
  if (!map || !extra || map.instanced) throw rejected('kill_target', 3, { ...detailBase, why: 'map' });

  const burst = getConfig().policy.killBurstField;
  // 필드 보스: 15분 창(시계 기준 :00 :15 :30 :45)마다 캐릭터당 1회만 인정한다
  const boss = extra.fieldBoss;
  if (boss && boss.monsterId === monsterId) {
    const windowMs = boss.intervalSeconds * 1000;
    const since = new Date(Math.floor(now.getTime() / windowMs) * windowMs);
    const n = await countFieldKillsSince(client, characterId, mapId, monsterId, since);
    if (n >= 1) throw rejected('kill_supply', opts.nonHost ? 1 : 2, { ...detailBase, count: n, limit: 1, field_boss: true });
    return {
      context: 'field',
      mapId,
      level: boss.level,
      xpOverride: boss.xp,
      hpMul: 1,
      burst,
      powerWindowSeconds: FIELD_POWER_WINDOW_SECONDS,
      run: null,
      isRaid: false,
      rewardLocked: false,
      powerCap: null,
      nonHost: opts.nonHost ?? false,
      field: null,
      commit: async () => {},
    };
  }
  const field = extra.fieldSpawns.find((s) => s.monsterId === monsterId);
  if (field) {
    // 리스폰 공급 상한(3.2.2): N개 스폰점, R초 리스폰이면 R초 안에 ceil(N * 여유)마리를 넘을 수 없다
    const def = data.economy.monsters.get(monsterId);
    const respawn = field.respawnSeconds ?? def?.respawnSeconds ?? 25;
    const limit = Math.ceil(field.points * getConfig().policy.killSupplyMargin);
    const since = new Date(now.getTime() - respawn * 1000);
    const n = await countFieldKillsSince(client, characterId, mapId, monsterId, since);
    if (n >= limit) throw rejected('kill_supply', opts.nonHost ? 1 : 2, { ...detailBase, count: n, limit, window_seconds: respawn });
    return {
      context: 'field',
      mapId,
      level: field.level,
      xpOverride: field.xp,
      hpMul: 1,
      burst,
      powerWindowSeconds: FIELD_POWER_WINDOW_SECONDS,
      run: null,
      isRaid: false,
      rewardLocked: false,
      powerCap: null,
      nonHost: opts.nonHost ?? false,
      field: null,
      commit: async () => {},
    };
  }

  // 연출 스폰은 세션 처치 대상이 아니다(맥락 불일치)
  if (opts.sessionOnly) throw rejected('kill_target', opts.nonHost ? 1 : 2, { ...detailBase, why: 'scripted_in_session' });
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
      isRaid: false,
      rewardLocked: false,
      powerCap: null,
      nonHost: false,
      field: null,
      commit: async () => {},
    };
  }
  throw rejected('kill_target', 3, { ...detailBase, why: 'monster_not_on_map' });
}
