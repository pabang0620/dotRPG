import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import { resolveDungeonTarget } from '../dungeons/dungeonKillContext';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { AnomalyError, runEconomy, type StoredResult } from '../economy/economyService';
import * as repo from './killRepository';
import { attackCap, effectiveHp, monsterXp, powerAllows, rollKillDrops } from './killRules';
import { rejected, resolveFieldTarget, type KillTarget } from './killTarget';
import type { KillBody } from './killValidation';

const ENDPOINT = 'POST /characters/:uuid/kills';
/** 황금 해골 타격 골드 횟수의 절대 상한(3.1.2) */
const MAX_HITS = 40;

export function reportKill(accountId: number, characterUuid: string, body: KillBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: ENDPOINT,
    requestId,
    payload,
    handler: (ctx) => processKill(ctx, body),
  });
}

async function resolveTarget(ctx: EconCtx, body: KillBody): Promise<KillTarget> {
  if (body.run_id !== undefined && body.room_index !== undefined) {
    return resolveDungeonTarget(ctx, body.run_id, body.map_id, body.room_index, body.monster_id);
  }
  return resolveFieldTarget(ctx.client, ctx.char.id, body.map_id, body.monster_id, ctx.now);
}

async function processKill(ctx: EconCtx, body: KillBody) {
  const eco = getGameData().economy;
  const pol = getConfig().policy;

  const def = eco.monsters.get(body.monster_id);
  if (!def) throw new AppError(422, '알 수 없는 몬스터입니다.', 'MONSTER_UNKNOWN');
  if (def.noLoot) throw new AppError(422, '보상이 없는 몬스터입니다.', 'MONSTER_NO_REWARD');
  const raidDenied = () => new AppError(422, '레이드 맥락에서만 받을 수 있는 몬스터입니다.', 'RAID_NOT_AVAILABLE');
  if (def.raid && body.run_id === undefined) throw raidDenied();

  // 3.2.4 반복 이상이면 일시 차단(차단 중 요청은 이상 기록을 더하지 않는다)
  const windowStart = new Date(ctx.now.getTime() - pol.anomalyWindowMinutes * 60_000);
  const recent = await econRepo.countRecentKillAnomalies(ctx.client, ctx.char.id, windowStart);
  if (recent >= pol.anomalyBlockCount) {
    throw new AppError(403, '처치 보고가 일시적으로 제한되었습니다.', 'KILL_BLOCKED');
  }

  const target = await resolveTarget(ctx, body);
  if (def.raid && !target.isRaid) throw raidDenied();

  // 속도: 1초 창 처치 수. 정직한 클라이언트도 드물게 닿을 수 있어 429로 재시도를 안내한다
  const burstSince = new Date(ctx.now.getTime() - 1000);
  const burstCount = await repo.countKillsSince(ctx.client, ctx.char.id, burstSince);
  if (burstCount >= target.burst) {
    throw new AnomalyError(
      429,
      '처치 보고가 너무 빠릅니다.',
      'KILL_RATE_LIMITED',
      { kind: 'kill_rate', severity: 1, detail: { count: burstCount, limit: target.burst, monster_id: body.monster_id } },
      { retry_after_sec: 1 },
    );
  }

  // 화력: 창 안 실효 HP 합 <= 화력 상한 x 창 길이 x AOE + 가장 큰 몬스터 HP
  const hpOf = (monsterId: string, level: number, hpMul: number): number => {
    const d = eco.monsters.get(monsterId);
    return d ? effectiveHp(eco, d, level, hpMul) : 0;
  };
  const rows = target.run
    ? await repo.killsOfRun(ctx.client, target.run.id)
    : await repo.killsInWindow(ctx.client, ctx.char.id, new Date(ctx.now.getTime() - target.powerWindowSeconds * 1000));
  const rowHp = rows.map((r) => hpOf(r.monster_id, r.monster_level, target.run ? target.hpMul : 1));
  const thisHp = hpOf(body.monster_id, target.level, target.hpMul);
  const worn = await econRepo.listWornKeys(ctx.client, ctx.char.id);
  const cap = target.powerCap ?? attackCap(eco, pol, ctx.level, worn);
  if (!powerAllows(eco, pol, cap, target.powerWindowSeconds, [...rowHp, thisHp])) {
    throw rejected('kill_power', target.nonHost ? 1 : 2, {
      monster_id: body.monster_id,
      window_seconds: target.powerWindowSeconds,
      hp_sum: Math.round([...rowHp, thisHp].reduce((a, b) => a + b, 0)),
      cap: Math.round(cap),
    });
  }

  // 보상: 서버 데이터로만 계산한다(요청의 어떤 값도 쓰지 않는다)
  const xp = target.xpOverride ?? monsterXp(eco, def, target.level);
  // 연습판(레이드 보상 잠금)은 처치를 받아들이되 경험치·드롭을 주지 않는다(무한 입장 파밍 방지)
  const { granted, leveledUp } = target.rewardLocked
    ? { granted: 0, leveledUp: false }
    : await ctx.grantXp(xp, 'kill', def.id);
  const hits =
    def.goldPerHitMax > 0
      ? Math.min(body.hits ?? 0, Math.ceil(thisHp / eco.player.attackDamage), MAX_HITS)
      : 0;

  const killId = await repo.insertKill(ctx.client, {
    characterId: ctx.char.id,
    mapId: target.mapId,
    monsterId: def.id,
    monsterLevel: target.level,
    context: target.context,
    hits,
    xpGranted: granted,
    requestId: ctx.requestId,
    createdAt: ctx.now,
    runId: target.run?.id ?? null,
    roomIndex: target.run?.roomIndex ?? null,
  });
  await repo.bumpKillStats(ctx.client, ctx.char.id, def.id, ctx.now);
  await target.commit(ctx.client);

  // 미수령 드롭이 너무 많으면 드롭 굴림만 건너뛴다(경험치는 인정)
  const drops: { id: string; item_key: string; count: number; expires_at: string }[] = [];
  const open = await repo.countOpenDrops(ctx.client, ctx.char.id, ctx.now);
  if (!target.rewardLocked && open < pol.dropOpenPerCharacter) {
    const expiresAt = new Date(ctx.now.getTime() + pol.dropTtlSeconds * 1000);
    for (const spec of rollKillDrops(eco, def, ctx.char.class, hits, getRng())) {
      const row = await repo.insertDrop(ctx.client, ctx.char.id, killId, spec.itemKey, spec.count, expiresAt, ctx.now);
      drops.push({ id: row.uuid, item_key: row.item_key, count: row.count, expires_at: row.expires_at.toISOString() });
    }
  }

  const data: Record<string, unknown> = { granted_xp: granted, leveled_up: leveledUp, drops, delta: ctx.delta() };
  if (target.rewardLocked) data.reward_locked = true;
  return { status: 200, data };
}
