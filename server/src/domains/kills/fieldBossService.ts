// 필드 보스 출현 시각: 서버가 기준이다. 마지막 처치 + intervalSeconds 가 다음 출현 시각
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { findOwnedAlive } from '../characters/characterRepository';
import { lastFieldBossKills } from './killRepository';

export interface FieldBossStatus {
  map_id: string;
  monster_id: string;
  interval_seconds: number;
  last_killed_at: string | null;
  /** 이 시각 이후에 나온다(이미 지났으면 지금 나와 있는 상태) */
  next_at: string;
}

export async function fieldBossStatus(accountId: number, characterUuid: string): Promise<{ server_time: string; bosses: FieldBossStatus[] }> {
  const c = await findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const now = getNow();
  const pairs: { mapId: string; monsterId: string; interval: number }[] = [];
  for (const [mapId, extra] of getGameData().economy.mapExtra) {
    if (extra.fieldBoss) pairs.push({ mapId, monsterId: extra.fieldBoss.monsterId, interval: extra.fieldBoss.intervalSeconds });
  }
  const last = await lastFieldBossKills(getPool(), c.id, pairs);
  const bosses = pairs.map((p) => {
    const at = last.get(p.mapId) ?? null;
    const next = at ? new Date(at.getTime() + p.interval * 1000) : now;
    return {
      map_id: p.mapId,
      monster_id: p.monsterId,
      interval_seconds: p.interval,
      last_killed_at: at ? at.toISOString() : null,
      next_at: (next > now ? next : now).toISOString(),
    };
  });
  return { server_time: now.toISOString(), bosses };
}
