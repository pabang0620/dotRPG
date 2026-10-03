// 감시자(phase7_ops.md 8.3): 매분 규칙을 평가해 웹훅으로 알린다. ALERT_WEBHOOK_URL이 없으면 로그에만 남긴다.
// 같은 알림 키는 ALERT_MIN_INTERVAL_MINUTES 안에 반복하지 않고, 해소되면 한 번 알린다. 하트비트(OPS_HEARTBEAT_URL)도 매분 보낸다.
import { getConfig } from '../config/env';
import { getPool } from '../db/pool';
import { getNow } from '../utils/clock';
import { logger } from '../utils/logger';
import { evaluate, type Alert } from './alertRules';
import { metrics } from './metrics';
import { collectSnapshot, type Snapshot } from './snapshot';
import { getDomainReady } from './watchdogReady';

const lastSent = new Map<string, number>();
const active = new Map<string, Alert>();
let readyFailStreak = 0;
let lastClose = 0;
let lastSnapshotLogAt = 0;

/** 웹훅 본문(discord: content, slack: text, json: 원본) */
export function formatAlert(a: Alert | { title: string; detail: string }, kind: 'firing' | 'resolved', format: 'discord' | 'slack' | 'json', server: string): unknown {
  const level = 'level' in a ? (a.level === 'critical' ? '긴급' : '경고') : '';
  const head = kind === 'resolved' ? `[해소] ${server}` : `[${level}] ${server}`;
  const text = `${head} ${a.title}\n${a.detail}`;
  if (format === 'discord') return { content: text.slice(0, 1900) };
  if (format === 'slack') return { text };
  return { server, kind, title: a.title, detail: a.detail, ...('level' in a ? { level: a.level } : {}) };
}

/** 알림 한 건 전송. URL이 없으면 로그만. 실패해도 던지지 않는다 */
export async function sendAlert(a: Alert, kind: 'firing' | 'resolved'): Promise<'sent' | 'logged'> {
  const cfg = getConfig().ops;
  logger.warn({ alert: a.key, level: a.level, kind, title: a.title, detail: a.detail }, 'alert.sent');
  if (!cfg.alertWebhookUrl) return 'logged';
  try {
    const res = await fetch(cfg.alertWebhookUrl, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(formatAlert(a, kind, cfg.alertWebhookFormat, cfg.serverName)),
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) logger.error({ status: res.status }, 'alert webhook rejected');
  } catch (err) {
    logger.error({ err }, 'alert webhook failed');
  }
  return 'sent';
}

async function latestIntegrityMismatches(): Promise<number | null> {
  try {
    const r = await getPool().query<{ m: string | null }>(
      `SELECT detail->>'mismatches' AS m FROM job_runs WHERE job = 'integrity-nightly' AND status = 'ok' ORDER BY started_at DESC LIMIT 1`,
    );
    const v = r.rows[0]?.m;
    return v === undefined || v === null ? null : Number(v);
  } catch {
    return null;
  }
}

/** 규칙을 한 번 평가하고 알림을 보낸다(테스트가 직접 부른다). 보낸 알림 키 목록을 돌려준다 */
export async function watchdogTick(snapIn?: Snapshot): Promise<{ fired: string[]; resolved: string[] }> {
  const cfg = getConfig().ops;
  const snap = snapIn ?? (await collectSnapshot());
  const ready = await getDomainReady();
  readyFailStreak = ready ? 0 : readyFailStreak + 1;
  const sc = metrics.wsClose.get('SLOW_CONSUMER') ?? 0;
  const slow = sc - lastClose;
  lastClose = sc;
  const found = evaluate(
    { snap, integrityMismatches: await latestIntegrityMismatches(), memLimitMb: Number(process.env.MEMORY_LIMIT_MB) || null, readyFailStreak, slowConsumerCloses1m: slow },
    getNow(),
  );
  const fired: string[] = [];
  const resolved: string[] = [];
  const now = Date.now();
  const keys = new Set(found.map((f) => f.key));
  for (const a of found) {
    active.set(a.key, a);
    const last = lastSent.get(a.key) ?? 0;
    if (now - last >= cfg.alertMinIntervalMinutes * 60_000) {
      lastSent.set(a.key, now);
      await sendAlert(a, 'firing');
      fired.push(a.key);
    }
  }
  for (const [key, a] of [...active]) {
    if (!keys.has(key)) {
      active.delete(key);
      lastSent.delete(key);
      await sendAlert(a, 'resolved');
      resolved.push(key);
    }
  }
  if (now - lastSnapshotLogAt >= cfg.snapshotSeconds * 1000) {
    lastSnapshotLogAt = now;
    logger.info({ snapshot: snap }, 'ops.snapshot');
  }
  return { fired, resolved };
}

/** 하트비트: 이 핑이 끊기면 외부 서비스가 알린다(프로세스·호스트·네트워크 사망을 한 장치로 감지) */
async function heartbeat(): Promise<void> {
  const url = getConfig().ops.heartbeatUrl;
  if (!url) return;
  try {
    await fetch(url, { method: 'GET', signal: AbortSignal.timeout(5000) });
  } catch (err) {
    logger.error({ err }, 'heartbeat failed');
  }
}

export function resetWatchdog(): void {
  lastSent.clear();
  active.clear();
  readyFailStreak = 0;
  lastClose = 0;
  lastSnapshotLogAt = 0;
}

export function startWatchdog(): () => void {
  const cfg = getConfig().ops;
  if (!cfg.alertWebhookUrl) logger.warn('ALERT_WEBHOOK_URL 이 없어 알림은 로그에만 남깁니다');
  if (!cfg.heartbeatUrl) logger.warn('OPS_HEARTBEAT_URL 이 없어 하트비트를 보내지 않습니다');
  const timer = setInterval(() => {
    watchdogTick().catch((err: unknown) => logger.error({ err }, 'watchdog failed'));
    heartbeat().catch(() => undefined);
    metrics.resetEventLoop();
  }, 60_000);
  timer.unref();
  return () => clearInterval(timer);
}
