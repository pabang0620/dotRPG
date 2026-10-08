// 관리자 탈퇴 도구 WD1~WD8(설계 14절). 변경 작업은 runAdminAction(멱등 + 감사 행 같은 트랜잭션)을 지난다.
// 감사 params 허용 목록: note_len, cancel_allowed, override_deferral, on (+ tombstone_id). 메모 원문은 계정 메모(admin_account_notes)에만 남긴다.
import { getPool, type Queryable } from '../../db/pool';
import { MINUTE, consumeOrThrow } from '../../middleware/rateLimiter';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { anonymizeOne } from '../../domains/withdrawal/anonymizeService';
import { cancelWithdrawal, startWithdrawal, withdrawalView } from '../../domains/withdrawal/withdrawalCore';
import * as idRepo from '../../domains/withdrawal/withdrawalIdentity';
import * as wrepo from '../../domains/withdrawal/withdrawalRepository';
import { assertEnabled } from '../../domains/withdrawal/withdrawalService';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as repo from './withdrawalsRepository';
import type { AnonymizeNowBody, CancelBody, HoldBody, ListQuery, ReleaseBody, StartBody, TombstoneQuery } from './withdrawalsValidation';

const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);
const DAY_MS = 86_400_000;

const listView = (w: repo.ListRow, now: Date) => ({
  id: w.uuid,
  account_id: w.account_uuid,
  state: w.state,
  source: w.source,
  requested_at: w.requested_at.toISOString(),
  due_at: w.due_at.toISOString(),
  defer_reasons: w.defer_reasons,
  defer_checked_at: iso(w.defer_checked_at),
  manual_hold: w.manual_hold,
  anonymized_at: iso(w.anonymized_at),
  remaining_days: w.state === 'requested' ? Math.max(0, Math.ceil((w.due_at.getTime() - now.getTime()) / DAY_MS)) : null,
});

const rowView = (w: wrepo.WithdrawalRow, now: Date) => ({
  id: w.uuid,
  state: w.state,
  source: w.source,
  requested_at: w.requested_at.toISOString(),
  due_at: w.due_at.toISOString(),
  cancel_allowed: w.cancel_allowed,
  ack_paid_loss: w.ack_paid_loss,
  loss_snapshot: w.loss_snapshot,
  defer_reasons: w.defer_reasons,
  defer_checked_at: iso(w.defer_checked_at),
  manual_hold: w.manual_hold,
  manual_hold_note: w.manual_hold_note,
  cancelled_at: iso(w.cancelled_at),
  cancelled_via: w.cancelled_via,
  anonymized_at: iso(w.anonymized_at),
  retain_until: iso(w.retain_until),
  remaining_days: w.state === 'requested' ? Math.max(0, Math.ceil((w.due_at.getTime() - now.getTime()) / DAY_MS)) : null,
});

/** WD1 */
export async function listWithdrawals(q: ListQuery) {
  const now = getNow();
  const rows = await repo.list(getPool(), {
    state: q.state,
    deferred: q.deferred === undefined ? undefined : q.deferred === 'true',
    dueBefore: q.due_before ? new Date(q.due_before) : undefined,
    cursor: q.cursor,
    limit: q.limit,
  });
  const page = rows.slice(0, q.limit);
  return { items: page.map((w) => listView(w, now)), next_cursor: rows.length > q.limit ? (page[page.length - 1] as repo.ListRow).uuid : null };
}

/** WD2: 계정의 탈퇴 이력과 현재 상태, 보류 사유별 근거 건수. 감사 withdrawal.view */
export async function accountWithdrawal(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const acc = await repo.accountBrief(db, uuid);
  if (!acc) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
  const now = getNow();
  const history = await wrepo.historyOf(db, acc.id, 50);
  const evidence = await repo.evidenceCounts(db, acc.id, now);
  await auditView(admin, ip, 'withdrawal.view', 'account', uuid);
  const current = history.find((w) => w.state === 'requested') ?? history.find((w) => w.state === 'completed') ?? null;
  return {
    account: { id: acc.uuid, withdrawn: acc.deleted_at !== null, anonymized: acc.anonymized_at !== null },
    current: current ? rowView(current, now) : null,
    history: history.map((w) => rowView(w, now)),
    defer_evidence: evidence,
  };
}

const note = async (db: Queryable, accountId: number, admin: AdminCtx, label: string, text: string): Promise<void> => {
  await repo.addNote(db, accountId, admin.id, `[탈퇴 ${label}] ${text}`);
};

/** WD3: 운영자 대행 요청(정보주체 요청을 지원 채널로 받은 경우). 정지 계정도 가능, 재인증·확인 문구 없음 */
export async function startForAccount(admin: AdminCtx, ip: string, uuid: string, body: StartBody): Promise<ActionResult> {
  assertEnabled();
  const cancelAllowed = body.cancel_allowed !== false;
  if (!cancelAllowed && admin.role !== 'owner') throw new AppError(403, '권한이 없습니다.', 'FORBIDDEN_ROLE');
  return runAdminAction({
    admin,
    ip,
    action: 'withdrawal.start',
    targetType: 'account',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { note_len: body.note.length, cancel_allowed: cancelAllowed },
    handler: async (client) => {
      const acc = await repo.accountBrief(client, uuid);
      if (!acc) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
      const r = await startWithdrawal(client, { accountId: acc.id, requestId: body.request_id, source: 'admin', adminId: admin.id, now: getNow(), cancelAllowed });
      await note(client, acc.id, admin, '대행 요청', body.note);
      return { status: 201, data: { withdrawal: withdrawalView(r.row), logged_out: true }, targetUuid: uuid };
    },
  });
}

async function openByUuid(client: Queryable, wuuid: string): Promise<wrepo.WithdrawalRow> {
  const w = await wrepo.byUuid(client, wuuid);
  if (!w) throw new AppError(404, '탈퇴 요청을 찾을 수 없습니다.', 'NOT_FOUND');
  return w;
}

/** WD4: 유예 중 운영자 취소. 기한이 지난 뒤에는 409 WITHDRAWAL_DUE */
export async function cancelByAdmin(admin: AdminCtx, ip: string, wuuid: string, body: CancelBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'withdrawal.cancel',
    requestId: body.request_id,
    params: { note_len: body.note.length },
    handler: async (client) => {
      const w = await openByUuid(client, wuuid);
      if (w.state === 'completed') throw new AppError(409, '이미 익명화되었습니다.', 'ALREADY_ANONYMIZED');
      const accountUuid = (await repo.accountUuidOf(client, w.account_id)) as string;
      const r = await cancelWithdrawal(client, { accountId: w.account_id, via: 'admin', cancelRequestId: body.request_id, now: getNow() });
      await note(client, w.account_id, admin, '운영자 취소', body.note);
      return { status: 200, data: { cancelled: true, renamed_characters: r.renamed_characters }, targetUuid: accountUuid };
    },
  });
}

/** WD5: 유예를 건너뛰고 즉시 익명화(owner). 보류 사유가 있는데 override 가 아니면 409 DEFERRED */
export async function anonymizeNow(admin: AdminCtx, ip: string, wuuid: string, body: AnonymizeNowBody): Promise<ActionResult> {
  consumeOrThrow(`admin-wd5:${admin.id}`, 5, MINUTE);
  const override = body.override_deferral === true;
  return runAdminAction({
    admin,
    ip,
    action: 'withdrawal.anonymize_now',
    requestId: body.request_id,
    params: { note_len: body.note.length, override_deferral: override },
    handler: async (client) => {
      const w = await openByUuid(client, wuuid);
      if (w.state === 'completed') throw new AppError(409, '이미 익명화되었습니다.', 'ALREADY_ANONYMIZED');
      if (w.state !== 'requested') throw new AppError(404, '열린 탈퇴 요청이 없습니다.', 'NO_PENDING_WITHDRAWAL');
      const accountUuid = (await repo.accountUuidOf(client, w.account_id)) as string;
      const r = await anonymizeOne(client, w.id, { now: getNow(), mode: 'admin', override });
      if (r.status === 'deferred') throw new AppError(409, '보류 사유가 있어 즉시 익명화할 수 없습니다.', 'DEFERRED', { defer_reasons: r.reasons });
      if (r.status === 'skipped') throw new AppError(409, '지금은 익명화할 수 없는 상태입니다.', 'NO_PENDING_WITHDRAWAL');
      await note(client, w.account_id, admin, '즉시 익명화', body.note);
      return { status: 200, data: { anonymized: true, forced: r.forced, tombstone: r.tombstone }, targetUuid: accountUuid };
    },
  });
}

/** WD6: 운영자 보류 켜기·끄기(manual 사유). 작업은 보류 중인 요청을 건너뛴다 */
export async function setHold(admin: AdminCtx, ip: string, wuuid: string, body: HoldBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'withdrawal.hold',
    requestId: body.request_id,
    params: { note_len: body.note.length, on: body.on },
    handler: async (client) => {
      const head = await openByUuid(client, wuuid);
      // 락 순서: 캐릭터 -> 계정 -> 요청 행. 익명화 작업과 같은 순서로 잠근다
      await wrepo.lockAllCharacters(client, head.account_id);
      await wrepo.lockAccount(client, head.account_id);
      const w = await wrepo.lockById(client, head.id);
      if (!w || w.state !== 'requested') throw new AppError(409, '열린 탈퇴 요청이 아닙니다.', 'NO_PENDING_WITHDRAWAL');
      await wrepo.setManualHold(client, w.id, body.on);
      await repo.setHoldNote(client, w.id, body.on ? body.note : null);
      await note(client, w.account_id, admin, body.on ? '보류 켬' : '보류 끔', body.note);
      const accountUuid = (await repo.accountUuidOf(client, w.account_id)) as string;
      return { status: 200, data: { manual_hold: body.on }, targetUuid: accountUuid };
    },
  });
}

/** WD7: Steam ID 로 이월 표시 조회. 서버가 HMAC 을 계산하고 입력 원문은 응답·로그·감사 params 에 남기지 않는다(해시 앞 8자만) */
export async function findTombstone(admin: AdminCtx, ip: string, q: TombstoneQuery) {
  const hash = idRepo.identityHash(q.steam);
  const t = await idRepo.findAnyByHash(getPool(), hash);
  await auditView(admin, ip, 'tombstone.view', null, null, {});
  const now = getNow();
  return {
    found: t !== null,
    hash_prefix: hash.slice(0, 8),
    tombstone: t
      ? {
          id: t.uuid,
          carry_ban_until: iso(t.carry_ban_until),
          carry_payment_block: t.carry_payment_block,
          carry_econ_hold: t.carry_econ_hold,
          created_at: t.created_at.toISOString(),
          expires_at: t.expires_at.toISOString(),
          expired: t.expires_at.getTime() <= now.getTime(),
          released: t.released_at !== null,
          released_at: iso(t.released_at),
          released_by: t.released_by,
        }
      : null,
  };
}

/** WD8: 이월 해제(owner) */
export async function releaseTombstone(admin: AdminCtx, ip: string, tuuid: string, body: ReleaseBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'tombstone.release',
    requestId: body.request_id,
    params: { note_len: body.note.length, tombstone_id: tuuid },
    handler: async (client) => {
      const t = await idRepo.lockByUuid(client, tuuid);
      if (!t) throw new AppError(404, '이월 표시를 찾을 수 없습니다.', 'NOT_FOUND');
      if (t.released_at) return { status: 200, data: { released: true, already: true } };
      await idRepo.release(client, t.id, admin.loginId, getNow());
      return { status: 200, data: { released: true, already: false } };
    },
  });
}

