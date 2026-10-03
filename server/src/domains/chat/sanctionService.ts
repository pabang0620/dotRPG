// 제재: 채팅 금지 조회, 자동 제재(금칙어·도배), 접속 중 세션 반영(LISTEN dotrpg_sanction), 미통보 제재 알림
import { Client } from 'pg';
import { getConfig } from '../../config/env';
import { query } from '../../db/pool';
import { logger } from '../../utils/logger';
import type { ChatSession } from './chatSession';
import { registry } from './realtimeNotifier';
import { CLOSE, type Frame } from './wsProtocol';

export type AutoSource = 'auto_filter' | 'auto_spam';

/** 지금 유효한 채팅 금지의 끝 시각(ms), 없으면 0 */
export async function activeMuteUntil(accountId: number): Promise<number> {
  const r = await query<{ ends: Date | null }>(
    `SELECT max(ends_at) AS ends FROM account_sanctions
      WHERE account_id = $1 AND kind = 'chat_mute' AND revoked_at IS NULL AND ends_at > now()`,
    [accountId],
  );
  const ends = r.rows[0]?.ends;
  return ends ? ends.getTime() : 0;
}

/** 서버가 직접 본 사실(금칙어·도배)에 대한 자동 채팅 금지. 24시간 안의 3번째부터는 길게. 정지(ban)는 만들지 않는다 */
export async function createAutoMute(accountId: number, source: AutoSource): Promise<void> {
  const s = getConfig().social;
  const reason = source === 'auto_filter' ? 'filter_strikes' : 'repeat_spam';
  const prior = await query<{ n: string }>(
    `SELECT count(*) AS n FROM account_sanctions
      WHERE account_id = $1 AND kind = 'chat_mute' AND source IN ('auto_filter', 'auto_spam') AND created_at > now() - interval '24 hours'`,
    [accountId],
  );
  const count = Number((prior.rows[0] as { n: string }).n) + 1;
  const minutes = count >= s.chatAutoEscalateCount ? s.chatAutoMuteEscalatedMinutes : s.chatAutoMuteMinutes;
  await query(
    `INSERT INTO account_sanctions (account_id, kind, source, reason_code, ends_at, created_by)
     VALUES ($1, 'chat_mute', $2, $3, now() + ($4::int * interval '1 minute'), 'system')`,
    [accountId, source, reason, minutes],
  );
  await refreshAccount(accountId);
}

const REASON_TEXT: Record<string, string> = {
  filter_strikes: '금지어를 반복해 사용해 채팅이 제한되었습니다.',
  repeat_spam: '같은 말을 반복해 채팅이 제한되었습니다.',
  abuse: '욕설·비하로 제재되었습니다.',
  spam: '도배로 제재되었습니다.',
  scam_ad: '광고·사기로 제재되었습니다.',
  cheat: '불법 프로그램 의심으로 제재되었습니다.',
  other: '운영 정책 위반으로 제재되었습니다.',
};

interface SanctionRow {
  kind: 'warning' | 'chat_mute' | 'ban';
  reason_code: string;
  ends_at: Date | null;
}

/** 아직 알리지 않은 제재를 한 번씩만 가져온다(UPDATE ... RETURNING으로 동시 호출에도 중복 알림이 없다) */
async function claimUnnotified(accountId: number): Promise<SanctionRow[]> {
  const r = await query<SanctionRow>(
    `UPDATE account_sanctions SET notified_at = now()
      WHERE account_id = $1 AND notified_at IS NULL AND revoked_at IS NULL
      RETURNING kind, reason_code, ends_at`,
    [accountId],
  );
  return r.rows;
}

export const sanctionFrame = (r: SanctionRow): Frame => {
  const f: Frame = { t: 'sanction', kind: r.kind, reason_code: r.reason_code, message: REASON_TEXT[r.reason_code] ?? REASON_TEXT.other };
  if (r.ends_at) f.ends_at = r.ends_at.toISOString();
  return f;
};

/** 접속 중인 그 계정의 금지 상태를 DB에서 다시 읽고, 새 제재를 알리고, 정지면 끊는다 */
export async function refreshAccount(accountId: number): Promise<void> {
  const s = registry.ofAccount(accountId);
  if (!s) return;
  s.sanctionMuteUntil = await activeMuteUntil(accountId);
  const fresh = await claimUnnotified(accountId);
  for (const r of fresh) s.send(sanctionFrame(r));
  await checkBan(s);
}

/** accounts.banned_until이 살아 있으면 sanction 후 4003으로 끊는다 */
export async function checkBan(s: ChatSession): Promise<boolean> {
  const r = await query<{ banned_until: Date | null }>('SELECT banned_until FROM accounts WHERE id = $1', [s.accountId]);
  const until = r.rows[0]?.banned_until;
  if (until && until.getTime() > Date.now()) {
    s.send({ t: 'sanction', kind: 'ban', ends_at: until.toISOString(), reason_code: 'other', message: '이용이 정지된 계정입니다.' });
    s.close(CLOSE.BANNED, 'BANNED', false);
    return true;
  }
  return false;
}

// ---------- LISTEN dotrpg_sanction (전용 연결, 끊기면 다시 연결) ----------

let listener: Client | null = null;
let stopped = true;
let retryTimer: NodeJS.Timeout | null = null;

async function connectListener(): Promise<void> {
  const client = new Client({ connectionString: getConfig().databaseUrl });
  client.on('notification', (msg) => {
    const id = Number(msg.payload);
    if (Number.isInteger(id)) refreshAccount(id).catch((err: unknown) => logger.error({ err }, 'sanction refresh failed'));
  });
  const lost = (): void => {
    if (listener === client) listener = null;
    client.removeAllListeners();
    client.end().catch(() => undefined);
    if (!stopped) {
      retryTimer = setTimeout(() => connectListener().catch(() => lost2()), 2000);
      retryTimer.unref();
    }
  };
  const lost2 = (): void => {
    if (!stopped) {
      retryTimer = setTimeout(() => connectListener().catch(() => lost2()), 2000);
      retryTimer.unref();
    }
  };
  client.on('error', lost);
  client.on('end', () => {
    if (listener === client) lost();
  });
  await client.connect();
  await client.query('LISTEN dotrpg_sanction');
  listener = client;
}

export async function startSanctionListener(): Promise<void> {
  stopped = false;
  await connectListener();
}

export async function stopSanctionListener(): Promise<void> {
  stopped = true;
  if (retryTimer) clearTimeout(retryTimer);
  const l = listener;
  listener = null;
  if (l) {
    l.removeAllListeners();
    await l.end().catch(() => undefined);
  }
}

