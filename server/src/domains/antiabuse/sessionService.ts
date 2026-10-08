// 계정당 세션 하나(3.3). 같은 계정이 다시 로그인하면 이전 리프레시 가족을 폐기하고, 접속 중인 /ws·/relay 연결을 끊는다.
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import type { AccessSignal } from './deviceRecords';

export const SESSION_CHANNEL = 'dotrpg_session';
/** 알림 payload 의 family 자리에 이 값이 오면 탈퇴 요청이다("<accountId>:withdrawn"): /ws 와 중계 연결을 CLOSE.WITHDRAWN 으로 끊는다 */
export const WITHDRAWN_FAMILY = 'withdrawn';

/**
 * 로그인 성공 직후 같은 트랜잭션에서 부른다. 계정 행을 잠가 동시 로그인을 직렬화한다(정확히 하나의 가족만 살아남는다).
 * SESSION_SINGLE_MODE=off 면 active_* 기록만 하고 이전 세션은 건드리지 않는다.
 */
export async function beginSession(client: PoolClient, accountId: number, familyId: string, s: AccessSignal): Promise<{ replacedOther: boolean }> {
  await client.query('SELECT id FROM accounts WHERE id = $1 FOR UPDATE', [accountId]);
  await client.query(
    `UPDATE accounts SET active_family_id = $2, active_install_id = $3, active_device_hash = $4, active_session_at = now() WHERE id = $1`,
    [accountId, familyId, s.installId, s.deviceKey],
  );
  if (!getConfig().aa.sessionSingle) return { replacedOther: false };
  const ended = await client.query<{ used_at: Date | null; expires_at: Date }>(
    `UPDATE refresh_tokens SET revoked_at = now(), revoke_reason = 'replaced'
      WHERE account_id = $1 AND family_id <> $2 AND revoked_at IS NULL
      RETURNING used_at, expires_at`,
    [accountId, familyId],
  );
  await client.query("UPDATE online_sessions SET ended_at = now(), end_reason = 'replaced' WHERE account_id = $1 AND ended_at IS NULL", [accountId]);
  // 살아 있던(사용 전, 만료 전) 토큰이 있었으면 다른 곳의 세션을 종료시킨 것으로 본다
  const now = Date.now();
  const replacedOther = ended.rows.some((r) => r.used_at === null && r.expires_at.getTime() > now);
  // NOTIFY는 트랜잭션이 커밋될 때 전달된다: 새 가족이 DB에 보이는 뒤에야 리스너가 받는다
  await client.query('SELECT pg_notify($1, $2)', [SESSION_CHANNEL, `${accountId}:${familyId}`]);
  return { replacedOther };
}
