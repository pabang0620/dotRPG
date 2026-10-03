// 점검 공지(phase7_ops.md 4.3): 서버가 만드는 문장만 chat.sys로 보낸다. 시작 시각에는 bye(MAINTENANCE)로 접속을 닫는다.
import { getConfig } from '../config/env';
import { CLOSE } from '../domains/chat/wsProtocol';
import { closeAllSessions, getNotifier } from '../domains/chat/realtimeNotifier';
import { getNow } from '../utils/clock';
import { logger } from '../utils/logger';
import { formatKstHm } from '../utils/resetBoundaries';
import { getMaintenance, maintRetryAfterMs, maintPhase, type MaintWindow } from './maintenanceState';

export interface Mark {
  key: string;
  at: Date;
  text: string;
}

const BLOCK_TEXT = '지금부터 새 던전 입장과 새 로그인이 제한됩니다. 진행 중인 던전은 끝까지 하실 수 있습니다.';
export const START_TEXT = '서버 점검을 시작합니다. 잠시 후 접속이 종료됩니다.';

/** 공지가 나갈 시각 목록(시작 N분 전들, block_login_at, 시작). block_login_at이 N분 전과 같은 시각이면 한 줄로 합친다 */
export function computeMarks(w: MaintWindow, announceMinutes: number[]): Mark[] {
  const marks: Mark[] = [];
  const startMs = w.startsAt.getTime();
  const blockMs = w.blockLoginAt.getTime();
  let blockMerged = false;
  for (const n of announceMinutes) {
    const at = startMs - n * 60_000;
    if (at >= startMs) continue;
    let text = `서버 점검이 ${n}분 뒤(${formatKstHm(w.startsAt)})에 시작됩니다. 예상 종료 ${formatKstHm(w.endsAt)}.`;
    if (w.notice) text += ` ${w.notice}`;
    if (Math.abs(at - blockMs) < 1000) {
      text += ` ${BLOCK_TEXT}`;
      blockMerged = true;
    }
    marks.push({ key: `${w.uuid}:m${n}`, at: new Date(at), text });
  }
  if (!blockMerged && blockMs < startMs) marks.push({ key: `${w.uuid}:block`, at: w.blockLoginAt, text: BLOCK_TEXT });
  marks.push({ key: `${w.uuid}:start`, at: w.startsAt, text: START_TEXT });
  return marks.sort((a, b) => a.at.getTime() - b.at.getTime());
}

const sent = new Set<string>();
let armed: { uuid: string; startsAt: number; at: number } | null = null;

export function resetAnnouncer(): void {
  sent.clear();
  armed = null;
}

export interface AnnounceResult {
  texts: string[];
  byeSent: boolean;
}

/** 지금 시각 기준으로 나갈 공지를 보낸다(1초마다 부른다). 이미 보낸 시점과 창을 알기 전에 지난 시점은 건너뛴다 */
export function announceTick(now: Date = getNow()): AnnounceResult {
  const res: AnnounceResult = { texts: [], byeSent: false };
  const w = getMaintenance();
  if (!w) {
    armed = null;
    return res;
  }
  if (!armed || armed.uuid !== w.uuid || armed.startsAt !== w.startsAt.getTime()) {
    armed = { uuid: w.uuid, startsAt: w.startsAt.getTime(), at: now.getTime() };
  }
  const t = now.getTime();
  const cfg = getConfig().maint;
  for (const m of computeMarks(w, cfg.announceMinutes)) {
    if (sent.has(m.key) || m.at.getTime() > t) continue;
    sent.add(m.key);
    if (m.at.getTime() < armed.at - 1000) continue;
    if (t >= w.endsAt.getTime()) continue;
    getNotifier().systemBroadcast(m.text);
    res.texts.push(m.text);
  }
  const byeKey = `${w.uuid}:bye`;
  if (!sent.has(byeKey) && maintPhase(now) === 'active' && t >= w.startsAt.getTime() + cfg.byeDelaySeconds * 1000) {
    sent.add(byeKey);
    closeAllSessions(CLOSE.GOING_AWAY, 'MAINTENANCE', true, () => maintRetryAfterMs(now));
    res.byeSent = true;
  }
  return res;
}

export function startAnnouncer(): () => void {
  const timer = setInterval(() => {
    try {
      announceTick();
    } catch (err) {
      logger.error({ err }, 'maintenance announce failed');
    }
  }, 1000);
  timer.unref();
  return () => clearInterval(timer);
}
