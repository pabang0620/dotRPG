// LISTEN dotrpg_session(전용 연결, 끊기면 다시 연결): 다른 곳에서 로그인한 계정의 옛 /ws·/relay 연결을 끊는다(3.3의 5번).
// 새 세션의 연결은 familyId가 같아 닫히지 않는다(통지가 새 세션 연결 뒤에 늦게 도착해도 안전하다).
import { Client } from 'pg';
import { getConfig } from '../../config/env';
import { logger } from '../../utils/logger';
import { registry } from '../chat/realtimeNotifier';
import { CLOSE } from '../chat/wsProtocol';
import { relayHub } from '../relay/relayHub';
import { SESSION_CHANNEL, WITHDRAWN_FAMILY } from './sessionService';

let listener: Client | null = null;
let stopped = true;
let retryTimer: NodeJS.Timeout | null = null;

/** 통지 한 건을 처리한다(테스트가 직접 부르기도 한다) */
export function handleSessionNotice(payload: string): void {
  const [idRaw, familyId] = payload.split(':');
  const accountId = Number(idRaw);
  if (!Number.isInteger(accountId) || !familyId) return;
  const s = registry.ofAccount(accountId);
  // 탈퇴 요청: 재연결하지 않는 코드로 /ws 와 중계 연결을 모두 닫는다(REPLACED 경로와 구분)
  if (familyId === WITHDRAWN_FAMILY) {
    s?.close(CLOSE.WITHDRAWN, 'WITHDRAWN', false);
    relayHub().kickAccount(accountId, CLOSE.WITHDRAWN, 'WITHDRAWN');
    return;
  }
  if (s && s.familyId !== familyId) {
    s.close(CLOSE.REPLACED, 'SESSION_REPLACED', false);
  }
  // 중계 연결에는 가족 id가 없다: 이 계정의 중계 연결을 모두 끊는다. 새 세션은 티켓을 새로 받아 다시 붙는다
  relayHub().kickAccount(accountId, CLOSE.REPLACED, 'SESSION_REPLACED');
}

async function connectListener(): Promise<void> {
  const client = new Client({ connectionString: getConfig().databaseUrl });
  client.on('notification', (msg) => {
    if (msg.payload) {
      try {
        handleSessionNotice(msg.payload);
      } catch (err) {
        logger.error({ err }, 'session notice failed');
      }
    }
  });
  const schedule = (): void => {
    if (!stopped) {
      retryTimer = setTimeout(() => connectListener().catch(schedule), 2000);
      retryTimer.unref();
    }
  };
  const lost = (): void => {
    if (listener === client) listener = null;
    client.removeAllListeners();
    client.end().catch(() => undefined);
    schedule();
  };
  client.on('error', lost);
  client.on('end', () => {
    if (listener === client) lost();
  });
  await client.connect();
  await client.query(`LISTEN ${SESSION_CHANNEL}`);
  listener = client;
}

export async function startSessionListener(): Promise<void> {
  stopped = false;
  await connectListener();
}

export async function stopSessionListener(): Promise<void> {
  stopped = true;
  if (retryTimer) clearTimeout(retryTimer);
  const l = listener;
  listener = null;
  if (l) {
    l.removeAllListeners();
    await l.end().catch(() => undefined);
  }
}
