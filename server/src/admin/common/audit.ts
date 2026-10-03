// 감사 로그(phase7_ops.md 5.5): 변경 작업은 작업과 같은 트랜잭션에서 ok 행을 쓰고(멱등성 기록 겸용),
// 거절·실패·로그인 실패는 롤백되지 않도록 별도로 쓴다. 비밀번호·코드·토큰·채팅 원문은 어디에도 저장하지 않는다.
import type { Request, Response } from 'express';
import type { PoolClient } from 'pg';
import { hashRequest } from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction, type Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import type { AdminCtx, AuditMeta, AuditTarget } from './adminTypes';

export interface AuditRow {
  adminId: number | null;
  loginIdTried?: string | null;
  action: string;
  targetType?: AuditTarget | null;
  targetUuid?: string | null;
  requestId?: string | null;
  result: 'ok' | 'denied' | 'invalid' | 'error';
  errorCode?: string | null;
  params?: Record<string, unknown>;
  response?: unknown;
  ip?: string | null;
}

export const AUDIT_REQUEST_UNIQUE = 'admin_audit_request_uq';

export async function writeAudit(db: Queryable, r: AuditRow): Promise<void> {
  await db.query(
    `INSERT INTO admin_audit_log (admin_id, login_id_tried, action, target_type, target_uuid, request_id, result,
                                  error_code, params, response, ip)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9::jsonb, $10::jsonb, $11)`,
    [
      r.adminId,
      r.loginIdTried ?? null,
      r.action,
      r.targetType ?? null,
      r.targetUuid ?? null,
      r.requestId ?? null,
      r.result,
      r.errorCode ?? null,
      JSON.stringify(r.params ?? {}),
      r.response === undefined ? null : JSON.stringify(r.response),
      r.ip ?? null,
    ],
  );
}

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const asUuid = (v: unknown): string | null => (typeof v === 'string' && UUID_RE.test(v) ? v : null);

/** 민감 조회(계정 상세, 신고 증거 등)도 누가 봤는지 남긴다 */
export async function auditView(
  admin: AdminCtx,
  ip: string,
  action: string,
  targetType: AuditTarget | null,
  targetUuid: string | null,
  params: Record<string, unknown> = {},
): Promise<void> {
  await writeAudit(getPool(), { adminId: admin.id, action, targetType, targetUuid, result: 'ok', params, ip });
}

/** 오류를 감사 로그에 남긴다(별도 트랜잭션: 요청이 롤백돼도 남는다). 기록 실패가 응답을 가리지 않는다 */
export async function auditFailure(req: Request, res: Response, err: unknown): Promise<void> {
  const meta = res.locals.audit as AuditMeta | undefined;
  if (!meta) return;
  const admin = res.locals.admin as AdminCtx | undefined;
  const status = err instanceof AppError ? err.status : 500;
  const result: AuditRow['result'] = status === 401 || status === 403 || status === 423 || status === 429 ? 'denied' : status >= 500 ? 'error' : 'invalid';
  const body = req.body as { login_id?: unknown } | undefined;
  const tried = typeof body?.login_id === 'string' ? body.login_id.slice(0, 64) : null;
  try {
    await writeAudit(getPool(), {
      adminId: admin?.id ?? null,
      loginIdTried: admin ? null : (tried ?? '(none)'),
      action: meta.action,
      targetType: meta.targetType ?? null,
      targetUuid: asUuid(req.params.uuid),
      result,
      errorCode: err instanceof AppError ? (err.code ?? null) : 'INTERNAL',
      params: {},
      ip: req.ip ?? null,
    });
  } catch (e) {
    logger.error({ err: e }, 'audit failure write failed');
  }
}

export interface ActionBody {
  status: number;
  message: string;
  data: unknown;
}

export interface ActionSpec {
  admin: AdminCtx;
  ip: string;
  action: string;
  targetType?: AuditTarget;
  targetUuid?: string | null;
  requestId: string;
  /** 허용 목록 필드만 정리한 요청 값(멱등 비교와 감사 기록용). 비밀값을 넣지 않는다 */
  params: Record<string, unknown>;
  /** 같은 트랜잭션에서 실제 작업을 한다. stored를 주면 data 대신 그것을 저장한다(임시 비밀번호 같은 값을 감사 로그에 남기지 않기 위해) */
  handler: (client: PoolClient) => Promise<{ status: number; message?: string; data: unknown; stored?: unknown; targetUuid?: string | null }>;
}

export interface ActionResult {
  body: ActionBody;
  replay: boolean;
}

interface StoredRow {
  params: Record<string, unknown>;
  action: string;
  response: ActionBody | null;
}

async function findOk(db: Queryable, adminId: number, requestId: string): Promise<StoredRow | null> {
  const r = await db.query<StoredRow>(
    `SELECT params, action, response FROM admin_audit_log WHERE admin_id = $1 AND request_id = $2 AND result = 'ok'`,
    [adminId, requestId],
  );
  return r.rows[0] ?? null;
}

function replayOf(stored: StoredRow, spec: Pick<ActionSpec, 'action' | 'params'>): ActionResult {
  const same = stored.action === spec.action && hashRequest(stored.params) === hashRequest(spec.params);
  if (!same) throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
  return { body: stored.response as ActionBody, replay: true };
}

/**
 * 변경 작업의 공통 틀: 멱등 재생 확인 -> 작업 -> 감사 행(같은 트랜잭션). 작업은 되고 기록은 없는 경우가 없다.
 * 같은 request_id가 동시에 둘 오면 UNIQUE(admin_audit_request_uq)가 하나만 통과시키고 진 쪽은 이긴 쪽의 응답을 돌려준다.
 */
export async function runAdminAction(spec: ActionSpec): Promise<ActionResult> {
  try {
    return await withTransaction(async (client) => {
      // 같은 (관리자, request_id)의 동시 요청은 여기서 줄을 선다: 뒤에 온 쪽은 앞 쪽이 커밋한 뒤 저장된 응답을 본다
      await client.query('SELECT pg_advisory_xact_lock(hashtextextended($1, 0))', [`admin-req:${spec.admin.id}:${spec.requestId}`]);
      const stored = await findOk(client, spec.admin.id, spec.requestId);
      if (stored) return replayOf(stored, spec);
      const r = await spec.handler(client);
      const body: ActionBody = { status: r.status, message: r.message ?? '', data: r.data };
      await writeAudit(client, {
        adminId: spec.admin.id,
        action: spec.action,
        targetType: spec.targetType ?? null,
        targetUuid: r.targetUuid ?? spec.targetUuid ?? null,
        requestId: spec.requestId,
        result: 'ok',
        params: spec.params,
        response: { ...body, data: r.stored !== undefined ? r.stored : r.data },
        ip: spec.ip,
      });
      return { body, replay: false };
    });
  } catch (err) {
    if (isUniqueViolation(err, AUDIT_REQUEST_UNIQUE)) {
      const stored = await findOk(getPool(), spec.admin.id, spec.requestId);
      if (stored) return replayOf(stored, spec);
    }
    throw err;
  }
}

export interface DetachedSpec extends Omit<ActionSpec, 'handler'> {
  /** 오래 걸릴 수 있는 작업(정리 작업 수동 실행). 트랜잭션 밖에서 돌린다: 유휴 트랜잭션 시간 제한(30초)에 걸리지 않게 */
  work: () => Promise<{ status: number; message?: string; data: unknown }>;
}

/** runAdminAction의 변형: 작업은 트랜잭션 밖에서 하고, 끝난 뒤 감사 행(ok)을 쓴다. 같은 request_id의 재전송은 저장된 응답을 돌려준다 */
export async function runAdminActionDetached(spec: DetachedSpec): Promise<ActionResult> {
  const stored = await findOk(getPool(), spec.admin.id, spec.requestId);
  if (stored) return replayOf(stored, spec);
  const r = await spec.work();
  const body: ActionBody = { status: r.status, message: r.message ?? '', data: r.data };
  try {
    await writeAudit(getPool(), {
      adminId: spec.admin.id,
      action: spec.action,
      targetType: spec.targetType ?? null,
      targetUuid: spec.targetUuid ?? null,
      requestId: spec.requestId,
      result: 'ok',
      params: spec.params,
      response: body,
      ip: spec.ip,
    });
  } catch (err) {
    if (isUniqueViolation(err, AUDIT_REQUEST_UNIQUE)) {
      const again = await findOk(getPool(), spec.admin.id, spec.requestId);
      if (again) return replayOf(again, spec);
    }
    throw err;
  }
  return { body, replay: false };
}
