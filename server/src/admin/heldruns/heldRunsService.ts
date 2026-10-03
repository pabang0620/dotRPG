// HR1~HR4: 보류(held) 던전 판 검토. 해제는 기존 finalizeCleared를 그대로 재사용하고(검증은 운영자가 대신 판단했으므로 건너뛴다),
// 불가능한 값(시간, 콤보, 부활)은 서버 한도로 잘라서 점수에 넣는다.
import { getPool } from '../../db/pool';
import { afterCommit } from '../../db/pool';
import { getNotifier } from '../../domains/chat/realtimeNotifier';
import { finalizeCleared, minClearSeconds, runContext } from '../../domains/dungeons/dungeonResult';
import * as dungeonRepo from '../../domains/dungeons/dungeonRepository';
import { EconCtx } from '../../domains/economy/economyContext';
import { lockCharacters } from '../../domains/economy/economyRepository';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './heldRunsRepository';
import type { ReviewBody } from './heldRunsValidation';

const HOLD_TEXT: Record<string, string> = {
  BOSS_ROOM_NOT_REACHED: '보스 방에 도달하지 않은 채 클리어를 보고했습니다.',
  BOSS_NOT_KILLED: '보스 처치 기록이 없습니다.',
  ROOMS_NOT_CLEARED: '앞 방들의 처치 수가 기준에 못 미칩니다.',
  TIME_OVER_SERVER: '보고한 경과 시간이 서버 경과 시간보다 깁니다.',
  TOO_FAST: '최소 클리어 시간보다 빠릅니다.',
  POWER: '처치 기록이 화력 상한을 넘습니다.',
  REVIVES_OVER: '부활 횟수가 난이도 한도를 넘습니다.',
  COMBO_OVER: '콤보 수가 공격 간격으로 가능한 값을 넘습니다.',
  MISMATCH: '파티원들의 결과 보고가 서로 맞지 않습니다.',
  NO_HOST_REPORT: '방장의 보고가 없습니다.',
  NO_WITNESS: '다른 멤버의 보고가 없습니다.',
};

const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);

/** HR1 */
export async function list() {
  const rows = await repo.pending(getPool());
  return {
    runs: rows.map((r) => ({
      id: r.uuid,
      dungeon_id: r.dungeon_id,
      difficulty: r.difficulty,
      character_name: r.character_name,
      hold_reason: r.hold_reason ? r.hold_reason.split(',') : [],
      ended_at: r.ended_at.toISOString(),
      party: r.party,
      waiting_seconds: r.wait_s,
    })),
  };
}

/** HR2: 판 기록, 보류 사유 설명과 임계값, 처치 요약, 이상 기록, 파티 판이면 다른 멤버의 상태 */
export async function detail(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const r = await repo.findRun(db, uuid);
  if (!r) throw new AppError(404, '던전 기록을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
  const run = (await dungeonRepo.findRunById(db, r.id)) as dungeonRepo.RunRow;
  const serverElapsed = r.ended_at ? (r.ended_at.getTime() - r.started_at.getTime()) / 1000 : null;
  const [kills, anomalies, recent, peers] = await Promise.all([
    repo.killSummary(db, r.id),
    repo.anomaliesOfRun(db, uuid),
    repo.recentHeldOfAccount(db, r.account_id),
    r.party_run_id === null ? Promise.resolve([]) : repo.peerRuns(db, r.party_run_id, r.id),
  ]);
  await auditView(admin, ip, 'held_run.view', 'dungeon_run', uuid);
  const reasons = r.hold_reason ? r.hold_reason.split(',') : [];
  return {
    run: {
      id: r.uuid,
      state: r.state,
      review: r.reviewed,
      character: { id: r.character_uuid, name: r.character_name },
      dungeon_id: r.dungeon_id,
      difficulty: r.difficulty,
      started_at: r.started_at.toISOString(),
      ended_at: iso(r.ended_at),
      server_elapsed_s: serverElapsed,
      stats: r.stats,
      room_kills: r.room_kills,
      party_run_id: r.party_run_uuid,
    },
    hold: reasons.map((code) => ({ code, explain: HOLD_TEXT[code] ?? '', ...(code === 'TOO_FAST' ? { min_clear_seconds: minClearSeconds(run) } : {}) })),
    kills: kills.map((k) => ({ monster_id: k.monster_id, count: Number(k.n) })),
    anomalies: anomalies.map((a) => ({ kind: a.kind, severity: a.severity, detail: a.detail, at: a.created_at.toISOString() })),
    other_members: peers.map((p) => ({ run_id: p.uuid, character_name: p.character_name, state: p.state, hold_reason: p.hold_reason })),
    recent_held_of_account: recent.map((x) => ({ run_id: x.uuid, hold_reason: x.hold_reason, at: x.ended_at.toISOString(), decision: x.decision })),
  };
}

/** HR3 해제: 캐릭터 행 -> 판 행 순으로 잠그고 finalizeCleared를 호출한다. 한 판에 한 번만(동시 두 번이면 하나만 통과) */
export function release(admin: AdminCtx, ip: string, uuid: string, body: ReviewBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'held_run.release',
    targetType: 'dungeon_run',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { note_length: body.note.length },
    handler: async (client) => {
      const head = await repo.findRun(client, uuid);
      if (!head) throw new AppError(404, '던전 기록을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
      const [locked] = await lockCharacters(client, [head.character_id]);
      if (!locked) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
      await repo.lockRun(client, head.id);
      const cur = await repo.findRun(client, uuid);
      if (!cur || cur.state !== 'held' || cur.reviewed !== null) throw new AppError(409, '검토할 수 있는 보류 판이 아닙니다.', 'RUN_NOT_HELD');
      const run = (await dungeonRepo.findRunById(client, cur.id)) as dungeonRepo.RunRow;
      const now = getNow();
      const stats = (run.stats ?? {}) as { elapsed_ms?: number; hits_taken?: number; max_combo?: number; revives_used?: number };
      const { eco, diff } = runContext(run);
      const elapsedS = (stats.elapsed_ms ?? 0) / 1000;
      // 시간 점수는 최소 클리어 시간 아래로 내려가지 않게, 콤보·부활은 난이도 한도로 자른다
      const scoredSeconds = Math.max(elapsedS, minClearSeconds(run));
      const maxCombo = Math.max(0, Math.min(stats.max_combo ?? 0, Math.floor(elapsedS / eco.player.attackCooldown)));
      const revivesUsed = Math.max(0, Math.min(stats.revives_used ?? 0, diff.revives));
      await repo.insertReview(client, { runId: cur.id, adminId: admin.id, decision: 'released', note: body.note, prevEndedAt: run.ended_at as Date, requestId: body.request_id });
      const ctx = new EconCtx(client, locked, body.request_id, now);
      const res = await finalizeCleared(ctx, run, {
        hitsTaken: stats.hits_taken ?? 0,
        maxCombo,
        revivesUsed,
        scoredSeconds,
        stats: stats as Record<string, unknown>,
        at: now,
      });
      afterCommit(client, () => {
        getNotifier().systemLine(cur.character_uuid, '보류되었던 던전 보상이 확정되었습니다. 카드를 선택해 주세요.');
      });
      const d = res.data as { rank: string; granted_xp: number; card_count: number; raid?: { reward_locked: boolean; lock_reason?: string } };
      return {
        status: 200,
        data: {
          result: 'cleared',
          rank: d.rank,
          granted_xp: d.granted_xp,
          card_count: d.card_count,
          ...(d.raid?.reward_locked ? { reward_locked: true, lock_reason: d.raid.lock_reason } : {}),
        },
      };
    },
  });
}

/** HR4 거절: 기록만 남긴다(판은 held로 남는다) */
export function reject(admin: AdminCtx, ip: string, uuid: string, body: ReviewBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'held_run.reject',
    targetType: 'dungeon_run',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { note_length: body.note.length },
    handler: async (client) => {
      const head = await repo.findRun(client, uuid);
      if (!head) throw new AppError(404, '던전 기록을 찾을 수 없습니다.', 'RUN_NOT_FOUND');
      await repo.lockRun(client, head.id);
      const cur = await repo.findRun(client, uuid);
      if (!cur || cur.state !== 'held' || cur.reviewed !== null) throw new AppError(409, '검토할 수 있는 보류 판이 아닙니다.', 'RUN_NOT_HELD');
      await repo.insertReview(client, { runId: cur.id, adminId: admin.id, decision: 'rejected', note: body.note, prevEndedAt: cur.ended_at as Date, requestId: body.request_id });
      return { status: 200, data: { result: 'held', decision: 'rejected' } };
    },
  });
}

