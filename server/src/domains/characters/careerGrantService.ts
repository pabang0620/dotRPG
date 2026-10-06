// 전직·각성의 서버 기록(8절). 서버가 승급과 각성 단계를 직접 기록하고 PUT state는 그 기록과 일치하는 값만 받는다.
// 모두 runEconomy 틀(캐릭터 행 잠금, request_log 멱등성 재생, 같은 request_id에 다른 본문이면 422 IDEMPOTENCY_MISMATCH)을 쓴다.
import { getConfig } from '../../config/env';
import { getCareerTrials } from '../../gamedata/antiAbuseData';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { isFresh } from '../antiabuse/presenceService';
import { readOnline } from '../antiabuse/presenceRepository';
import { inPartyRun } from '../dungeons/dungeonRepository';
import type { EconCtx } from '../economy/economyContext';
import { AnomalyError, runEconomy, type EconResult, type StoredResult } from '../economy/economyService';
import { activeSessionIdOf } from '../fieldsessions/fieldRepository';
import type { CareerState } from './careerRules';
import * as repo from './careerGrantRepository';
import type { AdvanceBody, PromoteBody, TrialFinishBody, TrialStartBody } from './careerGrantValidation';

const NOT_IN_TOWN = () => new AppError(409, '마을에서만 할 수 있습니다.', 'NOT_IN_TOWN');
const NOT_PROMOTED = () => new AppError(409, '아직 전직하지 않았습니다.', 'NOT_PROMOTED');

/** maps.json의 안전한 마을(instanced=false, safe=true) */
export function isTownMap(mapId: string): boolean {
  const m = getGameData().maps.get(mapId);
  return !!m && !m.instanced && m.safe === true;
}

/** 프레즌스가 신선하고 마을에 있다. 아니면 409 NOT_IN_TOWN. 시련 성공은 시도 내내 같은 마을에 있었는지도 본다(sinceMapAt) */
async function assertInTown(ctx: EconCtx, trialStartedAt?: Date): Promise<void> {
  const row = await readOnline(ctx.client, ctx.char.accountId);
  const mine = row && row.character_id === ctx.char.id ? row : null;
  if (!isFresh(mine, ctx.now, getConfig().aa.presence.onlineSeconds) || !isTownMap((mine as NonNullable<typeof mine>).map_id)) throw NOT_IN_TOWN();
  if (trialStartedAt && (mine as NonNullable<typeof mine>).map_since.getTime() > trialStartedAt.getTime()) throw NOT_IN_TOWN();
}

const baseClassOf = (career: number): 'warrior' | 'mage' => (career <= 2 ? 'warrior' : 'mage');

/** C1 POST /characters/{uuid}/career/promote */
export function promote(accountId: number, characterUuid: string, body: PromoteBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/career/promote',
    requestId,
    payload,
    handler: async (ctx): Promise<EconResult> => {
      const cfg = getConfig().aa.career;
      if (ctx.level < cfg.promoteMinLevel) throw new AppError(422, '레벨이 부족합니다.', 'LEVEL_TOO_LOW', { need: cfg.promoteMinLevel, have: ctx.level });
      if (baseClassOf(body.career) !== ctx.char.class) throw new AppError(422, '이 직업으로는 전직할 수 없습니다.', 'CAREER_BASE_MISMATCH');
      if (await repo.getGrant(ctx.client, ctx.char.id)) throw new AppError(409, '이미 전직했습니다.', 'ALREADY_PROMOTED');
      await repo.insertGrant(ctx.client, ctx.char.id, body.career, ctx.now);
      // character_state.career는 서버가 쓴다. 훈련·환급 값은 저장된 상태에서 가져온다(없으면 validateCareer와 같은 기본값)
      const st = await repo.stateCareer(ctx.client, ctx.char.id);
      const tree = getGameData().passive;
      const trainingDefault = [...new Set(st.passives.filter((x) => x !== 'start' && tree.nodes.has(x)))];
      const next: CareerState = {
        schema: 1,
        career: body.career,
        nodes: [],
        training: st.career?.training ?? trainingDefault,
        refunded: st.career?.refunded ?? Math.min(ctx.level - 1, trainingDefault.length),
        questStage: 0,
        awakened: false,
      };
      await repo.writeStateCareer(ctx.client, ctx.char.id, next);
      return { status: 200, data: { career: body.career, stage: 0, promoted_at: ctx.now.toISOString() } };
    },
  });
}

/** C2 POST .../career/awakening/trial/start */
export function trialStart(accountId: number, characterUuid: string, body: TrialStartBody): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/career/awakening/trial/start',
    requestId: body.request_id,
    payload: {},
    handler: async (ctx): Promise<EconResult> => {
      const cfg = getConfig().aa.career;
      const grant = await repo.getGrant(ctx.client, ctx.char.id);
      if (!grant) throw NOT_PROMOTED();
      if (grant.stage !== 2) throw new AppError(409, '지금은 시련을 시작할 수 없습니다.', 'STAGE_MISMATCH', { stage: grant.stage });
      await assertInTown(ctx);
      if ((await inPartyRun(ctx.client, ctx.char.id)) || (await activeSessionIdOf(ctx.client, ctx.char.id)) !== null) {
        throw new AppError(409, '파티 콘텐츠 중에는 시작할 수 없습니다.', 'IN_PARTY_CONTENT');
      }
      // 직업별 필수 노드(careers.json trials, Unity 내보내기가 채운다). 항목이 없으면 검사를 건너뛴다
      const trial = getCareerTrials()?.[String(grant.career)];
      if (trial && trial.requiredNodes.length > 0) {
        const st = await repo.stateCareer(ctx.client, ctx.char.id);
        const have = new Set((st.career?.nodes ?? []).map((n) => n.id));
        const missing = trial.requiredNodes.filter((id) => !have.has(id));
        if (missing.length > 0) throw new AppError(422, '시련에 필요한 노드가 없습니다.', 'NODES_REQUIRED', { missing });
      }
      let open = await repo.openTrial(ctx.client, ctx.char.id);
      // 마감이 지난 열린 시도는 만료로 닫고 새로 시작한다(성공 보고가 TRIAL_EXPIRED로 거절되면 다시 시작해야 한다)
      if (open && ctx.now.getTime() - open.started_at.getTime() > cfg.trialMaxSeconds * 1000) {
        await repo.closeTrial(ctx.client, open.id, 'expired', ctx.now);
        open = null;
      }
      if (!open) {
        await repo.insertTrial(ctx.client, ctx.char.id, grant.career, ctx.now);
        open = { id: 0, career: grant.career, started_at: ctx.now };
      }
      // 시험 시간 상한만 알린다. 최소 시간은 알리지 않는다
      return { status: 200, data: { started_at: open.started_at.toISOString(), expires_in_seconds: cfg.trialMaxSeconds } };
    },
  });
}

/** C3 POST .../career/awakening/trial/finish */
export function trialFinish(accountId: number, characterUuid: string, body: TrialFinishBody): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/career/awakening/trial/finish',
    requestId: body.request_id,
    payload: { result: body.result },
    handler: async (ctx): Promise<EconResult> => {
      const cfg = getConfig().aa.career;
      const grant = await repo.getGrant(ctx.client, ctx.char.id);
      if (!grant) throw NOT_PROMOTED();
      if (grant.stage !== 2) throw new AppError(409, '지금은 시련을 마칠 수 없습니다.', 'STAGE_MISMATCH', { stage: grant.stage });
      const open = await repo.openTrial(ctx.client, ctx.char.id);
      if (!open) throw new AppError(409, '시작한 시련이 없습니다.', 'TRIAL_NOT_STARTED');
      if (body.result === 'fail') {
        await repo.closeTrial(ctx.client, open.id, 'fail', ctx.now);
        return { status: 200, data: { stage: 2 } };
      }
      // 성공 검사(순서대로, 첫 실패에서 중단). 오류 응답은 롤백되므로 열린 시도는 그대로 남는다(만료는 다음 C2가 닫는다)
      const elapsedMs = ctx.now.getTime() - open.started_at.getTime();
      if (elapsedMs > cfg.trialMaxSeconds * 1000) throw new AppError(409, '시련 시간이 지났습니다. 다시 시작해 주세요.', 'TRIAL_EXPIRED');
      const minSeconds = getCareerTrials()?.[String(grant.career)]?.minSeconds ?? cfg.trialMinSeconds;
      if (elapsedMs < minSeconds * 1000) {
        throw new AnomalyError(422, '시련 결과를 인정할 수 없습니다.', 'TRIAL_TOO_FAST', {
          kind: 'career_state',
          severity: 2,
          detail: { why: 'trial_too_fast', career: grant.career, elapsed_ms: elapsedMs },
        });
      }
      await assertInTown(ctx, open.started_at);
      await repo.closeTrial(ctx.client, open.id, 'success', ctx.now);
      await repo.setStage(ctx.client, ctx.char.id, 3, ctx.now);
      await writeStage(ctx, 3);
      return { status: 200, data: { stage: 3 } };
    },
  });
}

/** C4 POST .../career/awakening/advance */
export function advance(accountId: number, characterUuid: string, body: AdvanceBody): Promise<StoredResult> {
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/career/awakening/advance',
    requestId: body.request_id,
    payload: { from_stage: body.from_stage },
    handler: async (ctx): Promise<EconResult> => {
      const cfg = getConfig().aa.career;
      if (body.from_stage === 2) throw new AppError(422, '시련은 시련 결과 보고로만 넘어갑니다.', 'TRIAL_REQUIRED');
      const grant = await repo.getGrant(ctx.client, ctx.char.id);
      if (!grant) throw NOT_PROMOTED();
      if (grant.stage !== body.from_stage) throw new AppError(409, '현재 단계와 다릅니다.', 'STAGE_MISMATCH', { stage: grant.stage });
      const sinceMs = ctx.now.getTime() - grant.stage_changed_at.getTime();
      if (sinceMs < cfg.stageMinGapSeconds * 1000) {
        const retry = Math.max(1, Math.ceil((cfg.stageMinGapSeconds * 1000 - sinceMs) / 1000));
        throw new AppError(429, '너무 빠릅니다. 잠시 후 다시 시도해 주세요.', 'STAGE_TOO_FAST', { retry_after_sec: retry });
      }
      await assertInTown(ctx);
      const next = grant.stage + 1;
      await repo.setStage(ctx.client, ctx.char.id, next, ctx.now);
      await writeStage(ctx, next);
      return { status: 200, data: { stage: next, awakened: next === 5 } };
    },
  });
}

/** character_state.career의 questStage(와 각성 값)를 서버 기록과 맞춘다 */
async function writeStage(ctx: EconCtx, stage: number): Promise<void> {
  const st = await repo.stateCareer(ctx.client, ctx.char.id);
  if (!st.career) return;
  await repo.writeStateCareer(ctx.client, ctx.char.id, { ...st.career, questStage: stage, awakened: stage === 5 });
}
