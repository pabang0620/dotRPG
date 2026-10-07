// 프레즌스 SQL(online_sessions, play_time_hourly). 변경은 한 문장 upsert로만 한다(캐릭터 행을 잠그지 않는 경로라 교착이 없다).
import type { Queryable } from '../../db/pool';

export interface OnlineRow {
  account_id: number;
  character_id: number;
  family_id: string;
  map_id: string;
  prev_map_id: string | null;
  map_since: Date;
  map_changed_at: Date;
  auto_play: boolean;
  input_recent: boolean;
  unattended_since: Date | null;
  last_seen_at: Date;
  ended_at: Date | null;
}

interface RawOnline extends Omit<OnlineRow, 'account_id' | 'character_id'> {
  account_id: string;
  character_id: string;
}

const COLS = `account_id, character_id, family_id, map_id, prev_map_id, map_since, map_changed_at, auto_play, input_recent,
              unattended_since, last_seen_at, ended_at`;

const toRow = (r: RawOnline): OnlineRow => ({ ...r, account_id: Number(r.account_id), character_id: Number(r.character_id) });

/** 계정의 온라인 행을 잠그고 읽는다(같은 계정의 프레즌스 요청끼리 직렬화) */
export async function lockOnline(db: Queryable, accountId: number): Promise<OnlineRow | null> {
  const r = await db.query<RawOnline>(`SELECT ${COLS} FROM online_sessions WHERE account_id = $1 FOR UPDATE`, [accountId]);
  return r.rows[0] ? toRow(r.rows[0]) : null;
}

/** 읽기만(처치 보고의 맵 확인) */
export async function readOnline(db: Queryable, accountId: number): Promise<OnlineRow | null> {
  const r = await db.query<RawOnline>(`SELECT ${COLS} FROM online_sessions WHERE account_id = $1`, [accountId]);
  return r.rows[0] ? toRow(r.rows[0]) : null;
}

export interface AccountSession {
  active_family_id: string | null;
  active_install_id: string | null;
  active_device_hash: string | null;
}

export async function accountSession(db: Queryable, accountId: number): Promise<AccountSession> {
  const r = await db.query<AccountSession>('SELECT active_family_id, active_install_id, active_device_hash FROM accounts WHERE id = $1', [accountId]);
  return r.rows[0] as AccountSession;
}

/** 같은 기기에서 지금 온라인인 다른 계정 수(기기 한도 판정) */
export async function countOthersOnDevice(db: Queryable, deviceHash: string, accountId: number, freshSince: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM online_sessions
      WHERE device_hash = $1 AND account_id <> $2 AND ended_at IS NULL AND last_seen_at > $3`,
    [deviceHash, accountId, freshSince],
  );
  return Number((r.rows[0] as { n: string }).n);
}

export async function countOthersOnIp(db: Queryable, ip: string, accountId: number, freshSince: Date): Promise<number> {
  const r = await db.query<{ n: string }>(
    `SELECT count(*) AS n FROM online_sessions
      WHERE ip = $1 AND account_id <> $2 AND ended_at IS NULL AND last_seen_at > $3`,
    [ip, accountId, freshSince],
  );
  return Number((r.rows[0] as { n: string }).n);
}

/** 진입: 행을 새로 만든다(옛 행이 있으면 덮어쓴다) */
export async function upsertEnter(
  db: Queryable,
  p: { accountId: number; characterId: number; familyId: string; installId: string | null; deviceHash: string | null; ip: string | null; mapId: string; autoPlay: boolean; inputRecent: boolean; now: Date },
): Promise<void> {
  await db.query(
    `INSERT INTO online_sessions (account_id, character_id, family_id, install_id, device_hash, ip, map_id, prev_map_id, map_since, map_changed_at,
                                  auto_play, input_recent, unattended_since, started_at, last_seen_at, ended_at, end_reason)
     VALUES ($1, $2, $3, $4, $5, $6, $7, NULL, $10, $10, $8, $9, CASE WHEN $8 AND NOT $9 THEN $10::timestamptz END, $10, $10, NULL, NULL)
     ON CONFLICT (account_id) DO UPDATE SET
       character_id = EXCLUDED.character_id, family_id = EXCLUDED.family_id, install_id = EXCLUDED.install_id,
       device_hash = EXCLUDED.device_hash, ip = EXCLUDED.ip, map_id = EXCLUDED.map_id, prev_map_id = NULL,
       map_since = EXCLUDED.map_since, map_changed_at = EXCLUDED.map_changed_at, auto_play = EXCLUDED.auto_play,
       input_recent = EXCLUDED.input_recent, unattended_since = EXCLUDED.unattended_since, started_at = EXCLUDED.started_at,
       last_seen_at = EXCLUDED.last_seen_at, ended_at = NULL, end_reason = NULL`,
    [p.accountId, p.characterId, p.familyId, p.installId, p.deviceHash, p.ip, p.mapId, p.autoPlay, p.inputRecent, p.now],
  );
}

/** 진입이 아닌 신호: 상태를 쓴다. touchSeen=false면 last_seen_at을 올리지 않는다(5초 안의 촘촘한 신호) */
export async function updateBeat(
  db: Queryable,
  p: { accountId: number; mapId: string; autoPlay: boolean; inputRecent: boolean; unattendedSince: Date | null; mapChanged: boolean; resetMapSince: boolean; touchSeen: boolean; now: Date },
): Promise<void> {
  await db.query(
    `UPDATE online_sessions SET
       prev_map_id = CASE WHEN $3 THEN map_id ELSE prev_map_id END,
       map_changed_at = CASE WHEN $3 THEN $9::timestamptz ELSE map_changed_at END,
       map_since = CASE WHEN $3 OR $4 THEN $9::timestamptz ELSE map_since END,
       map_id = $2, auto_play = $5, input_recent = $6, unattended_since = $7,
       last_seen_at = CASE WHEN $8 THEN $9::timestamptz ELSE last_seen_at END
     WHERE account_id = $1`,
    [p.accountId, p.mapId, p.mapChanged, p.resetMapSince, p.autoPlay, p.inputRecent, p.unattendedSince, p.touchSeen, p.now],
  );
}

/** 활동 시간 버킷에 더한다. 한 시간 버킷의 상한(3600초)을 넘지 않게 자른다 */
export async function addPlayTime(
  db: Queryable,
  characterId: number,
  hour: Date,
  seconds: { active: number; auto: number; unattended: number },
): Promise<void> {
  await db.query(
    `INSERT INTO play_time_hourly (character_id, hour_start, active_seconds, auto_seconds, unattended_seconds, beats)
     VALUES ($1, $2, LEAST($3, 3600), LEAST($4, 3600), LEAST($5, 3600), 1)
     ON CONFLICT (character_id, hour_start) DO UPDATE SET
       active_seconds = LEAST(3600, play_time_hourly.active_seconds + EXCLUDED.active_seconds),
       auto_seconds = LEAST(3600, play_time_hourly.auto_seconds + EXCLUDED.auto_seconds),
       unattended_seconds = LEAST(3600, play_time_hourly.unattended_seconds + EXCLUDED.unattended_seconds),
       beats = LEAST(32767, play_time_hourly.beats + 1)`,
    [characterId, hour, seconds.active, seconds.auto, seconds.unattended],
  );
}

export async function endSession(db: Queryable, accountId: number, characterId: number, reason: 'leave' | 'timeout', now: Date): Promise<boolean> {
  const r = await db.query(
    `UPDATE online_sessions SET ended_at = $3, end_reason = $4 WHERE account_id = $1 AND character_id = $2 AND ended_at IS NULL`,
    [accountId, characterId, now, reason],
  );
  return (r.rowCount ?? 0) > 0;
}

/** 필드 세션 입장·호스트 인계가 서버에서 맵을 확정하는 순간에 프레즌스 맵을 맞춘다(9단계 E4) */
export async function touchMap(db: Queryable, characterId: number, mapId: string, now: Date): Promise<void> {
  await db.query(
    `UPDATE online_sessions SET prev_map_id = map_id, map_id = $2, map_changed_at = $3, map_since = $3
      WHERE character_id = $1 AND ended_at IS NULL AND map_id <> $2`,
    [characterId, mapId, now],
  );
}

/** presence-sweep: 신선도를 잃은 활성 행을 닫는다 */
export async function sweepStale(db: Queryable, olderThan: Date): Promise<number> {
  const r = await db.query(
    `UPDATE online_sessions SET ended_at = last_seen_at, end_reason = 'timeout' WHERE ended_at IS NULL AND last_seen_at < $1`,
    [olderThan],
  );
  return r.rowCount ?? 0;
}

/** 같은 IP·같은 시간대에 이미 ip_cluster 기록이 있는가(시간대당 한 번만 남긴다) */
export async function ipClusterLogged(db: Queryable, group: string, hour: string): Promise<boolean> {
  const r = await db.query(
    "SELECT 1 FROM anomaly_log WHERE kind = 'ip_cluster' AND detail->>'ip_group' = $1 AND detail->>'hour' = $2 LIMIT 1",
    [group, hour],
  );
  return r.rows.length > 0;
}
