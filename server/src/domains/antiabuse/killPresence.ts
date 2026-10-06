// 처치·채집·상자·드롭 줍기의 프레즌스 검사(4.5): 신선도, 현재 맵(맵 위조 방지), 필드 보스 체류.
// 정직한 클라이언트는 맵 로드 직후 첫 처치 전에 프레즌스를 먼저 보내므로 정상 흐름에서는 걸리지 않는다.
import { getConfig } from '../../config/env';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { metrics } from '../../ops/metrics';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { AnomalyError } from '../economy/economyService';
import { rejected } from '../kills/killTarget';
import { isFresh } from './presenceService';
import { readOnline } from './presenceRepository';

export interface KillPresenceInput {
  mapId: string;
  /** 던전 처치(run_id 있음): 판 멤버십이 맵을 정하므로 신선도만 본다 */
  dungeon: boolean;
  isFieldBoss: boolean;
  monsterId: string;
}

export async function assertKillPresence(ctx: EconCtx, o: KillPresenceInput): Promise<void> {
  const pc = getConfig().aa.presence;
  const mode = pc.killMode;
  if (mode === 'off') return;
  const enforce = mode === 'enforce';
  const row = await readOnline(ctx.client, ctx.char.accountId);
  const mine = row && row.character_id === ctx.char.id ? row : null;

  // 1. 신선도: 프레즌스가 없으면 클라이언트가 프레즌스를 보낸 뒤 같은 request_id로 재시도한다
  if (!isFresh(mine, ctx.now, pc.staleRejectSeconds)) {
    metrics.presenceRequired++;
    if (enforce) throw new AppError(409, '위치 신호가 필요합니다. 잠시 후 다시 시도해 주세요.', 'PRESENCE_REQUIRED');
    logger.warn({ account: ctx.char.accountId, character: ctx.char.id, map: o.mapId }, 'anti_abuse.presence_required');
    return;
  }
  if (o.dungeon) return;
  const p = mine as NonNullable<typeof mine>;

  // 2. 맵 일치: 현재 맵이거나, 맵을 막 떠난 직후(유예 안)의 옛 맵 보고
  const graceOk = p.prev_map_id === o.mapId && ctx.now.getTime() - p.map_changed_at.getTime() <= pc.mapGraceSeconds * 1000;
  if (p.map_id !== o.mapId && !graceOk) {
    if (enforce) throw rejected('kill_presence', 2, { map_id: o.mapId, presence_map: p.map_id });
    await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'kill_presence', 1, { map_id: o.mapId, presence_map: p.map_id });
    return;
  }

  // 3. 필드 보스: 그 맵에 끊김 없이 머문 시간
  if (o.isFieldBoss && ctx.now.getTime() - p.map_since.getTime() < pc.fieldBossMinSeconds * 1000) {
    const detail = { map_id: o.mapId, monster_id: o.monsterId, why: 'boss_presence' };
    if (enforce) throw rejected('kill_presence', 1, detail);
    await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'kill_presence', 1, detail);
  }
}

export type ActionKind = 'gather' | 'chest' | 'drop_claim';

/**
 * 채집·상자·드롭 줍기의 프레즌스 검사(처치와 같은 PRESENCE_KILL_MODE 스위치): 신선도, 맵을 아는 요청은 현재 맵(유예 포함).
 * log 모드는 기록만 하고 통과한다. 드롭 줍기는 맵을 보내지 않으므로 신선도만 본다.
 */
export async function assertActionPresence(ctx: EconCtx, action: ActionKind, mapId: string | null): Promise<void> {
  const pc = getConfig().aa.presence;
  const mode = pc.killMode;
  if (mode === 'off') return;
  const enforce = mode === 'enforce';
  const row = await readOnline(ctx.client, ctx.char.accountId);
  const mine = row && row.character_id === ctx.char.id ? row : null;

  if (!isFresh(mine, ctx.now, pc.staleRejectSeconds)) {
    metrics.presenceRequired++;
    if (enforce) throw new AppError(409, '위치 신호가 필요합니다. 잠시 후 다시 시도해 주세요.', 'PRESENCE_REQUIRED');
    logger.warn({ account: ctx.char.accountId, character: ctx.char.id, action, map: mapId }, 'anti_abuse.presence_required');
    return;
  }
  if (mapId === null) return;
  const p = mine as NonNullable<typeof mine>;
  const graceOk = p.prev_map_id === mapId && ctx.now.getTime() - p.map_changed_at.getTime() <= pc.mapGraceSeconds * 1000;
  if (p.map_id !== mapId && !graceOk) {
    const detail = { map_id: mapId, presence_map: p.map_id, action };
    if (enforce) throw new AnomalyError(422, '현재 위치와 맞지 않는 요청입니다.', 'PRESENCE_MAP_MISMATCH', { kind: 'kill_presence', severity: 2, detail });
    await econRepo.insertAnomaly(ctx.client, ctx.char.accountId, ctx.char.id, 'kill_presence', 1, detail);
  }
}
