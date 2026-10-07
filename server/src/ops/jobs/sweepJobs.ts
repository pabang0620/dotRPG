// 10단계 작업(설계 11절): 클리어권 만료, 캠페인 상태 정리, 캠페인 미수령 우편 회수.
import { getConfig } from '../../config/env';
import { getPool, withTransaction } from '../../db/pool';
import { invalidateCampaignCache } from '../../domains/mail/campaignCache';
import { expireLot, lockAccount } from '../../domains/sweep/ticketWallet';
import { getNow } from '../../utils/clock';
import type { JobCtx, JobResult } from '../jobRunner';

const BATCH = 200;

/** sweep-ticket-expire: 기한이 지난 이벤트 로트의 remaining을 0으로 만들고 원장 expire를 쓴다(계정 행 -> 로트 행 순서로 잠근다) */
export async function sweepTicketExpireJob(ctx: JobCtx): Promise<JobResult> {
  const now = getNow();
  let lots = 0;
  let tickets = 0;
  while (!ctx.shouldStop()) {
    const due = await getPool().query<{ id: string; account_id: string }>(
      `SELECT id, account_id FROM sweep_ticket_lots WHERE kind = 'event' AND remaining > 0 AND expires_at <= $1 ORDER BY id LIMIT $2`,
      [now, BATCH],
    );
    if (due.rows.length === 0) break;
    for (const row of due.rows) {
      const n = await withTransaction(async (client) => {
        await lockAccount(client, Number(row.account_id));
        return expireLot(client, Number(row.id), now);
      });
      if (n > 0) {
        lots++;
        tickets += n;
      }
    }
    if (due.rows.length < BATCH) break;
  }
  return { rows: lots, detail: { lots, tickets } };
}

/** campaign-sweep: 기간이 끝났거나 상한에 닿은 진행 캠페인을 ended로, 기간이 끝난 미승인 캠페인을 시스템 취소로 */
export async function campaignSweepJob(): Promise<JobResult> {
  const now = getNow();
  const db = getPool();
  const ended = await db.query(
    "UPDATE mail_campaigns SET status = 'ended', ended_at = $1 WHERE status = 'active' AND (ends_at <= $1 OR issued_count >= cap_count)",
    [now],
  );
  const cancelled = await db.query(
    "UPDATE mail_campaigns SET status = 'cancelled', cancelled_at = $1, cancelled_by = NULL, cancel_reason = 'unapproved_expired' WHERE status = 'pending' AND ends_at <= $1",
    [now],
  );
  if ((ended.rowCount ?? 0) + (cancelled.rowCount ?? 0) > 0) invalidateCampaignCache();
  return { rows: (ended.rowCount ?? 0) + (cancelled.rowCount ?? 0), detail: { ended: ended.rowCount ?? 0, cancelled: cancelled.rowCount ?? 0 } };
}

/**
 * campaign-revoke: 취소 때 회수를 요청한 캠페인의 미수령 우편만 기한을 지금으로 앞당긴다(받은 우편은 그대로).
 * 이후 우편은 기존 폐기 틱(6.4)이 mail_expire 원장·소각 기록으로 닫는다. 수령과 겹치면 수령 쪽이 우편 행을 잠가 둘 중 하나만 성립한다.
 * 캠페인 행은 우편 행과 함께 잠그지 않는다(락 순서 ⑤).
 */
export async function campaignRevokeJob(ctx: JobCtx): Promise<JobResult> {
  const now = getNow();
  const batch = getConfig().sweep.campaignRevokeBatch;
  const targets = await getPool().query<{ id: string }>('SELECT id FROM mail_campaigns WHERE revoke_requested AND revoke_done_at IS NULL ORDER BY id');
  let revoked = 0;
  let done = 0;
  for (const t of targets.rows) {
    const id = Number(t.id);
    let total = 0;
    let finished = false;
    while (!ctx.shouldStop()) {
      const r = await getPool().query(
        // mails_time_chk(expires_at > created_at)를 지키려고 같은 시각에 만든 우편은 1ms 뒤로 둔다
        `UPDATE mails SET expires_at = GREATEST($2::timestamptz, created_at + interval '1 millisecond')
          WHERE id IN (SELECT id FROM mails WHERE campaign_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2
                        ORDER BY id LIMIT $3 FOR UPDATE SKIP LOCKED)`,
        [id, now, batch],
      );
      const n = r.rowCount ?? 0;
      total += n;
      if (n < batch) {
        finished = true;
        break;
      }
    }
    revoked += total;
    if (finished) {
      // 건너뛴(잠긴) 우편이 남았으면 다음 실행에서 이어간다
      const left = await getPool().query("SELECT 1 FROM mails WHERE campaign_id = $1 AND claimed_at IS NULL AND expired_at IS NULL AND expires_at > $2 LIMIT 1", [id, now]);
      if ((left.rowCount ?? 0) === 0) {
        await getPool().query('UPDATE mail_campaigns SET revoke_done_at = $2, revoked_count = revoked_count + $3 WHERE id = $1', [id, now, total]);
        done++;
      } else if (total > 0) {
        await getPool().query('UPDATE mail_campaigns SET revoked_count = revoked_count + $2 WHERE id = $1', [id, total]);
      }
    } else if (total > 0) {
      await getPool().query('UPDATE mail_campaigns SET revoked_count = revoked_count + $2 WHERE id = $1', [id, total]);
    }
  }
  return { rows: revoked, detail: { campaigns: targets.rows.length, revoked, done } };
}
