// 15단계 G8: 클라이언트(PC·폰) 예외를 DB에 남긴다(진단용). journald에는 요약 한 줄만 남긴다
import { getPool } from '../../db/pool';
import { logger } from '../../utils/logger';
import * as repo from './clientErrorRepository';
import type { ClientErrorBody } from './clientErrorValidation';

export async function report(accountId: number, accountUuid: string, body: ClientErrorBody): Promise<void> {
  await repo.insert(getPool(), accountId, body);
  logger.warn({ account: accountUuid, version: body.version, platform: body.platform, message: body.message.slice(0, 120) }, 'client.error');
}
