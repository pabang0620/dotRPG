// 판 진행 중: 하트비트(R5), 호스트 인계(R6), 방장 보고(R7), 이탈(R8)
import { getConfig } from '../../config/env';
import type { PoolClient } from 'pg';
import { AppError } from '../../utils/AppError';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import type { StoredResult } from '../economy/economyService';
import { lockPartyAndRun, runParty, withPartyLocks, type PartyRunRow } from '../party/partyTx';
import * as repo from './partyRunRepository';
import { relayHub } from '../relay/relayHub';
import { recordCardMismatch } from '../antiabuse/cardMismatch';
import { gearHashes } from '../fieldsessions/gearHash';
import { hostInfo, transportView } from './runView';
import { setPartyState } from './partyRunRepository';
import type { ClaimBody, HeartbeatBody, HostReportBody, RequestOnlyBody } from './partyRunValidation';

const notFound = () => new AppError(404, '판을 찾을 수 없습니다.', 'RUN_NOT_FOUND');

/**
 * 이탈·끊김 초과로 닫히는 멤버의 진행 중 dungeon_runs를 닫는다.
 * - 방장이 이미 클리어를 보고한 판: 클리어 뒤에 나간 것이므로 방장 보고 값으로 결과를 대신 접수한다(reported).
 *   정산은 대기 마감 뒤 서버 틱(runSettleTick)이 하고, 카드는 접속 때 자동으로 뒤집히거나 시간이 지나면 서버가 지급한다.
 * - 그 밖: abandoned(이미 받은 처치 보상은 유지, 클리어 보상 없음)
 */
export async function abandonMemberRun(client: PoolClient, partyRunId: number, member: repo.RunMemberRow, at: Date): Promise<void> {
  if (member.dungeon_run_id === null) return;
  const dr = await dungeonRepo.findRunById(client, member.dungeon_run_id);
  if (!dr || dr.state !== 'playing') return;
  const h = (await repo.hostReports(client, partyRunId))[0];
  if (h && h.outcome === 'cleared') {
    const seen = h.members.find((m) => m.character_id === member.character_uuid);
    await dungeonRepo.markReported(client, dr.id, 'cleared', {
      elapsed_ms: h.elapsed_ms,
      hits_taken: seen?.hits_taken ?? 0,
      max_combo: seen?.max_combo ?? 0,
      revives_used: seen?.revives_used ?? 0,
      reported_for_absent: true,
    }, at);
    return;
  }
  await dungeonRepo.abandonRun(client, dr.id, at);
}

// ---------- R5 POST /party-runs/{id}/heartbeat ----------

export function heartbeat(accountId: number, characterUuid: string, runUuid: string, body: HeartbeatBody) {
  const pol = getConfig().policy;
  return withPartyLocks(accountId, characterUuid, { kind: 'self' }, async (ctx) => {
    const locked = await lockPartyAndRun(ctx.client, runUuid);
    if (!locked) throw notFound();
    let run = locked.run;
    let members = await repo.runMembers(ctx.client, run.id);
    const me = members.find((m) => m.character_id === ctx.char.id);
    if (!me) throw notFound();
    const now = ctx.now;

    if (run.state === 'playing') {
      if (me.state === 'disconnected') {
        const since = me.disconnected_at ? now.getTime() - me.disconnected_at.getTime() : 0;
        if (since <= pol.partyRejoinSeconds * 1000) await repo.setMemberState(ctx.client, run.id, me.character_id, 'playing', now);
        else {
          await repo.setMemberState(ctx.client, run.id, me.character_id, 'left', now, { leftReason: 'rejoin_timeout' });
          await abandonMemberRun(ctx.client, run.id, me, now);
        }
      } else if (me.state === 'playing') {
        await repo.touchMember(ctx.client, run.id, me.character_id, now);
      }
      // 다른 멤버의 지연 전이: 12초 무신호는 끊김, 끊긴 뒤 60초가 지나면 이탈
      for (const m of members) {
        if (m.character_id === me.character_id) continue;
        if (m.state === 'playing' && m.last_seen_at && now.getTime() - m.last_seen_at.getTime() > pol.hostStaleSeconds * 1000) {
          await repo.setMemberState(ctx.client, run.id, m.character_id, 'disconnected', now);
        } else if (m.state === 'disconnected' && m.disconnected_at && now.getTime() - m.disconnected_at.getTime() > pol.partyRejoinSeconds * 1000) {
          await repo.setMemberState(ctx.client, run.id, m.character_id, 'left', now, { leftReason: 'rejoin_timeout' });
          await abandonMemberRun(ctx.client, run.id, m, now);
        }
      }
      await repo.endRunIfDone(ctx.client, run, now);
    }
    run = (await repo.getRunById(ctx.client, run.id)) as PartyRunRow;
    members = await repo.runMembers(ctx.client, run.id);
    const mine = members.find((m) => m.character_id === ctx.char.id);
    // 호스트가 멤버 카드를 믿어도 되는지 대조하는 서버 기준값(level, gear_hash)
    const hashes = await gearHashes(ctx.client, members.map((m) => m.character_id));
    return {
      state: run.state,
      host: await hostInfo(ctx.client, run, members),
      host_changed: body.seen_epoch !== run.host_epoch,
      transport: transportView(run),
      me: { state: mine?.state ?? 'left' },
      members: members.map((m) => ({ character_id: m.character_uuid, slot: m.slot, state: m.state, level: m.level, gear_hash: hashes.get(m.character_id) ?? '' })),
      server_time: now.toISOString(),
    };
  });
}

// ---------- R6 POST /party-runs/{id}/host/claim ----------

export function claimHost(accountId: number, characterUuid: string, runUuid: string, body: ClaimBody): Promise<StoredResult> {
  const pol = getConfig().policy;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party-runs/:id/host/claim',
    requestId: body.request_id,
    payload: { run_id: runUuid, observed_epoch: body.observed_epoch },
    handler: async (ctx) => {
      const locked = await lockPartyAndRun(ctx.client, runUuid);
      if (!locked) throw notFound();
      const run = locked.run;
      const members = await repo.runMembers(ctx.client, run.id);
      const me = members.find((m) => m.character_id === ctx.char.id);
      if (!me) throw notFound();
      if (run.state !== 'playing') throw new AppError(409, '진행 중인 판이 아닙니다.', 'RUN_NOT_PLAYING');
      if (body.observed_epoch !== run.host_epoch) {
        throw new AppError(409, '이미 다른 호스트가 이어받았습니다.', 'HOST_CHANGED', { host: await hostInfo(ctx.client, run, members) });
      }
      const host = members.find((m) => m.character_id === run.host_character_id);
      const stale = (m: repo.RunMemberRow | undefined): boolean =>
        !m || m.state === 'left' || m.state === 'disconnected' || !m.last_seen_at || ctx.now.getTime() - m.last_seen_at.getTime() > pol.hostStaleSeconds * 1000;
      // 중계 방: 호스트 연결이 있으면 하트비트가 늦어도 살아 있다, 연결이 grace 이상 없으면 하트비트가 신선해도 죽은 것으로 본다
      const presence = run.transport === 'relay' ? relayHub().hostPresence('run', run.uuid) : null;
      const hostAlive = presence ? (presence.connected ? !!host && host.state !== 'left' : presence.absentMs < getConfig().relay.hostGraceMs ? !stale(host) : false) : !stale(host);
      if (me.character_id === run.host_character_id || hostAlive) {
        throw new AppError(409, '호스트가 아직 살아 있습니다.', 'HOST_ALIVE');
      }
      // 살아 있는(하트비트가 신선한) 활성 멤버 중 가장 작은 자리만 이어받을 수 있다. 요청자는 방금 신호를 보낸 것으로 본다
      const fresh = members
        .filter((m) => m.character_id !== run.host_character_id && m.state === 'playing' && (m.character_id === me.character_id || !stale(m)))
        .sort((a, b) => a.slot - b.slot);
      const next = fresh[0];
      if (!next || next.character_id !== me.character_id) {
        throw new AppError(409, '다음 호스트가 아닙니다.', 'NOT_NEXT_HOST', { next_slot: next ? next.slot : null });
      }
      if (me.state !== 'playing') throw new AppError(409, '진행 중인 멤버만 이어받을 수 있습니다.', 'NOT_NEXT_HOST');
      await repo.touchMember(ctx.client, run.id, me.character_id, ctx.now);
      await repo.setHost(ctx.client, run.id, me.character_id);
      if (host && host.state !== 'left') await repo.setMemberState(ctx.client, run.id, host.character_id, 'disconnected', ctx.now);
      await ctx.client.query('UPDATE parties SET version = version + 1 WHERE id = $1', [run.party_id]);
      const fresh2 = (await repo.getRunById(ctx.client, run.id)) as PartyRunRow;
      const all = await repo.runMembers(ctx.client, run.id);
      return {
        status: 200,
        data: {
          host: await hostInfo(ctx.client, fresh2, all),
          host_key: (run.run_key as Buffer).toString('base64url'),
          members: all.map((m) => ({ character_id: m.character_uuid, slot: m.slot, state: m.state })),
        },
      };
    },
  });
}

// ---------- R7 POST /party-runs/{id}/host-report ----------

export function hostReport(accountId: number, characterUuid: string, runUuid: string, body: HostReportBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party-runs/:id/host-report',
    requestId,
    payload: { run_id: runUuid, ...payload },
    handler: async (ctx) => {
      const locked = await lockPartyAndRun(ctx.client, runUuid);
      if (!locked) throw notFound();
      const run = locked.run;
      const members = await repo.runMembers(ctx.client, run.id);
      if (!members.some((m) => m.character_id === ctx.char.id)) throw notFound();
      // 옛 세대의 보고는 거절한다(스플릿 브레인 방지)
      if (body.host_epoch !== run.host_epoch) throw new AppError(409, '호스트가 바뀌었습니다.', 'HOST_EPOCH_STALE');
      if (run.host_character_id !== ctx.char.id) throw new AppError(403, '방장만 할 수 있습니다.', 'NOT_HOST');
      // 멤버가 모두 보고해 판이 닫힌 뒤에 도착한 방장 보고도 대조에 필요하므로 ended를 허용한다
      if (run.state !== 'playing' && run.state !== 'ended') throw new AppError(409, '진행 중인 판이 아닙니다.', 'RUN_NOT_PLAYING');
      const existing = (await repo.hostReports(ctx.client, run.id)).find((r) => r.host_epoch === run.host_epoch);
      if (existing) throw new AppError(409, '이 세대의 보고가 이미 있습니다.', 'REPORT_EXISTS');
      // 판단은 정산 때 한다. 여기서는 기록만 한다
      await repo.insertHostReport(ctx.client, run.id, run.host_epoch, ctx.char.id, ctx.requestId, {
        outcome: body.outcome,
        elapsed_ms: body.elapsed_ms,
        rooms: body.rooms,
        members: body.members,
        ai: body.ai,
      }, ctx.now);
      await repo.setFirstReport(ctx.client, run.id, ctx.now);
      // 9단계 9.3: 호스트가 본 멤버 카드 불일치를 기록한다(판의 활성 멤버가 아니면 무시, 판 결과에는 쓰지 않는다)
      for (const hm of body.members) {
        if (hm.card_mismatch !== true) continue;
        const target = members.find((m) => m.character_uuid === hm.character_id && m.character_id !== ctx.char.id);
        if (target && repo.ACTIVE_STATES.includes(target.state)) {
          await recordCardMismatch(ctx.client, { accountId: target.account_id, characterId: target.character_id, characterUuid: target.character_uuid }, { kind: 'run_id', id: run.uuid });
        }
      }
      return { status: 200, data: { accepted: true, epoch: run.host_epoch } };
    },
  });
}

// ---------- R8 POST /party-runs/{id}/leave ----------

export function leaveRun(accountId: number, characterUuid: string, runUuid: string, body: RequestOnlyBody): Promise<StoredResult> {
  return runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/party-runs/:id/leave',
    requestId: body.request_id,
    payload: { run_id: runUuid },
    handler: async (ctx) => {
      const locked = await lockPartyAndRun(ctx.client, runUuid);
      if (!locked) throw notFound();
      const run = locked.run;
      const members = await repo.runMembers(ctx.client, run.id);
      const me = members.find((m) => m.character_id === ctx.char.id);
      if (!me) throw notFound();
      if (!repo.ACTIVE_STATES.includes(me.state)) return { status: 200, data: { left: true } };

      if (run.state === 'gathering') {
        if (run.host_character_id === me.character_id) {
          // 방장이 모으는 중에 나가면 판 취소(입장 횟수 소모 없음, 파티는 forming으로 복귀)
          await repo.cancelRun(ctx.client, run.id, 'host_cancel', ctx.now);
          await setPartyState(ctx.client, run.party_id, 'forming');
        } else {
          await repo.setMemberState(ctx.client, run.id, me.character_id, 'left', ctx.now, { leftReason: 'left' });
        }
      } else if (run.state === 'playing') {
        // 받은 처치 경험치·드롭은 유지. 방장이 클리어를 보고한 뒤라면 클리어 보상도 받는다(abandonMemberRun). 방장이 나가면 12초를 기다리지 않고 바로 인계할 수 있다
        await repo.setMemberState(ctx.client, run.id, me.character_id, 'left', ctx.now, { leftReason: 'left' });
        await abandonMemberRun(ctx.client, run.id, me, ctx.now);
        await repo.endRunIfDone(ctx.client, run, ctx.now);
      }
      return { status: 200, data: { left: true } };
    },
  });
}
