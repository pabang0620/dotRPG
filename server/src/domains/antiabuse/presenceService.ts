// 프레즌스(4절): "온라인"의 정의, 활동 시간(서버 시계), 기기 동시 접속 제한, IP 감시 점수.
// 프레즌스도 클라이언트의 자기 신고라 흉내 낼 수 있다. 이것은 맵 위조와 무신호 HTTP 봇을 막는 비용 상승이고, 진짜 방어선은 기여 판정과 경제 속도 정지다.
import { createHash, randomUUID } from 'node:crypto';
import { getConfig } from '../../config/env';
import { getPool, withTransaction, type Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import { metrics } from '../../ops/metrics';
import * as econRepo from '../economy/economyRepository';
import { accessSignal, ipGroup, normalizeIp, recordLogin } from './deviceRecords';
import { deliverCampaignsInBackground } from '../mail/campaignDelivery';
import { hourStart } from './incomeMeter';
import * as repo from './presenceRepository';
import type { OnlineRow } from './presenceRepository';
import type { PresenceBody } from './presenceValidation';

const NOT_FOUND = () => new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');

/** 온라인 = 행이 있고, 끝나지 않았고, 마지막 신호가 seconds 안이다 */
export function isFresh(row: OnlineRow | null, now: Date, seconds: number): row is OnlineRow {
  return row !== null && row.ended_at === null && now.getTime() - row.last_seen_at.getTime() <= seconds * 1000;
}

async function ownedChar(db: Queryable, accountId: number, uuid: string): Promise<{ id: number; level: number }> {
  const r = await db.query<{ id: string; level: number }>('SELECT id, level FROM characters WHERE uuid = $1 AND account_id = $2 AND deleted_at IS NULL', [uuid, accountId]);
  const row = r.rows[0];
  if (!row) throw NOT_FOUND();
  return { id: Number(row.id), level: row.level };
}

export interface PresenceResult {
  server_time: string;
  interval_seconds: number;
  counted: boolean;
  device: { online: number; max: number } | null;
}

/** P1: 생존 신호. 세션 진입 겸용(행이 없거나 끝났거나 신선하지 않거나 다른 캐릭터면 "진입") */
export async function sendPresence(accountId: number, characterUuid: string, body: PresenceBody, ip: string, clientVersion?: string): Promise<PresenceResult> {
  const cfg = getConfig().aa;
  const pc = cfg.presence;
  if (!getGameData().maps.has(body.map_id)) throw new AppError(422, '알 수 없는 맵입니다.', 'INVALID_MAP');
  let entered: number | null = null;
  const result = await withTransaction(async (client) => {
    const char = await ownedChar(client, accountId, characterUuid);
    const now = getNow();
    const acct = await repo.accountSession(client, accountId);
    const row = await repo.lockOnline(client, accountId);
    const normIp = normalizeIp(ip);
    const entering = !isFresh(row, now, pc.onlineSeconds) || row.character_id !== char.id;
    let counted = false;

    if (entering) {
      const deviceHash = acct.active_device_hash;
      if (deviceHash && cfg.device.mode !== 'off') {
        // 같은 기기끼리만 직렬화한다(동시에 세 번째 자리를 노리는 요청 중 하나만 통과)
        await client.query('SELECT pg_advisory_xact_lock(hashtextextended($1, 0))', [`device:${deviceHash}`]);
        const others = await repo.countOthersOnDevice(client, deviceHash, accountId, new Date(now.getTime() - pc.onlineSeconds * 1000));
        if (others >= cfg.device.maxConcurrent) {
          metrics.presenceDeviceLimitHits++;
          if (cfg.device.mode === 'enforce') {
            throw new AppError(409, '이 PC에서 동시에 접속할 수 있는 수를 넘었습니다.', 'DEVICE_LIMIT', { limit: cfg.device.maxConcurrent, online_on_device: others });
          }
          await econRepo.insertAnomaly(client, accountId, char.id, 'device_limit', 2, { device_slot_count: others + 1, limit: cfg.device.maxConcurrent });
        }
      }
      if (normIp) {
        const onIp = await repo.countOthersOnIp(client, normIp, accountId, new Date(now.getTime() - pc.onlineSeconds * 1000));
        if (onIp + 1 >= cfg.device.ipWatchConcurrent) {
          const group = createHash('sha256').update(ipGroup(normIp)).digest('hex').slice(0, 8);
          const hour = hourStart(now).toISOString();
          if (!(await repo.ipClusterLogged(client, group, hour))) {
            await econRepo.insertAnomaly(client, accountId, char.id, 'ip_cluster', 1, { ip_group: group, online: onIp + 1, hour });
          }
        }
      }
      await repo.upsertEnter(client, {
        accountId,
        characterId: char.id,
        familyId: acct.active_family_id ?? randomUUID(),
        installId: acct.active_install_id,
        deviceHash: acct.active_device_hash,
        ip: normIp,
        mapId: body.map_id,
        autoPlay: body.auto_play,
        inputRecent: body.input_recent,
        now,
      });
      await recordLogin(client, accountId, 'enter', { installId: acct.active_install_id, deviceHash: acct.active_device_hash, deviceKey: acct.active_device_hash, ip: normIp, clientVersion: clientVersion ? clientVersion.slice(0, 32) : null }, { characterId: char.id });
    } else {
      const prev = row as OnlineRow;
      const gapMs = now.getTime() - prev.last_seen_at.getTime();
      const gapSec = Math.floor(gapMs / 1000);
      let resetMapSince = false;
      let touchSeen = true;
      if (gapSec > pc.maxGapSeconds) {
        // 신호가 끊겼다: 활동 시간에 더하지 않고 같은 맵 체류(보스 체류 확인)를 처음부터 센다
        resetMapSince = true;
      } else if (gapSec < pc.minGapSeconds) {
        // 너무 촘촘한 신호: 상태만 갱신한다. last_seen_at을 올리지 않아 다음 신호가 이 시간까지 합쳐서 센다(벽시계보다 많이 쌓일 수 없다)
        touchSeen = false;
      } else {
        const sec = Math.min(gapSec, pc.maxGapSeconds);
        // 시간은 이전 신호 때의 자기 신고 상태가 아니라 지금 신호의 상태로 나눈다(마지막 값이 이긴다)
        await repo.addPlayTime(client, char.id, hourStart(now), {
          active: sec,
          auto: body.auto_play ? sec : 0,
          unattended: body.auto_play && !body.input_recent ? sec : 0,
        });
        counted = true;
      }
      const unattended = body.auto_play && !body.input_recent ? (prev.unattended_since ?? now) : null;
      if (unattended && now.getTime() - unattended.getTime() > pc.unattendedMaxSeconds * 1000) {
        const already = prev.unattended_since !== null && prev.last_seen_at.getTime() - prev.unattended_since.getTime() > pc.unattendedMaxSeconds * 1000;
        if (!already) logger.warn({ account: accountId, character: char.id }, 'anti_abuse.unattended');
      }
      await repo.updateBeat(client, {
        accountId,
        mapId: body.map_id,
        autoPlay: body.auto_play,
        inputRecent: body.input_recent,
        unattendedSince: unattended,
        mapChanged: body.map_id !== prev.map_id,
        resetMapSince,
        touchSeen,
        now,
      });
    }

    let device: PresenceResult['device'] = null;
    if (acct.active_device_hash) {
      const others = await repo.countOthersOnDevice(client, acct.active_device_hash, accountId, new Date(now.getTime() - pc.onlineSeconds * 1000));
      device = { online: others + 1, max: cfg.device.maxConcurrent };
    }
    if (entering) entered = char.id;
    return { server_time: now.toISOString(), interval_seconds: pc.intervalSeconds, counted, device };
  });
  // 10단계 E12: 세션 진입이 커밋된 뒤 캠페인 우편 배달(실패해도 이 응답에 영향이 없다)
  if (entered !== null) deliverCampaignsInBackground(accountId, entered);
  return result;
}

/** P2: 접속 종료 알림(기기 칸 반환). 이미 끝났거나 다른 캐릭터면 아무것도 하지 않고 성공(멱등) */
export async function leavePresence(accountId: number, characterUuid: string): Promise<{ left: true }> {
  const char = await ownedChar(getPool(), accountId, characterUuid);
  await repo.endSession(getPool(), accountId, char.id, 'leave', getNow());
  return { left: true };
}

/** 필드 세션 입장·호스트 인계가 서버에서 맵을 확정하는 순간 프레즌스 맵도 맞춘다(같은 트랜잭션) */
export async function touchMap(db: Queryable, characterId: number, mapId: string, now: Date): Promise<void> {
  await repo.touchMap(db, characterId, mapId, now);
}

/** presence-sweep 작업: 신선도를 잃은 활성 행을 닫는다 */
export async function sweepPresence(): Promise<number> {
  const olderThan = new Date(getNow().getTime() - getConfig().aa.presence.onlineSeconds * 1000);
  return repo.sweepStale(getPool(), olderThan);
}
