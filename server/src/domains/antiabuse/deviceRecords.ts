// 로그인·세션 진입의 기기·IP 기록(3.2). 클라이언트가 보낸 기기 지문은 서버 비밀(DEVICE_HASH_PEPPER)로 HMAC해서만 저장한다.
// 전부 클라이언트의 자기 신고라 단독 차단 근거로 쓰지 않는다(레이드 사람 수, 기기 한도, 의심 거래 기록, 정지 전파에만 쓴다).
import { createHmac } from 'node:crypto';
import net from 'node:net';
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';

export interface DeviceInput {
  install_id: string;
  device_hash?: string | undefined;
}

/** 한 요청에서 서버가 아는 "이 접속"의 신호 */
export interface AccessSignal {
  installId: string | null;
  /** HMAC된 기기 해시(클라이언트가 device_hash를 보냈을 때만. 리프레시 토큰 기기 결박에 쓴다) */
  deviceHash: string | null;
  /**
   * 기기 키: deviceHash가 있으면 그것, 없으면 install_id의 HMAC(대체 키). 기기 동시 접속 한도·레이드 사람 수·기기 기록이 이 키를 쓴다.
   * device_hash를 생략해서 한도를 피하는 길을 막는다(둘 다 없으면 null).
   */
  deviceKey: string | null;
  ip: string | null;
  clientVersion: string | null;
}

export type LoginKind = 'register' | 'login' | 'steam_login' | 'refresh' | 'enter';

/** SHA-256 hex(클라이언트) -> HMAC-SHA256 hex(서버 저장값). DB가 유출돼도 다른 서비스의 같은 지문과 대조되지 않는다 */
export function hmacDevice(clientHash: string): string {
  return createHmac('sha256', getConfig().aa.devicePepper).update(clientHash).digest('hex');
}

/** req.ip 형태(::ffff:1.2.3.4 포함)를 INET으로 넣을 수 있는 값으로. 알 수 없으면 null */
export function normalizeIp(ip: string | undefined | null): string | null {
  if (!ip) return null;
  const v = ip.startsWith('::ffff:') && net.isIPv4(ip.slice(7)) ? ip.slice(7) : ip;
  return net.isIP(v) !== 0 ? v : null;
}

export function accessSignal(device: DeviceInput | undefined, ip: string | undefined | null, clientVersion: string | undefined | null): AccessSignal {
  const deviceHash = device?.device_hash ? hmacDevice(device.device_hash) : null;
  return {
    installId: device?.install_id ?? null,
    deviceHash,
    deviceKey: deviceHash ?? (device?.install_id ? hmacDevice(`install:${device.install_id}`) : null),
    ip: normalizeIp(ip),
    clientVersion: clientVersion ? clientVersion.slice(0, 32) : null,
  };
}

/** /24(IPv6는 /48)로 묶은 표시용 그룹. 원본 IP를 관리자 화면에 그대로 내지 않기 위한 마스킹 */
export function ipGroup(ip: string): string {
  if (net.isIPv4(ip)) return `${ip.split('.').slice(0, 3).join('.')}.0/24`;
  const parts = ip.split(':');
  return `${parts.slice(0, 3).join(':')}::/48`;
}

export interface LoginRecordOptions {
  characterId?: number | null;
  steamId?: string | null;
  steamOwnerId?: string | null;
  flags?: string[];
}

/** login_events 한 줄 + account_devices·account_ips upsert(같은 트랜잭션). 기기를 모르면 device_missing 플래그 */
export async function recordLogin(client: Queryable, accountId: number, kind: LoginKind, s: AccessSignal, o: LoginRecordOptions = {}): Promise<void> {
  const flags = [...(o.flags ?? [])];
  if (s.deviceHash === null && !flags.includes('device_missing')) flags.push('device_missing');
  // 이 계정이 다른 기기로 접속한 적이 있는데 처음 보는 기기 키면 new_device(매번 무작위 해시를 보내는 우회도 여기에 쌓인다)
  if (s.deviceKey) {
    const seen = await client.query<{ this_seen: boolean; any_seen: boolean }>(
      'SELECT bool_or(device_hash = $2) AS this_seen, count(*) > 0 AS any_seen FROM account_devices WHERE account_id = $1',
      [accountId, s.deviceKey],
    );
    const row = seen.rows[0];
    if (row && row.any_seen && !row.this_seen && !flags.includes('new_device')) flags.push('new_device');
  }
  await client.query(
    `INSERT INTO login_events (account_id, character_id, kind, install_id, device_hash, ip, steam_id, steam_owner_id, client_version, flags)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)`,
    [accountId, o.characterId ?? null, kind, s.installId, s.deviceKey, s.ip, o.steamId ?? null, o.steamOwnerId ?? null, s.clientVersion, flags],
  );
  if (s.deviceKey) {
    await client.query(
      `INSERT INTO account_devices (account_id, device_hash) VALUES ($1, $2)
       ON CONFLICT (account_id, device_hash) DO UPDATE SET last_seen_at = now(), seen_count = account_devices.seen_count + 1`,
      [accountId, s.deviceKey],
    );
  }
  if (s.ip) {
    await client.query(
      `INSERT INTO account_ips (account_id, ip) VALUES ($1, $2)
       ON CONFLICT (account_id, ip) DO UPDATE SET last_seen_at = now(), seen_count = account_ips.seen_count + 1`,
      [accountId, s.ip],
    );
  }
}

/** 리프레시는 직전 기록의 (기기, IP)와 다를 때만 남긴다(15분마다 오는 요청의 쓰기 증폭 방지) */
export async function recordRefreshIfChanged(client: PoolClient, accountId: number, s: AccessSignal, flags: string[] = []): Promise<boolean> {
  const last = await client.query<{ device_hash: string | null; ip: string | null }>(
    'SELECT device_hash, host(ip) AS ip FROM login_events WHERE account_id = $1 ORDER BY id DESC LIMIT 1',
    [accountId],
  );
  const row = last.rows[0];
  if (row && row.device_hash === s.deviceKey && row.ip === s.ip && flags.length === 0) return false;
  await recordLogin(client, accountId, 'refresh', s, { flags });
  return true;
}
