// 신고(X1): 증거는 서버가 접수 순간 chat_messages에서 복사한다. 만난 적 있는 사람만 신고할 수 있다.
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { findCharacterByUuid } from '../chat/chatRepository';
import { registry } from '../chat/realtimeNotifier';
import type { StoredResult } from '../economy/economyService';
import { ownedAliveCharacter } from '../friends/friendsRepository';
import { runSocial } from '../social/socialTx';
import * as repo from './reportsRepository';
import type { ReportBody } from './reportsValidation';

export async function submitReport(accountId: number, body: ReportBody): Promise<StoredResult> {
  const cfg = getConfig().social;
  const db = getPool();
  const mine = await ownedAliveCharacter(db, accountId, body.character_id);
  if (!mine) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const target = await findCharacterByUuid(db, body.target);
  if (!target) throw new AppError(404, '모험가를 찾을 수 없습니다.', 'PLAYER_NOT_FOUND');
  if (target.account_id === accountId) throw new AppError(422, '자기 자신에게는 할 수 없습니다.', 'CANNOT_TARGET_SELF');

  return runSocial({
    accountId,
    endpoint: 'POST /reports',
    requestId: body.request_id,
    payload: { character_id: body.character_id, target: body.target, reason: body.reason },
    handler: async (client) => {
      const now = Date.now();
      const session = registry.ofAccount(accountId);
      const shard = session?.shard ?? null;
      // 일반 채팅은 내가 접속한 뒤의 줄만 "내가 본 줄"이다(접속 전 대화를 증거로 끌어오지 않는다)
      const connectedAt = session?.connectedAt ?? new Date(0);
      const ctxSince = new Date(now - cfg.reportContextHours * 3_600_000);
      const met =
        (await repo.targetSpokeInSight(client, accountId, target.account_id, shard, ctxSince, connectedAt)) ||
        (await repo.sharedParty(client, accountId, target.account_id, ctxSince)) ||
        (await repo.areFriends(client, accountId, target.account_id));
      if (!met) throw new AppError(422, '최근에 만난 적 없는 모험가는 신고할 수 없습니다.', 'REPORT_NO_CONTEXT');

      if (
        (await repo.countReports(client, accountId, '1 hour')) >= cfg.reportPerHour ||
        (await repo.countReports(client, accountId, '1 day')) >= cfg.reportPerDay
      ) {
        throw new AppError(429, '신고는 잠시 후에 다시 할 수 있습니다.', 'REPORT_LIMIT');
      }
      const dup = await repo.findOpen(client, accountId, target.account_id, body.reason);
      if (dup) {
        return { status: 200, data: { duplicate: true, report: { id: dup.uuid, line_count: dup.line_count, state: dup.state } } };
      }

      const lines = await repo.collectEvidence(
        client,
        accountId,
        target.account_id,
        shard,
        new Date(now - cfg.reportLookbackMinutes * 60_000),
        getGameData().chat.reportLines,
        cfg.reportTargetLines,
        await repo.blockedAccounts(client, accountId),
        connectedAt,
      );
      await client.query('SAVEPOINT report_insert');
      let saved: { id: number; uuid: string };
      try {
        saved = await repo.insertReport(client, {
          reporter: accountId,
          reporterChar: mine.id,
          target: target.account_id,
          targetChar: target.id,
          targetName: target.name,
          reason: body.reason,
          lineCount: lines.length,
        });
      } catch (err) {
        if (!isUniqueViolation(err, 'reports_open_uq')) throw err;
        await client.query('ROLLBACK TO SAVEPOINT report_insert');
        const again = await repo.findOpen(client, accountId, target.account_id, body.reason);
        if (!again) throw err;
        return { status: 200, data: { duplicate: true, report: { id: again.uuid, line_count: again.line_count, state: again.state } } };
      }
      await repo.insertLines(client, saved.id, lines, target.account_id);
      return { status: 201, data: { report: { id: saved.uuid, line_count: lines.length, state: 'open' } } };
    },
  });
}
