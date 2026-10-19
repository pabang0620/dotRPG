// 월드보스·길드보스 도전: 시작(횟수 차감) -> 3분 전투(클라이언트) -> 끝 보고(피해를 화력 상한으로 잘라 공유 체력에 반영).
import { getConfig } from '../../config/env';
import { getPool, type Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { resetBoundaries } from '../../utils/resetBoundaries';
import * as charRepo from '../characters/characterRepository';
import type { EconCtx } from '../economy/economyContext';
import * as econRepo from '../economy/economyRepository';
import { runEconomy, type StoredResult } from '../economy/economyService';
import * as guildRepo from '../guilds/guildRepository';
import { GUILD_BOSS_JOIN_WAIT_MS } from '../guilds/guildRules';
import { attackCap } from '../kills/killRules';
import { rankOf, ranked } from '../rankings/rankingService';
import * as repo from './bossRushRepository';
import {
  ATTEMPTS_PER_DAY, ATTEMPT_SECONDS, BOSS_LEVEL, FINISH_GRACE_SECONDS, GUILD_BOSS_ID, GUILD_STAGE1_HP, ROOM_HP_MUL,
  WORLD_BOSS_ID, applyGuildDamage, attemptDamageCap, stageDamageMul, type BossKind,
} from './bossRushRules';
import type { FinishBody, StartBody } from './bossRushValidation';

const WORLD_NAME = '심연의 거신';
const GUILD_NAME = '길드의 거상';

/** 이번 주 시즌(없으면 만든다) */
export async function currentSeason(db: Queryable, kind: BossKind, now: Date): Promise<repo.Season> {
  const b = resetBoundaries(now);
  const hp = kind === 'world' ? getConfig().policy.worldBossHp : GUILD_STAGE1_HP;
  return repo.ensureSeason(db, kind, new Date(b.weeklyStartAt), new Date(b.nextWeeklyAt), kind === 'world' ? WORLD_BOSS_ID : GUILD_BOSS_ID, hp);
}

async function attemptsLeft(db: Queryable, characterId: number, kind: BossKind, now: Date): Promise<number> {
  const used = await repo.countToday(db, characterId, kind, new Date(resetBoundaries(now).dailyStartAt));
  return Math.max(0, ATTEMPTS_PER_DAY[kind] - used);
}

async function snapshot(db: Queryable, characterId: number, now: Date) {
  const b = resetBoundaries(now);
  const world = await currentSeason(db, 'world', now);
  const worldRows = (await ranked('world_boss', world.week_start)).rows;
  const m = await guildRepo.membershipOf(db, characterId);
  let guild: Record<string, unknown> | null = null;
  if (m) {
    const gs = await currentSeason(db, 'guild', now);
    const p = await repo.ensureProgress(db, gs.id, m.guild_id, GUILD_STAGE1_HP, now);
    const guildRows = (await ranked('guild_boss', gs.week_start)).rows;
    guild = {
      season_id: gs.uuid, boss_id: gs.boss_id, name: GUILD_NAME, stage: p.stage, max_hp: p.stage_hp, hp_left: p.hp_left,
      attempts_left: await attemptsLeft(db, characterId, 'guild', now), attempts_per_day: ATTEMPTS_PER_DAY.guild,
      eligible_at: new Date(m.joined_at.getTime() + GUILD_BOSS_JOIN_WAIT_MS).toISOString(),
      guild_rank: rankOf(guildRows, m.guild_id),
    };
  }
  return {
    world: {
      season_id: world.uuid, boss_id: world.boss_id, name: WORLD_NAME, max_hp: world.max_hp, hp_left: world.hp_left,
      defeated_at: world.defeated_at?.toISOString() ?? null,
      attempts_left: await attemptsLeft(db, characterId, 'world', now), attempts_per_day: ATTEMPTS_PER_DAY.world,
      my_best: await repo.bestOf(db, world.id, characterId), my_rank: rankOf(worldRows, characterId),
    },
    guild,
    week_ends_at: b.nextWeeklyAt,
    attempt_seconds: ATTEMPT_SECONDS,
    min_level: getGameData().economy.progression.maxLevel,
  };
}

export async function status(accountId: number, uuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, uuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  return snapshot(getPool(), c.id, getNow());
}

const graceMs = FINISH_GRACE_SECONDS * 1000;

export function start(accountId: number, uuid: string, kind: BossKind, body: StartBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: `POST /characters/:uuid/bosses/${kind}/attempts`, requestId: body.request_id,
    payload: { party: body.party_member_uuids ?? [] }, handler: async (ctx) => {
      const maxLevel = getGameData().economy.progression.maxLevel;
      if (ctx.level < maxLevel) throw new AppError(422, `${maxLevel}레벨부터 도전할 수 있습니다.`, 'LEVEL_TOO_LOW', { need: maxLevel });
      let guildId: number | null = null;
      if (kind === 'guild') {
        const m = await guildRepo.membershipOf(ctx.client, ctx.char.id);
        if (!m) throw new AppError(409, '길드에 속해 있지 않습니다.', 'NOT_IN_GUILD');
        const eligibleAt = new Date(m.joined_at.getTime() + GUILD_BOSS_JOIN_WAIT_MS);
        if (ctx.now < eligibleAt) throw new AppError(409, '길드에 들어온 뒤 24시간이 지나야 길드보스에 도전할 수 있습니다.', 'GUILD_BOSS_NOT_YET', { eligible_at: eligibleAt.toISOString() });
        guildId = m.guild_id;
      }
      // 끝 보고 기한이 지난 도전은 닫고, 아직 보고할 수 있는 도전이 있으면 그것부터 끝내게 한다
      await repo.expireStale(ctx.client, ctx.char.id, new Date(ctx.now.getTime() - graceMs), ctx.now);
      const open = await repo.openAttempt(ctx.client, ctx.char.id, kind);
      if (open) throw new AppError(409, '끝나지 않은 도전이 있습니다.', 'BOSS_ATTEMPT_OPEN', { attempt_id: open.uuid });
      const resetDay = new Date(resetBoundaries(ctx.now).dailyStartAt);
      if (await repo.countToday(ctx.client, ctx.char.id, kind, resetDay) >= ATTEMPTS_PER_DAY[kind]) {
        throw new AppError(409, '오늘 도전 횟수를 모두 썼습니다.', 'BOSS_NO_ATTEMPTS');
      }
      const season = await currentSeason(ctx.client, kind, ctx.now);
      let stage: number | undefined;
      if (guildId !== null) stage = (await repo.ensureProgress(ctx.client, season.id, guildId, GUILD_STAGE1_HP, ctx.now)).stage;
      const endsAt = new Date(ctx.now.getTime() + ATTEMPT_SECONDS * 1000);
      const a = await repo.insertAttempt(ctx.client, {
        characterId: ctx.char.id, seasonId: season.id, kind, guildId, resetDay,
        partySize: 1 + (body.party_member_uuids?.length ?? 0), now: ctx.now, endsAt,
      });
      return { status: 201, data: {
        attempt_id: a.uuid, boss_id: season.boss_id, level: BOSS_LEVEL, hp_mul: ROOM_HP_MUL,
        damage_mul: stage ? stageDamageMul(stage) : 1, ...(stage ? { stage } : {}),
        started_at: a.started_at.toISOString(), ends_at: a.ends_at.toISOString(),
      } };
    } });
}

/** 한 번 도전 피해 상한: 착용 장비 화력 상한(attackCap)의 초당 값 x 실제 걸린 시간 x 여유 */
async function damageCap(ctx: EconCtx, elapsedSeconds: number): Promise<number> {
  const eco = getGameData().economy;
  const pol = getConfig().policy;
  const cap = attackCap(eco, pol, ctx.level, await econRepo.listWornKeys(ctx.client, ctx.char.id));
  const dps = (cap / eco.player.attackCooldown) * pol.powerSkillFactor;
  return attemptDamageCap(dps, elapsedSeconds);
}

export function finish(accountId: number, uuid: string, kind: BossKind, attemptUuid: string, body: FinishBody): Promise<StoredResult> {
  return runEconomy({ accountId, characterUuid: uuid, endpoint: `POST /characters/:uuid/bosses/${kind}/attempts/:attemptId/finish`, requestId: body.request_id,
    payload: { attempt: attemptUuid, damage: body.damage_dealt, hits: body.hits_landed, duration: body.duration_s }, handler: async (ctx) => {
      const a = await repo.lockAttempt(ctx.client, ctx.char.id, kind, attemptUuid);
      if (!a) throw new AppError(404, '도전 기록을 찾을 수 없습니다.', 'BOSS_ATTEMPT_NOT_FOUND');
      if (a.state !== 'open') throw new AppError(409, '이미 끝난 도전입니다.', 'BOSS_ATTEMPT_CLOSED');
      const season = await repo.seasonById(ctx.client, a.season_id) as repo.Season;
      if (ctx.now.getTime() > a.ends_at.getTime() + graceMs) {
        // 보고 기한이 지났다: 피해 없이 닫는다(횟수는 이미 썼다)
        await repo.closeAttempt(ctx.client, a.id, 'expired', 0, 0, false, ctx.now);
        return { status: 200, data: { accepted_damage: 0, capped: false, expired: true, best: await repo.bestOf(ctx.client, season.id, ctx.char.id), hp_left: season.hp_left, defeated: season.defeated_at !== null } };
      }
      const elapsed = Math.min(ATTEMPT_SECONDS, (ctx.now.getTime() - a.started_at.getTime()) / 1000);
      const cap = await damageCap(ctx, elapsed);
      // 맞힌 횟수 없이 피해만 오면 받지 않는다
      const reported = body.hits_landed > 0 ? body.damage_dealt : 0;
      const accepted = Math.min(reported, cap);
      const capped = reported > cap;
      if (capped) {
        await econRepo.insertAnomaly(ctx.client, accountId, ctx.char.id, 'boss_damage', 2,
          { kind, attempt: a.uuid, reported: body.damage_dealt, cap, elapsed_s: Math.round(elapsed), duration_s: body.duration_s });
      }
      await repo.closeAttempt(ctx.client, a.id, 'finished', body.damage_dealt, accepted, capped, ctx.now);
      const out: Record<string, unknown> = { accepted_damage: accepted, capped, expired: false, best: await repo.bestOf(ctx.client, season.id, ctx.char.id) };
      // 정산이 끝난 지난 주 시즌에는 기록만 남기고 체력은 건드리지 않는다
      const live = season.settled_at === null;
      if (kind === 'world') {
        const s = live && accepted > 0 ? await repo.hitWorld(ctx.client, season.id, accepted, ctx.now) : season;
        Object.assign(out, { hp_left: s.hp_left, defeated: s.defeated_at !== null });
        out.rank = rankOf((await ranked('world_boss', season.week_start, { fresh: true, db: ctx.client })).rows, ctx.char.id);
      } else {
        const p = await repo.lockProgress(ctx.client, season.id, a.guild_id as number);
        if (p && live && accepted > 0) {
          const next = applyGuildDamage(p.stage, p.hp_left, accepted);
          await repo.saveProgress(ctx.client, {
            ...p, stage: next.stage, stage_hp: next.stageHp, hp_left: next.hpLeft, total_damage: p.total_damage + accepted,
            stage_reached_at: next.advanced ? ctx.now : p.stage_reached_at,
          });
          Object.assign(out, { stage: next.stage, hp_left: next.hpLeft, defeated: next.advanced });
        } else if (p) {
          Object.assign(out, { stage: p.stage, hp_left: p.hp_left, defeated: false });
        }
        out.rank = rankOf((await ranked('guild_boss', season.week_start, { fresh: true, db: ctx.client })).rows, a.guild_id as number);
      }
      return { status: 200, data: out };
    } });
}
